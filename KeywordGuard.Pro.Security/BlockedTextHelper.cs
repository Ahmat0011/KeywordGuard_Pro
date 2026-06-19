using System.Text.RegularExpressions;

namespace KeywordGuard.Pro.Security;

public static class BlockedTextHelper
{
    public static string NormalizeEntry(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return Regex.Replace(value.Trim(), @"\s+", " ");
    }

    public static List<string> SplitEntries(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new List<string>();

        return input
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeEntry)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    public static bool Matches(string? sourceText, BlockedItem item)
    {
        string normalizedSource = NormalizeEntry(sourceText);
        string normalizedValue = NormalizeEntry(item.Value);

        if (string.IsNullOrWhiteSpace(normalizedSource) || string.IsNullOrWhiteSpace(normalizedValue))
            return false;

        if (item.IsAggressive)
            return normalizedSource.Contains(normalizedValue, StringComparison.OrdinalIgnoreCase);

        string pattern = BuildExactPattern(normalizedValue);
        return Regex.IsMatch(normalizedSource, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string BuildExactPattern(string value)
    {
        var tokens = value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(Regex.Escape);

        return $@"(?<!\w){string.Join(@"\s+", tokens)}(?!\w)";
    }
}
