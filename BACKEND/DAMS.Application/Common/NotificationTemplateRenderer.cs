using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DAMS.Application.Common
{
    /// <summary>Raised when a template is rejected on save.</summary>
    public sealed class NotificationTemplateException : Exception
    {
        public NotificationTemplateException(string message) : base(message) { }
    }

    /// <summary>
    /// Turns an admin-editable template into the text that actually goes out.
    ///
    /// Two rules make this safe. First, only variables approved for that notification type
    /// may appear, and they are checked when the template is saved — not silently dropped at
    /// send time. Second, every substituted value is HTML-encoded and the template's own
    /// markup is reduced to a formatting-only allowlist, so neither an admin nor a customer's
    /// own name can inject script into somebody else's mailbox.
    /// </summary>
    public static partial class NotificationTemplateRenderer
    {
        [GeneratedRegex(@"\{\{\s*([a-zA-Z][a-zA-Z0-9_]*)\s*\}\}", RegexOptions.CultureInvariant)]
        private static partial Regex PlaceholderRegex();

        [GeneratedRegex(@"<[^>]*>", RegexOptions.CultureInvariant)]
        private static partial Regex TagRegex();

        [GeneratedRegex(@"[ \t]{2,}", RegexOptions.CultureInvariant)]
        private static partial Regex RunOfSpacesRegex();

        /// <summary>Markup an admin may use in a template body. Everything else is stripped.</summary>
        private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "b", "strong", "i", "em", "u", "p", "br", "ul", "ol", "li", "a",
            "h1", "h2", "h3", "h4", "span", "div", "hr", "blockquote", "small"
        };

        private static readonly string[] DangerousMarkers =
        {
            "<script", "</script", "<iframe", "<object", "<embed", "<form", "<style", "<link",
            "javascript:", "vbscript:", "data:text/html", "srcdoc", "<meta", "<base"
        };

        /// <summary>
        /// Checks a template before it is stored. Rejects unsafe markup and any variable that
        /// is not approved for this notification type.
        /// </summary>
        public static void Validate(string? content, IReadOnlyCollection<string> allowedVariables, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(content))
                return;

            var lowered = content.ToLowerInvariant();
            foreach (var marker in DangerousMarkers)
            {
                if (lowered.Contains(marker, StringComparison.Ordinal))
                    throw new NotificationTemplateException(
                        $"{fieldName} contains content that is not allowed in a notification template ('{marker}').");
            }

            // Inline event handlers are the other way script gets in.
            if (Regex.IsMatch(content, @"<[^>]+\bon[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new NotificationTemplateException($"{fieldName} may not contain event handler attributes.");

            var unknown = PlaceholderRegex().Matches(content)
                .Select(m => m.Groups[1].Value)
                .Where(name => !allowedVariables.Contains(name, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (unknown.Count > 0)
                throw new NotificationTemplateException(
                    $"{fieldName} uses variables that are not available for this notification: {string.Join(", ", unknown)}.");
        }

        /// <summary>
        /// Substitutes into a plain-text field (subject, push title, push body). Values are
        /// inserted raw because the result is never interpreted as markup; a missing value
        /// collapses to nothing and the surrounding text is tidied so no "{{ }}" or double
        /// space ever reaches a recipient.
        /// </summary>
        public static string RenderText(string? template, IReadOnlyDictionary<string, string?> values)
        {
            if (string.IsNullOrWhiteSpace(template))
                return string.Empty;

            var rendered = PlaceholderRegex().Replace(template, match =>
                values.TryGetValue(match.Groups[1].Value, out var value) && !string.IsNullOrWhiteSpace(value)
                    ? value!
                    : string.Empty);

            return Tidy(rendered);
        }

        /// <summary>
        /// Substitutes into an HTML body. Values are HTML-encoded, then the whole body is
        /// reduced to the formatting allowlist.
        /// </summary>
        public static string RenderHtml(string? template, IReadOnlyDictionary<string, string?> values)
        {
            if (string.IsNullOrWhiteSpace(template))
                return string.Empty;

            var rendered = PlaceholderRegex().Replace(template, match =>
                values.TryGetValue(match.Groups[1].Value, out var value) && !string.IsNullOrWhiteSpace(value)
                    ? WebUtility.HtmlEncode(value)
                    : string.Empty);

            return Sanitize(rendered);
        }

        /// <summary>A readable plain-text fallback for a rendered HTML body.</summary>
        public static string ToPlainText(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            var withBreaks = html
                .Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase)
                .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("</li>", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("</div>", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("</h1>", "\n\n", StringComparison.OrdinalIgnoreCase)
                .Replace("</h2>", "\n\n", StringComparison.OrdinalIgnoreCase)
                .Replace("</h3>", "\n\n", StringComparison.OrdinalIgnoreCase);

            var text = WebUtility.HtmlDecode(TagRegex().Replace(withBreaks, string.Empty));

            var lines = text.Split('\n').Select(line => line.Trim());
            var result = string.Join("\n", lines);
            while (result.Contains("\n\n\n", StringComparison.Ordinal))
                result = result.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);

            return result.Trim();
        }

        /// <summary>
        /// Reduces arbitrary HTML to the formatting allowlist. Text is decoded then
        /// re-encoded so an author's own entities survive without being doubled, and every
        /// attribute except a safe <c>href</c> is dropped.
        /// </summary>
        public static string Sanitize(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            var output = new StringBuilder(html.Length);
            var openTags = new Stack<string>();
            var index = 0;

            while (index < html.Length)
            {
                var start = html.IndexOf('<', index);
                if (start < 0)
                {
                    AppendText(output, html[index..]);
                    break;
                }

                if (start > index)
                    AppendText(output, html[index..start]);

                var end = html.IndexOf('>', start + 1);
                if (end < 0)
                {
                    // Unterminated "<" — treat the remainder as text so it cannot become a tag.
                    AppendText(output, html[start..]);
                    break;
                }

                var raw = html[(start + 1)..end].Trim();
                index = end + 1;

                if (raw.Length == 0 || raw.StartsWith('!') || raw.StartsWith('?'))
                    continue;

                var isClosing = raw.StartsWith('/');
                if (isClosing)
                    raw = raw[1..].Trim();

                var selfClosing = raw.EndsWith('/');
                if (selfClosing)
                    raw = raw[..^1].Trim();

                var nameEnd = raw.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
                var name = (nameEnd < 0 ? raw : raw[..nameEnd]).ToLowerInvariant();
                if (!AllowedTags.Contains(name))
                    continue;

                if (isClosing)
                {
                    if (openTags.Contains(name))
                    {
                        // Close everything down to the matching tag so stripped markup cannot
                        // leave the document unbalanced.
                        string popped;
                        do
                        {
                            popped = openTags.Pop();
                            output.Append("</").Append(popped).Append('>');
                        }
                        while (popped != name && openTags.Count > 0);
                    }
                    continue;
                }

                var isVoid = name is "br" or "hr";
                output.Append('<').Append(name);

                if (name == "a")
                {
                    var href = ExtractHref(nameEnd < 0 ? string.Empty : raw[nameEnd..]);
                    if (href != null)
                        output.Append(" href=\"").Append(WebUtility.HtmlEncode(href))
                              .Append("\" target=\"_blank\" rel=\"noopener noreferrer\"");
                }

                if (isVoid || selfClosing)
                {
                    output.Append(" />");
                    if (!isVoid)
                        continue;
                }
                else
                {
                    output.Append('>');
                    openTags.Push(name);
                }
            }

            while (openTags.Count > 0)
                output.Append("</").Append(openTags.Pop()).Append('>');

            return output.ToString();
        }

        private static void AppendText(StringBuilder output, string text) =>
            output.Append(WebUtility.HtmlEncode(WebUtility.HtmlDecode(text)));

        [GeneratedRegex("href\\s*=\\s*(\"([^\"]*)\"|'([^']*)'|([^\\s>]+))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex HrefRegex();

        /// <summary>Only http, https, mailto and site-relative destinations survive.</summary>
        private static string? ExtractHref(string attributes)
        {
            var match = HrefRegex().Match(attributes);
            if (!match.Success)
                return null;

            var value = (match.Groups[2].Success ? match.Groups[2].Value
                        : match.Groups[3].Success ? match.Groups[3].Value
                        : match.Groups[4].Value).Trim();

            value = WebUtility.HtmlDecode(value);
            if (value.Length == 0 || value.Any(char.IsControl))
                return null;

            if (value.StartsWith("/", StringComparison.Ordinal) && !value.StartsWith("//", StringComparison.Ordinal))
                return value;

            if (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                return value;

            return Uri.TryCreate(value, UriKind.Absolute, out var uri)
                   && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri.AbsoluteUri
                : null;
        }

        /// <summary>Removes the gaps a missing variable leaves behind.</summary>
        private static string Tidy(string value)
        {
            var cleaned = RunOfSpacesRegex().Replace(value, " ");
            cleaned = cleaned.Replace(" ,", ",", StringComparison.Ordinal)
                             .Replace(" .", ".", StringComparison.Ordinal)
                             .Replace("( )", string.Empty, StringComparison.Ordinal)
                             .Replace("()", string.Empty, StringComparison.Ordinal);
            return cleaned.Trim();
        }
    }
}
