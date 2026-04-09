using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.ViewModels.Base;

namespace FloatingDeskAssistant.ViewModels;

public sealed class PromptPresetEditorViewModel : ObservableObject
{
    private string _title;
    private string _prompt;
    private bool _isEnabled;

    public PromptPresetEditorViewModel(PromptPresetConfig preset)
    {
        Id = string.IsNullOrWhiteSpace(preset.Id) ? Guid.NewGuid().ToString("N") : preset.Id;
        _title = preset.Title;
        _prompt = preset.Prompt;
        _isEnabled = preset.IsEnabled;
    }

    public string Id { get; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Prompt
    {
        get => _prompt;
        set => SetProperty(ref _prompt, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public PromptPresetConfig ToConfig()
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
