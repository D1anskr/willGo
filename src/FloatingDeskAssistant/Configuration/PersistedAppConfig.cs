namespace FloatingDeskAssistant.Configuration;

public sealed class PersistedAppConfig
{
    public bool RememberUserId { get; set; }
    public string RememberedUserId { get; set; } = "";
    public BattleConfig? Battle { get; set; }
    public string SpeechApiKeyProtected { get; set; } = string.Empty;
    public string SpeechAccessTokenProtected { get; set; } = string.Empty;
    public ModelRouteConfig? ConversationModels { get; set; }
    public string ConversationPrimaryApiKeyProtected { get; set; } = string.Empty;
    public string ConversationSecondaryApiKeyProtected { get; set; } = string.Empty;
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
    public bool HideFromScreenCapture { get; set; } = true;
    public RemoteCaptureMode RemoteCaptureMode { get; set; } = RemoteCaptureMode.FullDesktop;
    public int RemoteCaptureRegionX { get; set; }
    public int RemoteCaptureRegionY { get; set; }
    public int RemoteCaptureRegionWidth { get; set; }
    public int RemoteCaptureRegionHeight { get; set; }
}
