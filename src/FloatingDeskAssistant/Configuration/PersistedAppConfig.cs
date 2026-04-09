namespace FloatingDeskAssistant.Configuration;

public sealed class PersistedAppConfig
{
    public ModelEndpointConfig Primary { get; set; } = new();
    public ModelEndpointConfig Secondary { get; set; } = new();
    public List<PromptPresetConfig>? PromptPresets { get; set; }
    public PreferredModelTarget PreferredModel { get; set; } = PreferredModelTarget.Primary;
    public bool CompactChatMode { get; set; }
    public CompactModeLevel CompactModeLevel { get; set; } = CompactModeLevel.Mode1;
    public double MessageFontSize { get; set; } = 14.0;
    public string PrimaryApiKeyProtected { get; set; } = string.Empty;
    public string SecondaryApiKeyProtected { get; set; } = string.Empty;
    public double BallOpacity { get; set; } = 0.28;
    public double WindowOpacity { get; set; } = 0.82;
    public RemoteCaptureMode RemoteCaptureMode { get; set; } = RemoteCaptureMode.FullDesktop;
    public int RemoteCaptureRegionX { get; set; }
    public int RemoteCaptureRegionY { get; set; }
    public int RemoteCaptureRegionWidth { get; set; }
    public int RemoteCaptureRegionHeight { get; set; }
}
