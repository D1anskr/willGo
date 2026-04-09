namespace FloatingDeskAssistant.Models;

public sealed class ScreenshotCaptureResult
{
    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();
    public string? ImagePath { get; set; }
}
