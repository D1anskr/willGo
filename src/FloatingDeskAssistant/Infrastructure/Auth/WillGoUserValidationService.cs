using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Models;

namespace FloatingDeskAssistant.Infrastructure.Auth;

public sealed class WillGoUserValidationService : IWillGoUserValidationService, IDisposable
{
    private static readonly Uri ValidationUri = new("https://43.155.247.243/willGo/api/public/willgo-users/validate");
    private readonly HttpClient _client;
    private readonly ILoggerService _logger;

    public WillGoUserValidationService(ILoggerService logger, HttpMessageHandler? handler = null)
    {
        _logger = logger;
        // Preserve compatibility with the existing validation server's IP certificate.
        _client = new HttpClient(handler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (request, _, _, _) => request.RequestUri?.Host == ValidationUri.Host
        }) { Timeout = TimeSpan.FromSeconds(12) };
    }

    public async Task<WillGoUserValidationResult> ValidateAsync(string userId, string ip, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.PostAsJsonAsync(ValidationUri, new { userId, ip }, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
                return new() { StatusCode = $"HTTP_{(int)response.StatusCode}", Message = $"账号校验失败（HTTP {(int)response.StatusCode}），请稍后重试。", IsServerUnavailable = (int)response.StatusCode >= 500 };

            var payload = await response.Content.ReadFromJsonAsync<ValidationResponse>(cancellationToken).ConfigureAwait(false);
            var status = (payload?.Status ?? "UNKNOWN").Trim().Replace('-', '_').Replace(' ', '_').ToUpperInvariant();
            var message = payload?.Message?.Trim() ?? "校验服务未返回提示信息。";
            var allowedStatus = status is "VALID" or "BOUND_SUCCESS" or "WHITELIST" or "WHITE_LIST"
                or "WHITELIST_ALLOW" or "WHITELIST_ALLOWED" or "WHITE_LIST_ALLOW" or "WHITE_LIST_ALLOWED"
                or "ALLOWLIST" or "ALLOW_LIST" or "ALLOWLIST_ALLOW" or "ALLOWLIST_ALLOWED" or "ALLOW_LIST_ALLOW" or "ALLOW_LIST_ALLOWED";
            var deniedStatus = status is "USER_NOT_FOUND" or "USER_DISABLED" or "IP_MISMATCH" or "BAD_REQUEST";
            var whitelistMessage = message.Contains("白名单", StringComparison.OrdinalIgnoreCase)
                && new[] { "放行", "通过", "允许", "allow" }.Any(word => message.Contains(word, StringComparison.OrdinalIgnoreCase));
            return new()
            {
                Success = payload?.Success == true,
                IsAllowed = payload?.Success == true && !deniedStatus && (allowedStatus || whitelistMessage),
                StatusCode = status,
                Message = message
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warn("willGo login validation timed out.");
            return NetworkFailure("连接校验服务器超时，请重试。");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            _logger.Warn($"willGo login validation failed: {ex.GetType().Name}.");
            return NetworkFailure("无法完成账号校验，请检查网络后重试。");
        }
    }

    private static WillGoUserValidationResult NetworkFailure(string message) => new()
    {
        StatusCode = "NETWORK_ERROR", Message = message, IsServerUnavailable = true
    };

    public void Dispose() => _client.Dispose();

    private sealed class ValidationResponse
    {
        public bool Success { get; set; }
        public string? Status { get; set; }
        public string? Message { get; set; }
    }
}
