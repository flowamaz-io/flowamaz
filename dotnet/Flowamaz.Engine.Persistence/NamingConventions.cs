using System.Text;

namespace Flowamaz.Infrastructure.Persistence;

internal static class NamingConventions
{
    /// <summary>
    /// Converts PascalCase or camelCase to snake_case (e.g. "AiTokenUsage" → "ai_token_usage").
    /// Treats runs of capital letters as a single token, then prepends underscores before
    /// each new lowercase boundary so "Pk_AiTokenUsage" → "pk_ai_token_usage".
    /// </summary>
    public static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new StringBuilder(input.Length + 8);
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (i > 0 && char.IsUpper(c))
            {
                var prev = input[i - 1];
                var next = i + 1 < input.Length ? input[i + 1] : '\0';
                // Underscore before this upper if the previous char was lower OR
                // we're at the start of a new word inside a run of capitals (e.g. "JSONResponse").
                if (char.IsLower(prev) || (char.IsUpper(prev) && char.IsLower(next)))
                {
                    sb.Append('_');
                }
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
