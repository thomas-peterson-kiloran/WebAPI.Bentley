using System.Text.RegularExpressions;

namespace WebAPI.Bentley.Utilities
{
    public static class Sanitizer
    {
        // Remove script blocks and HTML tags, collapse whitespace, trim, and optionally enforce max length.
        public static string? Sanitize(string? input, int? maxLength = null)
        {
            if (input == null) return null;

            // Remove script blocks
            var withoutScripts = Regex.Replace(input, "<script.*?>.*?</script>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Remove all HTML tags
            var withoutTags = Regex.Replace(withoutScripts, "<.*?>", string.Empty, RegexOptions.Singleline);

            // Remove control characters
            var cleaned = Regex.Replace(withoutTags, "[\u0000-\u001F\u007F]+", string.Empty);

            // Collapse multiple whitespaces
            cleaned = Regex.Replace(cleaned, "\\s+", " ").Trim();

            if (maxLength.HasValue && cleaned.Length > maxLength.Value)
            {
                cleaned = cleaned.Substring(0, maxLength.Value);
            }

            return cleaned;
        }
    }
}
