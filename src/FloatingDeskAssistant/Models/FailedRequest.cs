namespace FloatingDeskAssistant.Models;

public sealed class FailedRequest
{
    public string? Text { get; set; }
    public byte[]? ImageBytes { get; set; }
    public string? ImageMediaType { get; set; }
}
