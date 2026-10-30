using System.IO;
using System.Text.Json;

namespace FloatingDeskAssistant.Application;

public static class SpeechTechnicalVocabulary
{
    public static IReadOnlyList<string> Terms { get; } = LoadTerms();

    private static IReadOnlyList<string> LoadTerms()
    {
        using var stream = typeof(SpeechTechnicalVocabulary).Assembly.GetManifestResourceStream("FloatingDeskAssistant.SpeechTechnicalVocabulary.json")
            ?? throw new InvalidOperationException("Missing embedded speech technical vocabulary.");
        using var document = JsonDocument.Parse(stream);
        var terms = document.RootElement.GetProperty("categories").EnumerateObject()
            .SelectMany(category => category.Value.EnumerateArray()).Select(value => value.GetString()!)
            .Where(term => !string.IsNullOrWhiteSpace(term) && term.Length <= 80 && term.All(c => c is >= ' ' and <= '~'))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return Array.AsReadOnly(terms);
    }
}
