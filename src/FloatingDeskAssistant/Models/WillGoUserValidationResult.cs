namespace FloatingDeskAssistant.Models;

public sealed class WillGoUserValidationResult
{
    public bool IsAllowed { get; init; }
    public bool Success { get; init; }
    public string StatusCode { get; init; } = "";
    public string Message { get; init; } = "";
    public bool IsServerUnavailable { get; init; }
}
