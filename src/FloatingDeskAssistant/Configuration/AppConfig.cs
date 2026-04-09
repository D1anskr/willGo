namespace FloatingDeskAssistant.Configuration;

public sealed class AppConfig
{
    public ModelEndpointConfig Primary { get; set; } = new();
    public ModelEndpointConfig Secondary { get; set; } = new();
    public List<PromptPresetConfig> PromptPresets { get; set; } = CreateDefaultPromptPresets();
    public PreferredModelTarget PreferredModel { get; set; } = PreferredModelTarget.Primary;
    public bool CompactChatMode { get; set; }
    public CompactModeLevel CompactModeLevel { get; set; } = CompactModeLevel.Mode1;
    public double MessageFontSize { get; set; } = 14.0;
    public double BallOpacity { get; set; } = 0.28;
    public double WindowOpacity { get; set; } = 0.82;
    public RemoteCaptureMode RemoteCaptureMode { get; set; } = RemoteCaptureMode.FullDesktop;
    public int RemoteCaptureRegionX { get; set; }
    public int RemoteCaptureRegionY { get; set; }
    public int RemoteCaptureRegionWidth { get; set; }
    public int RemoteCaptureRegionHeight { get; set; }

    public static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            Primary = new ModelEndpointConfig
            {
                Name = "Primary",
                Protocol = ModelApiProtocol.Auto,
                BaseUrl = "https://api.openai.com/v1",
                Model = "gpt-4o-mini",
                SystemPrompt = "你是一个桌面截图分析助手，请准确描述图片内容并给出可执行建议。",
                TimeoutSeconds = 30,
                MaxRetries = 3
            },
            Secondary = new ModelEndpointConfig
            {
                Name = "Secondary",
                Protocol = ModelApiProtocol.Auto,
                BaseUrl = "https://api.openai.com/v1",
                Model = "gpt-4o-mini",
                SystemPrompt = "你是一个桌面截图分析助手，请准确描述图片内容并给出可执行建议。",
                TimeoutSeconds = 30,
                MaxRetries = 2
            },
            PromptPresets = CreateDefaultPromptPresets(),
            PreferredModel = PreferredModelTarget.Primary,
            CompactChatMode = false,
            CompactModeLevel = CompactModeLevel.Mode1,
            MessageFontSize = 14.0,
            BallOpacity = 0.28,
            WindowOpacity = 0.82,
            RemoteCaptureMode = RemoteCaptureMode.FullDesktop,
            RemoteCaptureRegionX = 0,
            RemoteCaptureRegionY = 0,
            RemoteCaptureRegionWidth = 0,
            RemoteCaptureRegionHeight = 0
        };
    }

    public AppConfig Clone()
    {
        return new AppConfig
        {
            Primary = Primary.Clone(),
            Secondary = Secondary.Clone(),
            PromptPresets = PromptPresets.Select(preset => preset.Clone()).ToList(),
            PreferredModel = PreferredModel,
            CompactChatMode = CompactChatMode,
            CompactModeLevel = CompactModeLevel,
            MessageFontSize = MessageFontSize,
            BallOpacity = BallOpacity,
            WindowOpacity = WindowOpacity,
            RemoteCaptureMode = RemoteCaptureMode,
            RemoteCaptureRegionX = RemoteCaptureRegionX,
            RemoteCaptureRegionY = RemoteCaptureRegionY,
            RemoteCaptureRegionWidth = RemoteCaptureRegionWidth,
            RemoteCaptureRegionHeight = RemoteCaptureRegionHeight
        };
    }

    private static List<PromptPresetConfig> CreateDefaultPromptPresets()
    {
        return
        [
            new PromptPresetConfig
            {
                Title = "Producer Consumer",
                Prompt = """
Write a thread-safe producer consumer implementation.

Please answer in this structure:
1. Restate the problem briefly
2. Explain the synchronization strategy
3. Give a correct implementation
4. Explain time complexity and key concurrency pitfalls
5. Mention when to use wait/notify or condition variables
""",
                IsEnabled = true
            },
            new PromptPresetConfig
            {
                Title = "LRU Cache",
                Prompt = """
Implement an LRU cache.

Please answer in this structure:
1. Core idea
2. Data structures used
3. Complexity analysis
4. Complete implementation
5. Common edge cases
""",
                IsEnabled = true
            },
            new PromptPresetConfig
            {
                Title = "Binary Tree Level Order",
                Prompt = """
Solve the binary tree level order traversal problem.

Please answer in this structure:
1. Problem summary
2. BFS approach
3. Complexity analysis
4. Complete implementation
5. Edge cases
""",
                IsEnabled = true
            },
            new PromptPresetConfig
            {
                Title = "Thread Pool",
                Prompt = """
Design a simple thread pool.

Please answer in this structure:
1. Key components
2. Task queue and worker lifecycle
3. Shutdown behavior
4. Complete implementation
5. Concurrency risks and tradeoffs
""",
                IsEnabled = true
            }
        ];
    }
}
