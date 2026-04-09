using System.IO;
using System.Text;
using FloatingDeskAssistant.Application;

namespace FloatingDeskAssistant.Infrastructure.Logging;

public sealed class FileLogger : ILoggerService
{
    private readonly string _logFilePath;
    private readonly object _gate = new();

    public FileLogger()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FloatingDeskAssistant",
            "logs");
        Directory.CreateDirectory(root);
        _logFilePath = Path.Combine(root, $"app-{DateTime.Now:yyyyMMdd}.log");
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private void Write(string level, string message, Exception? ex)
    {
        var safeMessage = Sanitize(message);
        var sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        sb.Append(" [").Append(level).Append("] ");
        sb.Append(safeMessage);
        if (ex is not null)
        {
            sb.Append(" | ").Append(Sanitize(ex.Message));
        }

        lock (_gate)
        {
            File.AppendAllText(_logFilePath, sb.ToString() + Environment.NewLine, Encoding.UTF8);
        }
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var replaced = value.Replace("Bearer ", "Bearer ***", StringComparison.OrdinalIgnoreCase);
        if (replaced.Contains("apiKey", StringComparison.OrdinalIgnoreCase))
        {
            return "[Sensitive content omitted]";
        }

        return replaced;
    }
}
