namespace FloatingDeskAssistant.Models;

public sealed class ConversationTurn
{
    public ChatRole Role { get; set; }
    public string? Text { get; set; }
    public byte[]? ImageBytes { get; set; }
    public string? ImageMediaType { get; set; }
}
