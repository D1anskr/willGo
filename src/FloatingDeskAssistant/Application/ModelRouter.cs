using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Infrastructure.Api;
using FloatingDeskAssistant.Models;

namespace FloatingDeskAssistant.Application;

public sealed class ModelRouter : IModelRouter
{
    private readonly IModelClient _modelClient;
    private readonly ILoggerService _logger;

    public ModelRouter(IModelClient modelClient, ILoggerService logger)
    {
        _modelClient = modelClient;
        _logger = logger;
    }

    public async Task<ModelReply> AskAsync(AppConfig config, IReadOnlyList<ConversationTurn> turns, CancellationToken cancellationToken)
    {
        var preferredIsSecondary = config.PreferredModel == PreferredModelTarget.Secondary;
        var preferredEndpoint = preferredIsSecondary ? config.Secondary : config.Primary;
        var fallbackEndpoint = preferredIsSecondary ? config.Primary : config.Secondary;

        var preferred = await ExecuteWithRetriesAsync(preferredEndpoint, turns, cancellationToken);
        if (preferred.Success)
        {
            return CreateReply(preferred.Text, preferredEndpoint, preferredIsSecondary);
        }

        _logger.Warn($"{GetModelLabel(preferredIsSecondary)} failed after retries: {preferred.Exception?.Message}");
        _logger.Warn($"Switching to {GetModelLabel(!preferredIsSecondary)}.");

        var fallback = await ExecuteWithRetriesAsync(fallbackEndpoint, turns, cancellationToken);
        if (fallback.Success)
        {
            _logger.Warn($"Auto switched to {GetModelLabel(!preferredIsSecondary)} and succeeded.");
            return CreateReply(
                fallback.Text,
                fallbackEndpoint,
                !preferredIsSecondary,
                BuildFallbackWarning(preferredIsSecondary));
        }

        _logger.Error($"Both {GetModelLabel(preferredIsSecondary)} and {GetModelLabel(!preferredIsSecondary)} failed.", fallback.Exception);

        var preferredReason = preferred.Exception?.Message ?? "unknown";
        var fallbackReason = fallback.Exception?.Message ?? "unknown";
        throw new ModelApiException(
            $"首选{GetModelLabel(preferredIsSecondary)}与{GetModelLabel(!preferredIsSecondary)}均不可用。{GetModelLabel(preferredIsSecondary)}错误：{preferredReason}；{GetModelLabel(!preferredIsSecondary)}错误：{fallbackReason}",
            true,
            fallback.Exception?.StatusCode,
            fallback.Exception);
    }

    private async Task<ExecutionResult> ExecuteWithRetriesAsync(
        ModelEndpointConfig endpoint,
        IReadOnlyList<ConversationTurn> turns,
        CancellationToken cancellationToken)
    {
        var attempts = Math.Max(1, endpoint.MaxRetries);
        ModelApiException? lastException = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                var text = await _modelClient.SendAsync(endpoint, turns, cancellationToken);
                return ExecutionResult.Ok(text);
            }
            catch (ModelApiException ex)
            {
                lastException = ex;
                var canRetry = ex.IsRetryable && attempt < attempts;
                if (!canRetry)
                {
                    break;
                }

                var delayMilliseconds = 500 * (int)Math.Pow(2, attempt - 1);
                _logger.Warn($"{endpoint.Name} attempt {attempt}/{attempts} failed. Retrying in {delayMilliseconds}ms.");
                await Task.Delay(delayMilliseconds, cancellationToken);
            }
        }

        return ExecutionResult.Fail(lastException ?? new ModelApiException("未知错误", true));
    }

    private static ModelReply CreateReply(
        string text,
        ModelEndpointConfig endpoint,
        bool usedSecondary,
        string? warning = null)
    {
        return new ModelReply
        {
            Text = text,
            ProviderName = endpoint.Name,
            UsedSecondary = usedSecondary,
            Warning = warning
        };
    }

    private static string BuildFallbackWarning(bool preferredWasSecondary)
    {
        return preferredWasSecondary
            ? "首选备用模型当前不可用，已自动切换到主模型。"
            : "首选主模型当前不可用，已自动切换到备用模型。";
    }

    private static string GetModelLabel(bool isSecondary)
    {
        return isSecondary ? "备用模型" : "主模型";
    }

    private readonly record struct ExecutionResult(bool Success, string Text, ModelApiException? Exception)
    {
        public static ExecutionResult Ok(string text) => new(true, text, null);

        public static ExecutionResult Fail(ModelApiException exception) => new(false, string.Empty, exception);
    }
}