using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace FloatingDeskAssistant.Application;

public sealed record BattleHistoryEntry(string Name, DateTime StartedAt, string AudioPath, string TranscriptPath,
    bool AudioBusy, bool TranscriptBusy)
{
    public bool HasAudio => AudioPath.Length > 0;
    public bool HasTranscript => TranscriptPath.Length > 0;
    public bool CanPlayAudio => HasAudio && !AudioBusy;
    public string TimeLabel => StartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string AudioStatus => !HasAudio ? "无录音" : AudioBusy ? "写入中 / 占用中" : "已保存";
    public string TranscriptStatus => !HasTranscript ? "无文档" : TranscriptBusy ? "记录中 / 占用中" : "已保存";
}

public static class BattleHistory
{
    private static readonly Regex Timestamp = new(@"^willGo-(\d{14}|\d{8}-\d{6}(?:-\d{3})?)(?:-\d+)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<BattleHistoryEntry> Read(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase))
            .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase);
        var entries = new List<BattleHistoryEntry>();
        foreach (var group in files)
        {
            var audio = group.FirstOrDefault(path => Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase)) ?? "";
            var text = group.FirstOrDefault(path => Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase)) ?? "";
            var name = group.Key!;
            var match = Timestamp.Match(name);
            DateTime started;
            if (!match.Success || !DateTime.TryParseExact(match.Groups[1].Value,
                ["yyyyMMddHHmmss", "yyyyMMdd-HHmmss", "yyyyMMdd-HHmmss-fff"], CultureInfo.InvariantCulture, DateTimeStyles.None, out started))
            {
                started = File.GetLastWriteTime(text.Length > 0 ? text : audio);
            }
            entries.Add(new(name, started, audio, text, IsBeingWritten(audio), IsBeingWritten(text)));
        }
        return entries.OrderByDescending(entry => entry.StartedAt).ThenByDescending(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsBeingWritten(string path)
    {
        if (path.Length == 0) return false;
        try
        {
            // Other readers, including our player, are allowed; active writers are not.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }
}
