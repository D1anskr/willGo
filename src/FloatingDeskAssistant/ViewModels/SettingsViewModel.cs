using System.Drawing;
using System.Collections.ObjectModel;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.ViewModels.Base;

namespace FloatingDeskAssistant.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppConfig _defaultConfig;

    public SettingsViewModel(AppConfig current, AppConfig defaultConfig)
    {
        _defaultConfig = defaultConfig;
        EditableConfig = current.Clone();
        PromptPresets = new ObservableCollection<PromptPresetEditorViewModel>();
        ReloadPromptPresets();
    }

    public AppConfig EditableConfig { get; private set; }

    public ObservableCollection<PromptPresetEditorViewModel> PromptPresets { get; }

    public IReadOnlyList<ModelApiProtocol> ProtocolOptions { get; } =
        Enum.GetValues<ModelApiProtocol>();

    public IReadOnlyList<CompactModeLevel> CompactModeLevelOptions { get; } =
        Enum.GetValues<CompactModeLevel>();

    public IReadOnlyList<RemoteCaptureMode> RemoteCaptureModeOptions { get; } =
        Enum.GetValues<RemoteCaptureMode>();

    public bool HasRemoteCaptureRegion =>
        EditableConfig.RemoteCaptureRegionWidth >= 6 &&
        EditableConfig.RemoteCaptureRegionHeight >= 6;

    public string RemoteCaptureRegionSummary => HasRemoteCaptureRegion
        ? $"Saved region: X={EditableConfig.RemoteCaptureRegionX}, Y={EditableConfig.RemoteCaptureRegionY}, Width={EditableConfig.RemoteCaptureRegionWidth}, Height={EditableConfig.RemoteCaptureRegionHeight}"
        : "No preset region saved yet. In PresetRegion mode, phone trigger will wait until you choose and save a region.";

    public bool HasPromptPresets => PromptPresets.Count > 0;

    public bool NoPromptPresets => !HasPromptPresets;

    public void RestoreDefaults()
    {
        EditableConfig = _defaultConfig.Clone();
        RaisePropertyChanged(nameof(EditableConfig));
        RaiseRemoteCaptureChanged();
        ReloadPromptPresets();
    }

    public void AddPromptPreset()
    {
        PromptPresets.Add(new PromptPresetEditorViewModel(new PromptPresetConfig
        {
            Title = $"Preset {PromptPresets.Count + 1}",
            Prompt = "Describe the problem and answer with explanation, complexity analysis, and code.",
            IsEnabled = true
        }));
        RaisePromptPresetStateChanged();
    }

    public void RemovePromptPreset(PromptPresetEditorViewModel preset)
    {
        if (!PromptPresets.Remove(preset))
        {
            return;
        }

        RaisePromptPresetStateChanged();
    }

    public void MovePromptPreset(PromptPresetEditorViewModel preset, int offset)
    {
        var currentIndex = PromptPresets.IndexOf(preset);
        if (currentIndex < 0)
        {
            return;
        }

        var targetIndex = Math.Clamp(currentIndex + offset, 0, PromptPresets.Count - 1);
        if (targetIndex == currentIndex)
        {
            return;
        }

        PromptPresets.Move(currentIndex, targetIndex);
    }

    public AppConfig BuildUpdatedConfig()
    {
        var config = EditableConfig.Clone();
        config.PromptPresets = PromptPresets
            .Select(item => item.ToConfig())
            .Where(item => !string.IsNullOrWhiteSpace(item.Title) && !string.IsNullOrWhiteSpace(item.Prompt))
            .Select(item =>
            {
                item.Title = item.Title.Trim();
                item.Prompt = item.Prompt.Trim();
                return item;
            })
            .ToList();
        return config;
    }

    public void SetRemoteCaptureRegion(Rectangle region)
    {
        EditableConfig.RemoteCaptureRegionX = region.X;
        EditableConfig.RemoteCaptureRegionY = region.Y;
        EditableConfig.RemoteCaptureRegionWidth = region.Width;
        EditableConfig.RemoteCaptureRegionHeight = region.Height;
        RaiseRemoteCaptureChanged();
    }

    public void ClearRemoteCaptureRegion()
    {
        EditableConfig.RemoteCaptureRegionX = 0;
        EditableConfig.RemoteCaptureRegionY = 0;
        EditableConfig.RemoteCaptureRegionWidth = 0;
        EditableConfig.RemoteCaptureRegionHeight = 0;
        RaiseRemoteCaptureChanged();
    }

    private void RaiseRemoteCaptureChanged()
    {
        RaisePropertyChanged(nameof(EditableConfig));
        RaisePropertyChanged(nameof(HasRemoteCaptureRegion));
        RaisePropertyChanged(nameof(RemoteCaptureRegionSummary));
    }

    private void ReloadPromptPresets()
    {
        PromptPresets.Clear();
        foreach (var preset in EditableConfig.PromptPresets)
        {
            PromptPresets.Add(new PromptPresetEditorViewModel(preset.Clone()));
        }

        RaisePromptPresetStateChanged();
    }

    private void RaisePromptPresetStateChanged()
    {
        RaisePropertyChanged(nameof(HasPromptPresets));
        RaisePropertyChanged(nameof(NoPromptPresets));
    }
}
