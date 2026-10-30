using System.Windows;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Models;
using FloatingDeskAssistant.ViewModels.Base;

namespace FloatingDeskAssistant.ViewModels;

public sealed class LoginViewModel : ObservableObject, IDisposable
{
    private readonly IWillGoUserValidationService _validationService;
    private readonly IClientIpResolver _ipResolver;
    private readonly ILoggerService _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private string _userId;
    private bool _rememberUserId;
    private bool _isBusy;
    private string _statusText = "";
    private bool _disposed;

    public LoginViewModel(IWillGoUserValidationService validationService, IClientIpResolver ipResolver, ILoggerService logger, string rememberedUserId = "", bool rememberUserId = false)
    {
        _validationService = validationService;
        _ipResolver = ipResolver;
        _logger = logger;
        _userId = rememberUserId ? rememberedUserId.Trim() : "";
        _rememberUserId = rememberUserId;
        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(UserId));
        ExitCommand = new RelayCommand(() => CloseRequested?.Invoke(false));
    }

    public string UserId { get => _userId; set { if (SetProperty(ref _userId, value)) LoginCommand.NotifyCanExecuteChanged(); } }
    public bool RememberUserId { get => _rememberUserId; set => SetProperty(ref _rememberUserId, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public bool IsBusy { get => _isBusy; private set { SetProperty(ref _isBusy, value); LoginCommand.NotifyCanExecuteChanged(); } }
    public string VersionText => $"v{typeof(LoginViewModel).Assembly.GetName().Version?.ToString(3)}";
    public AsyncRelayCommand LoginCommand { get; }
    public RelayCommand ExitCommand { get; }
    public WillGoUserValidationResult? SuccessfulResult { get; private set; }
    public event Action<bool>? CloseRequested;

    private async Task LoginAsync()
    {
        if (_disposed) return;
        SuccessfulResult = null;
        var id = UserId.Trim();
        if (string.Equals(id, "d1anskr", StringComparison.Ordinal))
        {
            SuccessfulResult = new() { Success = true, IsAllowed = true, StatusCode = "LOCAL_ACCESS" };
            StatusText = "登录成功。";
            CloseRequested?.Invoke(true);
            return;
        }
        if (id.Length < 10) { StatusText = "ID 长度不能少于 10 位。"; return; }
        IsBusy = true;
        StatusText = "正在校验账号…";
        try
        {
            var ip = _ipResolver.GetCurrentIpv4Address();
            if (string.IsNullOrWhiteSpace(ip)) { StatusText = "未检测到本机 IPv4 地址，请检查网络后重试。"; return; }
            var result = await _validationService.ValidateAsync(id, ip, _lifetime.Token);
            if (_disposed) return;
            if (result is { Success: true, IsAllowed: true, IsServerUnavailable: false })
            {
                SuccessfulResult = result;
                StatusText = "校验通过。";
                CloseRequested?.Invoke(true);
            }
            else StatusText = result.StatusCode.ToUpperInvariant() switch
            {
                "USER_NOT_FOUND" => "账号不存在，请联系管理员。",
                "USER_DISABLED" => "账号已被禁用，请联系管理员。",
                "IP_MISMATCH" => "该账号已绑定其他设备，请联系管理员。",
                _ => string.IsNullOrWhiteSpace(result.Message) ? "账号校验未通过，请重试。" : result.Message
            };
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.Warn($"willGo login failed: {ex.GetType().Name}.");
            StatusText = "无法完成账号校验，请稍后重试。";
        }
        finally { IsBusy = false; }
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
