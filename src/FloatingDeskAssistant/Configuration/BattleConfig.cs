namespace FloatingDeskAssistant.Configuration;

public sealed class BattleConfig
{
    public const string DefaultContextPrompt = "这是 Java 后端、AI 应用或 Agent 开发岗位的面试。前面的消息是最近两轮问题及其回答，最后一条是最新问题。语音转写可能包含中文同音字、英文术语或断句错误。请结合前两轮问答、技术语境和最新问题，合理判断是否存在识别错误；问题可能逐步深入、补充或重新表达，也可能切换话题，不要强行关联。历史问题和回答本身也可能有误，不要把旧答案当作事实。只有上下文和技术逻辑充分支持时才按正确术语理解，例如 MySQL 索引语境中的“毕加索”可能指 B+ 树；不要仅凭发音随意替换。只回答最新问题，必要时用一句话说明采用的理解；仍有歧义时简短说明假设或请求澄清。";
    public double AnswerFontSize { get; set; } = 18;
    public double QuestionPaneWidth { get; set; } = 210;
    public double ScreenshotQuestionRatio { get; set; } = .38;
    public string ConversationPrompt { get; set; } = "你是我的实时对话助手。请针对下面这句话，直接给出准确、简洁、可用于口头表达的回答。技术问题先给出核心结论，再补充必要的解释和示例。";
    public string TriggerPhrases { get; set; } = "我想一想\n让我想想\n让我想一想";
    public string ContextPrompt { get; set; } = DefaultContextPrompt;
    public bool SuppressSpeakerEcho { get; set; } = true;
    public string MicrophoneDeviceId { get; set; } = string.Empty;
    public string SpeakerDeviceId { get; set; } = string.Empty;
    public DoubaoSpeechConfig Speech { get; set; } = new();
    public double SpeechPauseSeconds { get; set; } = 1.1;
    public bool SaveSpeechSessions { get; set; } = true;
    public string SpeechArchiveDirectory { get; set; } = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "willGo", "Recordings");

    public BattleConfig Clone() => new()
    {
        AnswerFontSize = double.IsFinite(AnswerFontSize) ? Math.Clamp(AnswerFontSize, 14, 28) : 18,
        QuestionPaneWidth = double.IsFinite(QuestionPaneWidth) ? Math.Clamp(QuestionPaneWidth, 150, 360) : 210,
        ScreenshotQuestionRatio = double.IsFinite(ScreenshotQuestionRatio) ? Math.Clamp(ScreenshotQuestionRatio, .25, .65) : .38,
        ConversationPrompt = ConversationPrompt ?? string.Empty,
        TriggerPhrases = TriggerPhrases ?? string.Empty,
        ContextPrompt = ContextPrompt ?? DefaultContextPrompt,
        SuppressSpeakerEcho = SuppressSpeakerEcho,
        MicrophoneDeviceId = MicrophoneDeviceId ?? string.Empty,
        SpeakerDeviceId = SpeakerDeviceId ?? string.Empty,
        Speech = Speech?.Clone() ?? new(),
        SpeechPauseSeconds = double.IsFinite(SpeechPauseSeconds) ? Math.Clamp(SpeechPauseSeconds, 0.6, 2.5) : 1.1,
        SaveSpeechSessions = SaveSpeechSessions,
        SpeechArchiveDirectory = SpeechArchiveDirectory ?? string.Empty
    };
}
