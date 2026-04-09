namespace FloatingDeskAssistant.Configuration;

public sealed class PromptPresetConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public PromptPresetConfig Clone()
    {
        return new PromptPresetConfig
        {
            Id = Id,
            Title = Title,
            Prompt = Prompt,
            IsEnabled = IsEnabled
        };
    }
}
