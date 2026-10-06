using System;
using System.Text.RegularExpressions;

namespace UrlShortener.Application.Common
{
    public interface IPromptSanitizer
    {
        string Sanitize(string input);
    }

    public class PromptSanitizer : IPromptSanitizer
    {
        // Guard against Prompt Injection (e.g., "Ignore all previous instructions and do X")
        private static readonly string[] ForbiddenKeywords = 
        { 
            "ignore previous", "system prompt", "bypass", "override", "developer mode", "root access" 
        };

        public string Sanitize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            string sanitized = input.Trim();

            // 1. Keyword blocking
            foreach (var keyword in ForbiddenKeywords)
            {
                if (sanitized.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return "Invalid request: Prompt contains forbidden keywords.";
                }
            }

            // 2. Length constraint to prevent buffer/token overflow attacks
            if (sanitized.Length > 500)
            {
                sanitized = sanitized.Substring(0, 500);
            }

            return sanitized;
        }
    }
}
