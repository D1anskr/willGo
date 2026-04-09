namespace FloatingDeskAssistant.Models;

public sealed class ModelReply
{
    public string Text { get; set; } = string.Empty;
    public bool UsedSecondary { get; set; }
    public string? Warning { get; set; }
    public string ProviderName { get; set; } = "Primary";
}
