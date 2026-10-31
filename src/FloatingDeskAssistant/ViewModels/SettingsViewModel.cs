using System.Drawing;
using System.Collections.ObjectModel;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.ViewModels.Base;
using FloatingDeskAssistant.Application;
using NAudio.CoreAudioApi;

namespace FloatingDeskAssistant.ViewModels;

public sealed record SettingsOption<T>(T Value, string Label);

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppConfig _defaultConfig;

    public SettingsViewModel(AppConfig current, AppConfig defaultConfig)
    {
        _defaultConfig = defaultConfig;
        EditableConfig = current.Clone();
        PromptPresets = new ObservableCollection<PromptPresetEditorViewModel>();
        ReloadPromptPresets();
        Microphones = LoadDevices(DataFlow.Capture, EditableConfig.Battle.MicrophoneDeviceId);
        Speakers = LoadDevices(DataFlow.Render, EditableConfig.Battle.SpeakerDeviceId);
    }

    public AppConfig EditableConfig { get; private set; }
    public IReadOnlyList<AudioDeviceOption> Microphones { get; }
    public IReadOnlyList<AudioDeviceOption> Speakers { get; }
    public IReadOnlyList<SettingsOption<DualMachineRole>> DualMachineRoles { get; } =
        [new(DualMachineRole.Data, "数据端（采集这台电脑）"), new(DualMachineRole.Display, "显示端（接收并回答）")];
    public bool DualMachineEnabled
    {
        get => EditableConfig.DualMachine.Enabled;
        set { EditableConfig.DualMachine.Enabled = value; RaiseDualMachineChanged(); }
    }
    public DualMachineRole SelectedDualMachineRole
    {
        get => EditableConfig.DualMachine.Role;
        set { EditableConfig.DualMachine.Role = value; RaiseDualMachineChanged(); }
    }
    public bool IsDataRole => EditableConfig.DualMachine.IsData;
    public bool IsDisplayRole => EditableConfig.DualMachine.IsDisplay;
    private void RaiseDualMachineChanged()
    {
        RaisePropertyChanged(nameof(DualMachineEnabled)); RaisePropertyChanged(nameof(SelectedDualMachineRole));
        RaisePropertyChanged(nameof(IsDataRole)); RaisePropertyChanged(nameof(IsDisplayRole));
    }

    private static IReadOnlyList<AudioDeviceOption> LoadDevices(DataFlow flow, string savedId)
    {
        List<AudioDeviceOption> devices;
        try { devices = BattleSpeechService.GetDevices(flow).ToList(); }
        catch { devices = [new("", "跟随系统默认设备")]; }
        if (!devices.Any(d => d.Id == savedId)) devices.Add(new(savedId, "之前选择的设备（当前不可用）"));
        return devices;
    }

    public ObservableCollection<PromptPresetEditorViewModel> PromptPresets { get; }

    public IReadOnlyList<SettingsOption<ModelApiProtocol>> ProtocolOptions { get; } =
    [
        new(ModelApiProtocol.Auto, "自动识别"),
        new(ModelApiProtocol.OpenAiChatCompletions, "聊天补全协议"),
        new(ModelApiProtocol.OpenAiResponses, "响应协议"),
        new(ModelApiProtocol.AnthropicMessages, "消息协议")
    ];
    public IReadOnlyList<SettingsOption<PreferredModelTarget>> PreferredModelOptions { get; } =
    [new(PreferredModelTarget.Primary, "主模型"), new(PreferredModelTarget.Secondary, "备用模型")];

    public IReadOnlyList<CompactModeLevel> CompactModeLevelOptions { get; } =
        Enum.GetValues<CompactModeLevel>();

    public IReadOnlyList<SettingsOption<RemoteCaptureMode>> RemoteCaptureModeOptions { get; } =
    [new(RemoteCaptureMode.FullDesktop, "整个桌面"), new(RemoteCaptureMode.PresetRegion, "指定区域")];

    public bool HasRemoteCaptureRegion =>
        EditableConfig.RemoteCaptureRegionWidth >= 6 &&
        EditableConfig.RemoteCaptureRegionHeight >= 6;

    public string RemoteCaptureRegionSummary => HasRemoteCaptureRegion
        ? $"已选区域：横坐标 {EditableConfig.RemoteCaptureRegionX}，纵坐标 {EditableConfig.RemoteCaptureRegionY}，宽度 {EditableConfig.RemoteCaptureRegionWidth}，高度 {EditableConfig.RemoteCaptureRegionHeight}"
        : "尚未选择截图区域";

    public bool HasPromptPresets => PromptPresets.Count > 0;

    public bool NoPromptPresets => !HasPromptPresets;

    public void RestoreDefaults()
    {
        EditableConfig = _defaultConfig.Clone();
        RaisePropertyChanged(nameof(EditableConfig));
        RaiseDualMachineChanged();
        RaiseRemoteCaptureChanged();
        ReloadPromptPresets();
    }

    public void UpdateOpacity(double ballOpacity, double windowOpacity)
    {
        EditableConfig.BallOpacity = ballOpacity;
        EditableConfig.WindowOpacity = windowOpacity;
        RaisePropertyChanged(nameof(EditableConfig));
    }

    public void AddPromptPreset()
    {
        PromptPresets.Add(new PromptPresetEditorViewModel(new PromptPresetConfig
        {
            Title = $"预设问题 {PromptPresets.Count + 1}",
            Prompt = "请分析这个问题，说明解决思路、复杂度，并给出代码实现。",
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
