namespace FloatingDeskAssistant.Configuration;

public sealed class ModelRouteConfig
{
    public ModelEndpointConfig Primary { get; set; } = new();
    public ModelEndpointConfig Secondary { get; set; } = new() { Name = "Secondary" };
    public PreferredModelTarget PreferredModel { get; set; }

    public ModelRouteConfig Clone() => new()
    {
        Primary = Primary.Clone(), Secondary = Secondary.Clone(), PreferredModel = PreferredModel
    };

    public static ModelRouteConfig FromScreenshotConfig(AppConfig config) => new()
    {
        Primary = config.Primary.Clone(), Secondary = config.Secondary.Clone(), PreferredModel = config.PreferredModel
    };
}
