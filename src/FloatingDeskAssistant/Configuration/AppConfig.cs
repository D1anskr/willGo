namespace FloatingDeskAssistant.Configuration;

public sealed class AppConfig
{
    public bool RememberUserId { get; set; }
    public string RememberedUserId { get; set; } = "";
    public BattleConfig Battle { get; set; } = new();
    public DualMachineConfig DualMachine { get; set; } = new();
    public ModelRouteConfig ConversationModels { get; set; } = new();
    public ModelEndpointConfig Primary { get; set; } = new();
    public ModelEndpointConfig Secondary { get; set; } = new();
    public List<PromptPresetConfig> PromptPresets { get; set; } = CreateDefaultPromptPresets();
    public PreferredModelTarget PreferredModel { get; set; } = PreferredModelTarget.Primary;
    public bool CompactChatMode { get; set; }
    public CompactModeLevel CompactModeLevel { get; set; } = CompactModeLevel.Mode1;
    public double MessageFontSize { get; set; } = 14.0;
    public double BallOpacity { get; set; } = 0.28;
    public double WindowOpacity { get; set; } = 0.82;
    public double EffectiveWindowOpacity => DualMachine.IsFusionDisplay ? DualMachine.Clone().FusionOpacity : WindowOpacity;
    public bool HideFromScreenCapture { get; set; } = true;
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
            RememberUserId = RememberUserId,
            RememberedUserId = RememberedUserId,
            Primary = Primary.Clone(),
            Battle = Battle.Clone(),
            DualMachine = DualMachine.Clone(),
            ConversationModels = ConversationModels.Clone(),
            Secondary = Secondary.Clone(),
            PromptPresets = PromptPresets.Select(preset => preset.Clone()).ToList(),
            PreferredModel = PreferredModel,
            CompactChatMode = CompactChatMode,
            CompactModeLevel = CompactModeLevel,
            MessageFontSize = MessageFontSize,
            BallOpacity = BallOpacity,
            WindowOpacity = WindowOpacity,
            HideFromScreenCapture = HideFromScreenCapture,
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
                Title = "生产者消费者",
                Prompt = """
请实现一个线程安全的生产者消费者模型。

请按以下结构回答：
1. 简要概括问题
2. 解释线程同步策略
3. 给出正确的代码实现
4. 分析时间复杂度与并发陷阱
5. 说明等待通知机制与条件变量的适用场景
""",
                IsEnabled = true
            },
            new PromptPresetConfig
            {
                Title = "最近最少使用缓存",
                Prompt = """
请实现一个最近最少使用缓存。

请按以下结构回答：
1. 核心思路
2. 使用的数据结构
3. 复杂度分析
4. 完整代码实现
5. 常见边界情况
""",
                IsEnabled = true
            },
            new PromptPresetConfig
            {
                Title = "二叉树层序遍历",
                Prompt = """
请解决二叉树层序遍历问题。

请按以下结构回答：
1. 问题概述
2. 广度优先遍历思路
3. 复杂度分析
4. 完整代码实现
5. 边界情况
""",
                IsEnabled = true
            },
            new PromptPresetConfig
            {
                Title = "线程池设计",
                Prompt = """
请设计一个简单的线程池。

请按以下结构回答：
1. 核心组成
2. 任务队列和工作线程的生命周期
3. 关闭过程
4. 完整代码实现
5. 并发风险与取舍
""",
                IsEnabled = true
            }
        ];
    }
}
