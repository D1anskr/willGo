namespace FloatingDeskAssistant.Configuration;

public sealed class ModelEndpointConfig
{
    public string Name { get; set; } = "Primary";
    public ModelApiProtocol Protocol { get; set; } = ModelApiProtocol.Auto;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
    public string SystemPrompt { get; set; } = "你是一个桌面截图分析助手，请准确描述图中内容并给出可执行建议。";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;

    public ModelEndpointConfig Clone()
    {
        return new ModelEndpointConfig
        {
            Name = Name,
            Protocol = Protocol,
            BaseUrl = BaseUrl,
            ApiKey = ApiKey,
            Model = Model,
            SystemPrompt = SystemPrompt,
            TimeoutSeconds = TimeoutSeconds,
            MaxRetries = MaxRetries
        };
    }
}
