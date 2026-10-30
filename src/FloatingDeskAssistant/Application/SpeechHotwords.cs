using System.Text.RegularExpressions;

namespace FloatingDeskAssistant.Application;

public static class SpeechHotwords
{
    public static string[] Parse(string? text) => ParseAll(text).Take(100).ToArray();

    internal static IEnumerable<string> ParseAll(string? text) => (text ?? "").Split(['\r', '\n', ';', '；'], StringSplitOptions.RemoveEmptyEntries)
        .Select(s => new string(s.Trim().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '+' or '#' or '.').ToArray()))
        .Where(s => s.Length is > 0 and <= 80).Distinct(StringComparer.OrdinalIgnoreCase);

    // Merge only an exactly spelled configured acronym; never invent a missing letter.
    public static string NormalizeAcronyms(string text, string? hotwords)
    {
        foreach (var word in ParseAll(hotwords).Where(s => s.Length is >= 2 and <= 8 && s.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9')))
        {
            var pattern = @"(?<![A-Za-z0-9])" + string.Join(@"[ \t]*", word.Select(c => Regex.Escape(c.ToString()))) + @"(?![A-Za-z0-9])";
            text = Regex.Replace(text, pattern, word, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return text;
    }

    public static string SelectFinalText(string streamingText, string refinedText, string? hotwords) =>
        new SpeechTermProtection(hotwords).SelectFinalText(streamingText, refinedText);
}
