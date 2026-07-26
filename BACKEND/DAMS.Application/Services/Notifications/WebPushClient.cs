using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DAMS.Application.Interfaces;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// Sends a browser push message directly to the subscriber's push service, implementing
    /// the two standards a browser requires:
    ///
    /// * RFC 8291 — the payload is encrypted end to end with a key only that browser holds,
    ///   so the push service itself (Google, Mozilla, Microsoft) never sees the content.
    /// * RFC 8292 — each request is signed with the DAMS VAPID key, so a push service can
    ///   attribute the message and rate-limit per application.
    ///
    /// Written against the standards rather than a third-party client so the private key
    /// never leaves this process and there is no unmaintained dependency in the delivery path.
    /// </summary>
    public sealed class WebPushClient : IWebPushSender, IDisposable
    {
        private const int RecordSize = 4096;
        private const int PublicKeyLength = 65;

        private readonly HttpClient _http;

        public WebPushClient(HttpClient? http = null)
        {
            _http = http ?? new HttpClient(new HttpClientHandler
            {
                // A validated push-service URL must never redirect the server into a private
                // network or to a different origin.
                AllowAutoRedirect = false
            })
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
        }

        public async Task<WebPushResult> SendAsync(
            WebPushTarget target, string payloadJson, WebPushCredentials credentials, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!Uri.TryCreate(target.Endpoint, UriKind.Absolute, out var endpoint)
                    || endpoint.Scheme != Uri.UriSchemeHttps)
                {
                    return new WebPushResult
                    {
                        Success = false,
                        Error = "The push endpoint is not a valid https address.",
                        SubscriptionGone = true
                    };
                }

                var body = Encrypt(Encoding.UTF8.GetBytes(payloadJson), target.P256dh, target.Auth);
                var authorization = BuildVapidAuthorization(endpoint, credentials);

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new ByteArrayContent(body)
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                request.Content.Headers.ContentEncoding.Add("aes128gcm");
                request.Headers.TryAddWithoutValidation("TTL", "86400");
                request.Headers.TryAddWithoutValidation("Urgency", "normal");
                request.Headers.TryAddWithoutValidation("Authorization", authorization);

                using var response = await _http.SendAsync(request, cancellationToken);
                var status = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    // Push services return the message id in Location when they issue one.
                    var reference = response.Headers.Location?.ToString();
                    return new WebPushResult { Success = true, StatusCode = status, Error = reference };
                }

                // 404/410 is the browser telling us the subscription no longer exists. It is a
                // normal end of life, not a fault, and the row must be deactivated.
                var gone = response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone;
                var detail = await SafeReadAsync(response, cancellationToken);

                return new WebPushResult
                {
                    Success = false,
                    StatusCode = status,
                    SubscriptionGone = gone,
                    Error = gone
                        ? "The browser discarded this subscription."
                        : $"Push service responded {status}. {detail}".Trim()
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new WebPushResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// RFC 8291 §3.4. The result is the complete aes128gcm body:
        /// salt(16) ‖ record size(4) ‖ key id length(1) ‖ server public key(65) ‖ ciphertext.
        /// </summary>
        internal static byte[] Encrypt(byte[] payload, string clientPublicKeyBase64Url, string authSecretBase64Url)
        {
            var clientPublicKey = Base64Url.Decode(clientPublicKeyBase64Url);
            var authSecret = Base64Url.Decode(authSecretBase64Url);

            if (clientPublicKey.Length != PublicKeyLength || clientPublicKey[0] != 0x04)
                throw new CryptographicException("The subscription public key is not a valid uncompressed P-256 point.");
            if (authSecret.Length != 16)
                throw new CryptographicException("The subscription auth secret must be 16 bytes.");

            using var serverKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var serverParameters = serverKey.ExportParameters(false);
            var serverPublicKey = ToUncompressedPoint(serverParameters.Q);

            using var clientKey = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = clientPublicKey[1..33],
                    Y = clientPublicKey[33..65]
                }
            });

            var sharedSecret = serverKey.DeriveRawSecretAgreement(clientKey.PublicKey);

            // The key derivation deliberately binds both public keys, so a message encrypted
            // for one subscription can never decrypt on another.
            var keyInfo = Concat(
                Encoding.ASCII.GetBytes("WebPush: info"),
                new byte[] { 0x00 },
                clientPublicKey,
                serverPublicKey);

            var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, authSecret, keyInfo);

            var salt = RandomNumberGenerator.GetBytes(16);
            var contentEncryptionKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt,
                Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
            var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt,
                Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

            // 0x02 marks the final record; there is only ever one for a notification payload.
            var plaintext = Concat(payload, new byte[] { 0x02 });
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];

            using (var aes = new AesGcm(contentEncryptionKey, tag.Length))
                aes.Encrypt(nonce, plaintext, ciphertext, tag);

            var header = new byte[16 + 4 + 1 + PublicKeyLength];
            salt.CopyTo(header, 0);
            BinaryPrimitivesWriteUInt32BigEndian(header.AsSpan(16, 4), RecordSize);
            header[20] = PublicKeyLength;
            serverPublicKey.CopyTo(header, 21);

            return Concat(header, ciphertext, tag);
        }

        /// <summary>RFC 8292: an ES256 JWT bound to the push service origin, plus the public key.</summary>
        internal static string BuildVapidAuthorization(Uri endpoint, WebPushCredentials credentials)
        {
            var privateKey = Base64Url.Decode(credentials.PrivateKey);
            var publicKey = Base64Url.Decode(credentials.PublicKey);

            if (privateKey.Length != 32)
                throw new CryptographicException("The VAPID private key must be a 32-byte P-256 scalar.");
            if (publicKey.Length != PublicKeyLength || publicKey[0] != 0x04)
                throw new CryptographicException("The VAPID public key must be an uncompressed P-256 point.");

            using var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = privateKey,
                Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] }
            });

            var header = Base64Url.Encode(Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"));
            var claims = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["aud"] = endpoint.GetLeftPart(UriPartial.Authority),
                // Well inside the 24-hour maximum, and long enough that a retry hours later
                // still authenticates.
                ["exp"] = DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeSeconds(),
                ["sub"] = credentials.Subject
            });

            var payload = Base64Url.Encode(Encoding.UTF8.GetBytes(claims));
            var signingInput = Encoding.ASCII.GetBytes($"{header}.{payload}");
            var signature = Base64Url.Encode(ecdsa.SignData(signingInput, HashAlgorithmName.SHA256));

            return $"vapid t={header}.{payload}.{signature}, k={Base64Url.Encode(publicKey)}";
        }

        /// <summary>Creates a fresh VAPID key pair for an admin to store.</summary>
        public static (string PublicKey, string PrivateKey) GenerateVapidKeys()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var parameters = key.ExportParameters(true);

            return (Base64Url.Encode(ToUncompressedPoint(parameters.Q)),
                    Base64Url.Encode(LeftPad(parameters.D!, 32)));
        }

        private static byte[] ToUncompressedPoint(ECPoint point)
        {
            var x = LeftPad(point.X!, 32);
            var y = LeftPad(point.Y!, 32);
            var result = new byte[PublicKeyLength];
            result[0] = 0x04;
            x.CopyTo(result, 1);
            y.CopyTo(result, 33);
            return result;
        }

        /// <summary>Curve coordinates are fixed width; a leading zero byte must not be lost.</summary>
        private static byte[] LeftPad(byte[] value, int length)
        {
            if (value.Length == length)
                return value;
            if (value.Length > length)
                return value[^length..];

            var padded = new byte[length];
            value.CopyTo(padded, length - value.Length);
            return padded;
        }

        private static void BinaryPrimitivesWriteUInt32BigEndian(Span<byte> destination, uint value)
        {
            destination[0] = (byte)(value >> 24);
            destination[1] = (byte)(value >> 16);
            destination[2] = (byte)(value >> 8);
            destination[3] = (byte)value;
        }

        private static byte[] Concat(params byte[][] parts)
        {
            var result = new byte[parts.Sum(p => p.Length)];
            var offset = 0;
            foreach (var part in parts)
            {
                part.CopyTo(result, offset);
                offset += part.Length;
            }
            return result;
        }

        private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                return text.Length <= 200 ? text : text[..200];
            }
            catch
            {
                return string.Empty;
            }
        }

        public void Dispose() => _http.Dispose();
    }

    /// <summary>base64url without padding — the encoding every web-push value uses.</summary>
    public static class Base64Url
    {
        public static string Encode(byte[] value) =>
            Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static byte[] Decode(string value)
        {
            var normalized = value.Trim().Replace('-', '+').Replace('_', '/');
            var padding = normalized.Length % 4;
            if (padding == 2) normalized += "==";
            else if (padding == 3) normalized += "=";
            else if (padding == 1) throw new FormatException("The value is not valid base64url.");

            return Convert.FromBase64String(normalized);
        }
    }
}
