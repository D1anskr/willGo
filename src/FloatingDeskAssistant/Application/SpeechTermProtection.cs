using System.Text.RegularExpressions;

namespace FloatingDeskAssistant.Application;

public sealed class SpeechTermProtection
{
    private const RegexOptions MatchOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex BuiltInTerms = CreateMatcher(SpeechTechnicalVocabulary.Terms);
    private static readonly Regex SpelledAcronyms = CreateSpelledAcronymMatcher();
    private static readonly Regex EnglishIdentifiers = new(
        @"(?<![A-Za-z0-9_])\.?[A-Za-z0-9_]+(?:(?:[.+/#-]|::)[A-Za-z0-9_]+)*(?:\+\+|#)?(?![A-Za-z0-9_])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly string _hotwords;
    private readonly Regex _customTerms;

    public SpeechTermProtection(string? hotwords)
    {
        _hotwords = hotwords ?? "";
        _customTerms = CreateMatcher(SpeechHotwords.ParseAll(_hotwords).Where(term => term.Any(char.IsAsciiLetter) && term.All(c => c <= 127)));
    }

    public string SelectFinalText(string streamingText, string refinedText)
    {
        streamingText = Normalize(streamingText);
        refinedText = Normalize(refinedText);
        if (string.IsNullOrWhiteSpace(refinedText)) return streamingText;

        var mixedLanguage = streamingText.Any(c => c is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff');
        var candidates = BuiltInTerms.Matches(streamingText).Select(match => match.Value)
            .Concat(_customTerms.Matches(streamingText).Select(match => match.Value))
            .Concat(EnglishIdentifiers.Matches(streamingText).Select(match => match.Value)
                .Where(token => token.Any(char.IsAsciiLetter) && (mixedLanguage && token.Length >= 2 || LooksLikeIdentifier(token))))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        // Check only terms actually present in this utterance. The broad dictionary is
        // never fed to either decoder, where it could bias short acronyms or add latency.
        foreach (var term in candidates)
        {
            var pattern = BoundedPattern(term);
            if (Regex.Count(streamingText, pattern, MatchOptions) > Regex.Count(refinedText, pattern, MatchOptions))
                return streamingText;
        }
        return refinedText;
    }

    private string Normalize(string text) => SpelledAcronyms.Replace(
        SpeechHotwords.NormalizeAcronyms(text, _hotwords), match => new string(match.Value.Where(c => c is not (' ' or '\t')).ToArray()).ToUpperInvariant());

    private static bool LooksLikeIdentifier(string token) => token.Length >= 2 &&
        (token.Any(c => char.IsAsciiDigit(c) || c is '_' or '.' or '+' or '#' or '/' or ':' or '-') ||
         token.All(c => c is >= 'A' and <= 'Z') || token.Any(char.IsAsciiLetterLower) && token.Skip(1).Any(char.IsAsciiLetterUpper));

    private static string BoundedPattern(string term) => @"(?<![A-Za-z0-9_])" + Regex.Escape(term).Replace(@"\ ", @"[ \t]+") + @"(?![A-Za-z0-9_])";

    private static Regex CreateMatcher(IEnumerable<string> terms)
    {
        var alternatives = terms.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(term => term.Length)
            .ThenBy(term => term, StringComparer.Ordinal).Select(term => Regex.Escape(term).Replace(@"\ ", @"[ \t]+"));
        var pattern = string.Join("|", alternatives);
        return new(pattern.Length == 0 ? "(?!)" : @"(?<![A-Za-z0-9_])(?:" + pattern + @")(?![A-Za-z0-9_])", MatchOptions | RegexOptions.Compiled);
    }

    private static Regex CreateSpelledAcronymMatcher()
    {
        var alternatives = SpeechTechnicalVocabulary.Terms.Where(term => term.Length is >= 2 and <= 8 &&
                term.Any(char.IsAsciiLetter) && term.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9'))
            .OrderByDescending(term => term.Length).ThenBy(term => term, StringComparer.Ordinal)
            .Select(term => string.Join(@"[ \t]+", term.Select(c => Regex.Escape(c.ToString()))));
        return new(@"(?<![A-Za-z0-9_])(?:" + string.Join("|", alternatives) + @")(?![A-Za-z0-9_])", MatchOptions | RegexOptions.Compiled);
    }
}
