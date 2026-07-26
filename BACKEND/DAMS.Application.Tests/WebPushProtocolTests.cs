using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DAMS.Application.Interfaces;
using DAMS.Application.Services.Notifications;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// The browser-push wire format. These are the one part of delivery that cannot be checked
/// against a real service without a browser, so they are checked against the standards
/// instead: RFC 8291 for the encrypted payload and RFC 8292 for the VAPID authorisation.
///
/// The decryption here is deliberately written from the specification rather than reusing the
/// sender's own helpers, so a mistake in the sender cannot cancel itself out.
/// </summary>
public sealed class WebPushProtocolTests
{
    [Fact]
    public void AnEncryptedPayloadIsShapedExactlyAsTheStandardRequires()
    {
        var (clientPublic, _, auth) = NewSubscription();
        var body = WebPushClient.Encrypt(Encoding.UTF8.GetBytes("{\"title\":\"hello\"}"),
            Base64Url.Encode(clientPublic), Base64Url.Encode(auth));

        // salt(16) ‖ record size(4) ‖ key id length(1) ‖ server public key(65) ‖ ciphertext+tag
        Assert.True(body.Length > 86);
        Assert.Equal(4096u, (uint)((body[16] << 24) | (body[17] << 16) | (body[18] << 8) | body[19]));
        Assert.Equal(65, body[20]);
        Assert.Equal(0x04, body[21]);
    }

    [Fact]
    public void OnlyTheSubscribedBrowserCanDecryptThePayload()
    {
        var (clientPublic, clientPrivate, auth) = NewSubscription();
        const string plaintext = "{\"title\":\"Payment received\",\"body\":\"Open DAMS to view your receipt.\"}";

        var body = WebPushClient.Encrypt(Encoding.UTF8.GetBytes(plaintext),
            Base64Url.Encode(clientPublic), Base64Url.Encode(auth));

        var decrypted = Decrypt(body, clientPublic, clientPrivate, auth);
        Assert.Equal(plaintext, decrypted);

        // A different subscription's keys must not open it.
        var (_, otherPrivate, otherAuth) = NewSubscription();
        Assert.ThrowsAny<CryptographicException>(() => { Decrypt(body, clientPublic, otherPrivate, auth); });
        Assert.ThrowsAny<CryptographicException>(() => { Decrypt(body, clientPublic, clientPrivate, otherAuth); });
    }

    [Fact]
    public void TheVapidHeaderIsAVerifiableEs256TokenBoundToThePushServiceOrigin()
    {
        var (publicKey, privateKey) = WebPushClient.GenerateVapidKeys();
        var endpoint = new Uri("https://fcm.googleapis.com/fcm/send/abc123");

        var header = WebPushClient.BuildVapidAuthorization(endpoint,
            new WebPushCredentials(publicKey, privateKey, "mailto:admin@dams.test"));

        Assert.StartsWith("vapid t=", header);
        Assert.Contains($", k={publicKey}", header);

        var token = header["vapid t=".Length..].Split(',')[0];
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);

        var claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            Encoding.UTF8.GetString(Base64Url.Decode(parts[1])))!;

        // Bound to the push service, not to DAMS: that is what stops the token being replayed
        // against a different service.
        Assert.Equal("https://fcm.googleapis.com", claims["aud"].GetString());
        Assert.Equal("mailto:admin@dams.test", claims["sub"].GetString());
        Assert.True(claims["exp"].GetInt64() > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Assert.True(claims["exp"].GetInt64() < DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds());

        var raw = Base64Url.Decode(publicKey);
        using var verifier = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = raw[1..33], Y = raw[33..65] }
        });

        Assert.True(verifier.VerifyData(
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
            Base64Url.Decode(parts[2]),
            HashAlgorithmName.SHA256));
    }

    [Fact]
    public void AMalformedSubscriptionIsRejectedRatherThanProducingGarbage()
    {
        var (clientPublic, _, auth) = NewSubscription();

        Assert.Throws<CryptographicException>(() =>
            WebPushClient.Encrypt(new byte[] { 1 }, Base64Url.Encode(new byte[10]), Base64Url.Encode(auth)));

        Assert.Throws<CryptographicException>(() =>
            WebPushClient.Encrypt(new byte[] { 1 }, Base64Url.Encode(clientPublic), Base64Url.Encode(new byte[5])));
    }

    [Fact]
    public void Base64UrlRoundTripsWithoutPadding()
    {
        foreach (var length in new[] { 1, 2, 3, 16, 32, 65 })
        {
            var bytes = RandomNumberGenerator.GetBytes(length);
            var encoded = Base64Url.Encode(bytes);

            Assert.DoesNotContain('=', encoded);
            Assert.DoesNotContain('+', encoded);
            Assert.DoesNotContain('/', encoded);
            Assert.Equal(bytes, Base64Url.Decode(encoded));
        }
    }

    // ── A browser's side of RFC 8291, written straight from the specification ───────

    private static (byte[] Public, ECDiffieHellman Key, byte[] Auth) NewSubscription()
    {
        var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(false);

        var point = new byte[65];
        point[0] = 0x04;
        Pad(parameters.Q.X!).CopyTo(point, 1);
        Pad(parameters.Q.Y!).CopyTo(point, 33);

        return (point, key, RandomNumberGenerator.GetBytes(16));
    }

    private static string Decrypt(byte[] body, byte[] clientPublic, ECDiffieHellman clientKey, byte[] auth)
    {
        var salt = body[..16];
        var serverPublic = body[21..86];
        var ciphertext = body[86..];

        using var server = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = serverPublic[1..33], Y = serverPublic[33..65] }
        });

        var shared = clientKey.DeriveRawSecretAgreement(server.PublicKey);

        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info"), new byte[] { 0x00 }, clientPublic, serverPublic);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, auth, keyInfo);

        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt,
            Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt,
            Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var tag = ciphertext[^16..];
        var payload = ciphertext[..^16];
        var plaintext = new byte[payload.Length];

        using var aes = new AesGcm(cek, 16);
        aes.Decrypt(nonce, payload, tag, plaintext);

        // The final byte is the record delimiter, not content.
        return Encoding.UTF8.GetString(plaintext[..^1]);
    }

    private static byte[] Pad(byte[] value)
    {
        if (value.Length == 32) return value;
        var padded = new byte[32];
        value.CopyTo(padded, 32 - value.Length);
        return padded;
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
}
