using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Infrastructure.Auth;
using FloatingDeskAssistant.Infrastructure.Config;
using FloatingDeskAssistant.Infrastructure.Security;
using FloatingDeskAssistant.Models;
using FloatingDeskAssistant.ViewModels;

internal static class LoginTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var logger = new QuietLogger();
        var handler = new ResponseHandler();
        using var service = new WillGoUserValidationService(logger, handler);
        foreach (var status in new[] { "VALID", "BOUND_SUCCESS", "WHITELIST_ALLOW" })
        {
            handler.Body = JsonSerializer.Serialize(new { success = true, status, message = "Test reply" });
            var result = await service.ValidateAsync("test-user-0001", "192.0.2.1", CancellationToken.None);
            check(result.Success && result.IsAllowed, "original validation protocol accepts explicit " + status);
        }
        check(handler.Endpoint == "https://43.155.247.243/willGo/api/public/willgo-users/validate" && handler.ValidBody, "login sends original userId and ip JSON contract to original endpoint");
        foreach (var status in new[] { "USER_NOT_FOUND", "USER_DISABLED", "IP_MISMATCH", "UNKNOWN" })
        {
            handler.Body = JsonSerializer.Serialize(new { success = true, status, message = "Denied" });
            check(!(await service.ValidateAsync("test-user-0001", "192.0.2.1", CancellationToken.None)).IsAllowed, "validation rejects " + status);
        }
        handler.Body = "{\"success\":false,\"status\":\"VALID\"}";
        check(!(await service.ValidateAsync("test-user-0001", "192.0.2.1", CancellationToken.None)).IsAllowed, "status text cannot override unsuccessful validation");
        foreach (var status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.InternalServerError })
        {
            handler.Status = status;
            check(!(await service.ValidateAsync("test-user-0001", "192.0.2.1", CancellationToken.None)).IsAllowed, $"HTTP {(int)status} never grants access");
        }
        handler.Status = HttpStatusCode.OK;
        handler.Body = "invalid json";
        check(!(await service.ValidateAsync("test-user-0001", "192.0.2.1", CancellationToken.None)).IsAllowed, "malformed server response never grants access");
        handler.Error = new HttpRequestException("Simulated network failure");
        check(!(await service.ValidateAsync("test-user-0001", "192.0.2.1", CancellationToken.None)).IsAllowed, "network failure never grants offline access");
        handler.Error = new TaskCanceledException("Simulated timeout");
        check(!(await service.ValidateAsync("test-user-0001", "192.0.2.1", CancellationToken.None)).IsAllowed, "validation timeout never grants offline access");

        var validation = new FakeValidation();
        using var login = new LoginViewModel(validation, new FixedIp(), logger, "test-user-0001", true);
        var accepted = 0;
        login.CloseRequested += success => { if (success) accepted++; };
        check(login.UserId == "test-user-0001" && login.SuccessfulResult is null && validation.Calls == 0, "remembered account prefill does not authorize a session");
        foreach (var result in new[]
        {
            new WillGoUserValidationResult { StatusCode = "USER_DISABLED" },
            new WillGoUserValidationResult { Success = true, IsAllowed = true, IsServerUnavailable = true },
            new WillGoUserValidationResult { IsAllowed = true, Success = false }
        })
        {
            validation.Result = result;
            login.LoginCommand.Execute(null);
            await WaitAsync(() => login.LoginCommand.CanExecute(null));
            check(accepted == 0 && login.SuccessfulResult is null, "login stays closed to denied, unavailable or unsuccessful results");
        }
        validation.Result = new() { Success = true, IsAllowed = true, StatusCode = "VALID" };
        login.LoginCommand.Execute(null);
        await WaitAsync(() => accepted == 1);
        check(login.SuccessfulResult is { Success: true, IsAllowed: true }, "regular accounts complete login after successful online validation");

        var offlineValidation = new FakeValidation { Result = new() { IsServerUnavailable = true } };
        var offlineIp = new MissingIp();
        var local = new LoginViewModel(offlineValidation, offlineIp, logger, "d1anskr", true);
        var localAccepted = 0;
        local.CloseRequested += success => { if (success) localAccepted++; };
        check(local.SuccessfulResult is null && localAccepted == 0, "remembered local code still requires an explicit login action");
        foreach (var input in new[] { "d1anskr", "  d1anskr  " })
        {
            local.UserId = input; local.LoginCommand.Execute(null);
            await WaitAsync(() => local.LoginCommand.CanExecute(null));
            check(local.SuccessfulResult is { Success: true, IsAllowed: true, IsServerUnavailable: false, StatusCode: "LOCAL_ACCESS" },
                "exact local code accepts login before account length or network checks");
        }
        check(localAccepted == 2 && offlineIp.Calls == 0 && offlineValidation.Calls == 0,
            "local access performs no IP lookup or remote validation even without a network");
        foreach (var input in new[] { "d1anskR", "d1anskrx", "xd1anskr", "d1anskr-other-user" })
        {
            local.UserId = input; local.LoginCommand.Execute(null);
            await WaitAsync(() => local.LoginCommand.CanExecute(null));
            check(localAccepted == 2 && local.SuccessfulResult is null, "similar or case-mismatched local codes never grant access");
        }
        local.Dispose(); local.UserId = "d1anskr"; local.LoginCommand.Execute(null);
        check(localAccepted == 2 && local.SuccessfulResult is null, "disposed login cannot authorize using the local code");

        var pending = new FakeValidation { Delay = true, Result = validation.Result };
        var cancelled = new LoginViewModel(pending, new FixedIp(), logger) { UserId = "test-user-0001" };
        var lateAccepted = false;
        cancelled.CloseRequested += success => lateAccepted |= success;
        cancelled.LoginCommand.Execute(null);
        cancelled.Dispose();
        await WaitAsync(() => !cancelled.IsBusy);
        check(!lateAccepted && cancelled.SuccessfulResult is null, "closing login cancels pending validation without late authorization");

        var config = AppConfig.CreateDefault(); config.RememberUserId = true; config.RememberedUserId = "test-user-0001";
        var store = new AppConfigService(new ApiKeyProtector(), logger);
        var path = Path.Combine(AppContext.BaseDirectory, "login-settings-test.json");
        typeof(AppConfigService).GetField("_filePath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(store, path);
        await store.SaveAsync(config);
        var loaded = await store.LoadAsync();
        check(loaded.RememberUserId && loaded.Clone().RememberedUserId == config.RememberedUserId, "remembered account survives persistence and config cloning");
        config.RememberUserId = false;
        await store.SaveAsync(config);
        check((await store.LoadAsync()).RememberedUserId == "" && !File.ReadAllText(path).Contains("test-user-0001"), "unchecking remember removes saved account ID");
    }

    private static async Task WaitAsync(Func<bool> ready)
    {
        var timeout = DateTime.UtcNow.AddSeconds(3);
        while (!ready()) { if (DateTime.UtcNow > timeout) throw new TimeoutException("Login test timed out"); await Task.Delay(20); }
    }

    internal sealed class FakeValidation : IWillGoUserValidationService
    {
        public WillGoUserValidationResult Result { get; set; } = new();
        public int Calls { get; private set; }
        public bool Delay { get; set; }
        public async Task<WillGoUserValidationResult> ValidateAsync(string id, string ip, CancellationToken token)
        {
            Calls++;
            if (Delay) await Task.Delay(Timeout.Infinite, token);
            return Result;
        }
    }

    internal sealed class FixedIp : IClientIpResolver { public string GetCurrentIpv4Address() => "192.0.2.1"; }
    private sealed class MissingIp : IClientIpResolver
    {
        public int Calls { get; private set; }
        public string GetCurrentIpv4Address() { Calls++; return ""; }
    }
    internal sealed class MemoryConfig : IAppConfigService
    {
        public AppConfig? Saved { get; private set; }
        public AppConfig CreateDefault() => AppConfig.CreateDefault();
        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDefault());
        public Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default) { Saved = config.Clone(); return Task.CompletedTask; }
    }

    private sealed class QuietLogger : ILoggerService
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? ex = null) => throw new InvalidOperationException(message, ex);
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        public string Body { get; set; } = "{}";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public Exception? Error { get; set; }
        public string? Endpoint { get; private set; }
        public bool ValidBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Error is not null) throw Error;
            Endpoint = request.RequestUri!.ToString();
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            ValidBody = request.Method == HttpMethod.Post && json.RootElement.GetProperty("userId").GetString() == "test-user-0001"
                && json.RootElement.GetProperty("ip").GetString() == "192.0.2.1";
            return new(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }
}
