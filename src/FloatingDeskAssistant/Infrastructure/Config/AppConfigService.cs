using System.IO;
using System.Text.Json;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Infrastructure.Security;

namespace FloatingDeskAssistant.Infrastructure.Config;

public sealed class AppConfigService : IAppConfigService
{
    private const int SaveRetryCount = 4;
    private const double DefaultBallOpacity = 0.28;
    private const double DefaultWindowOpacity = 0.82;
    private const double DefaultMessageFontSize = 14.0;
    private readonly ApiKeyProtector _protector;
    private readonly ILoggerService _logger;
    private readonly string _filePath;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppConfigService(ApiKeyProtector protector, ILoggerService logger)
    {
        _protector = protector;
        _logger = logger;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatingDeskAssistant");
        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, "appsettings.user.json");
    }

    public AppConfig CreateDefault() => AppConfig.CreateDefault();

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            var defaultConfig = CreateDefault();
            await SaveAsync(defaultConfig, cancellationToken).ConfigureAwait(false);
            return defaultConfig;
        }

        try
        {
            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var persisted = await JsonSerializer.DeserializeAsync<PersistedAppConfig>(stream, _serializerOptions, cancellationToken).ConfigureAwait(false);
            if (persisted is null)
            {
                _logger.Warn("Config deserialize returned null. Falling back to defaults.");
                return CreateDefault();
            }

            var config = CreateDefault();
            var primary = persisted.Primary ?? new ModelEndpointConfig();
            var secondary = persisted.Secondary ?? new ModelEndpointConfig();
            config.Primary.BaseUrl = string.IsNullOrWhiteSpace(primary.BaseUrl) ? config.Primary.BaseUrl : primary.BaseUrl;
            config.Primary.Protocol = primary.Protocol;
            config.Primary.Model = string.IsNullOrWhiteSpace(primary.Model) ? config.Primary.Model : primary.Model;
            config.Primary.SystemPrompt = string.IsNullOrWhiteSpace(primary.SystemPrompt) ? config.Primary.SystemPrompt : primary.SystemPrompt;
            config.Primary.TimeoutSeconds = primary.TimeoutSeconds > 0 ? primary.TimeoutSeconds : config.Primary.TimeoutSeconds;
            config.Primary.MaxRetries = primary.MaxRetries > 0 ? primary.MaxRetries : config.Primary.MaxRetries;
            config.Primary.ApiKey = _protector.Unprotect(persisted.PrimaryApiKeyProtected);
            config.Secondary.BaseUrl = string.IsNullOrWhiteSpace(secondary.BaseUrl) ? config.Secondary.BaseUrl : secondary.BaseUrl;
            config.Secondary.Protocol = secondary.Protocol;
            config.Secondary.Model = string.IsNullOrWhiteSpace(secondary.Model) ? config.Secondary.Model : secondary.Model;
            config.Secondary.SystemPrompt = string.IsNullOrWhiteSpace(secondary.SystemPrompt) ? config.Secondary.SystemPrompt : secondary.SystemPrompt;
            config.Secondary.TimeoutSeconds = secondary.TimeoutSeconds > 0 ? secondary.TimeoutSeconds : config.Secondary.TimeoutSeconds;
            config.Secondary.MaxRetries = secondary.MaxRetries > 0 ? secondary.MaxRetries : config.Secondary.MaxRetries;
            config.Secondary.ApiKey = _protector.Unprotect(persisted.SecondaryApiKeyProtected);
            if (persisted.PromptPresets is not null)
            {
                config.PromptPresets = NormalizePromptPresets(persisted.PromptPresets);
            }

            config.PreferredModel = persisted.PreferredModel;
            config.CompactChatMode = persisted.CompactChatMode;
            config.CompactModeLevel = NormalizeCompactModeLevel(persisted.CompactModeLevel);
            config.MessageFontSize = ClampMessageFontSize(persisted.MessageFontSize, DefaultMessageFontSize);
            config.BallOpacity = ClampOpacity(persisted.BallOpacity, 0.05, 1.0, DefaultBallOpacity);
            config.WindowOpacity = ClampOpacity(persisted.WindowOpacity, 0.3, 1.0, DefaultWindowOpacity);
            config.RemoteCaptureMode = NormalizeRemoteCaptureMode(persisted.RemoteCaptureMode);
            config.RemoteCaptureRegionX = ClampCoordinate(persisted.RemoteCaptureRegionX);
            config.RemoteCaptureRegionY = ClampCoordinate(persisted.RemoteCaptureRegionY);
            config.RemoteCaptureRegionWidth = ClampRegionSize(persisted.RemoteCaptureRegionWidth);
            config.RemoteCaptureRegionHeight = ClampRegionSize(persisted.RemoteCaptureRegionHeight);
            return config;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load config file.", ex);
            return CreateDefault();
        }
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        var persisted = new PersistedAppConfig
        {
            Primary = new ModelEndpointConfig { Name = "Primary", Protocol = config.Primary.Protocol, BaseUrl = config.Primary.BaseUrl, Model = config.Primary.Model, SystemPrompt = config.Primary.SystemPrompt, TimeoutSeconds = Math.Max(1, config.Primary.TimeoutSeconds), MaxRetries = Math.Max(1, config.Primary.MaxRetries) },
            Secondary = new ModelEndpointConfig { Name = "Secondary", Protocol = config.Secondary.Protocol, BaseUrl = config.Secondary.BaseUrl, Model = config.Secondary.Model, SystemPrompt = config.Secondary.SystemPrompt, TimeoutSeconds = Math.Max(1, config.Secondary.TimeoutSeconds), MaxRetries = Math.Max(1, config.Secondary.MaxRetries) },
            PromptPresets = NormalizePromptPresets(config.PromptPresets),
            PreferredModel = config.PreferredModel,
            CompactChatMode = config.CompactChatMode,
            CompactModeLevel = NormalizeCompactModeLevel(config.CompactModeLevel),
            MessageFontSize = ClampMessageFontSize(config.MessageFontSize, DefaultMessageFontSize),
            PrimaryApiKeyProtected = _protector.Protect(config.Primary.ApiKey),
            SecondaryApiKeyProtected = _protector.Protect(config.Secondary.ApiKey),
            BallOpacity = ClampOpacity(config.BallOpacity, 0.05, 1.0, DefaultBallOpacity),
            WindowOpacity = ClampOpacity(config.WindowOpacity, 0.3, 1.0, DefaultWindowOpacity),
            RemoteCaptureMode = NormalizeRemoteCaptureMode(config.RemoteCaptureMode),
            RemoteCaptureRegionX = ClampCoordinate(config.RemoteCaptureRegionX),
            RemoteCaptureRegionY = ClampCoordinate(config.RemoteCaptureRegionY),
            RemoteCaptureRegionWidth = ClampRegionSize(config.RemoteCaptureRegionWidth),
            RemoteCaptureRegionHeight = ClampRegionSize(config.RemoteCaptureRegionHeight)
        };

        var tempFilePath = _filePath + ".tmp";
        Exception? lastException = null;
        for (var attempt = 1; attempt <= SaveRetryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(stream, persisted, _serializerOptions, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                if (File.Exists(_filePath))
                {
                    File.Replace(tempFilePath, _filePath, null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempFilePath, _filePath, overwrite: true);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastException = ex;
                TryDeleteTemp(tempFilePath);
                if (attempt == SaveRetryCount)
                {
                    break;
                }

                await Task.Delay(120 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new IOException($"Failed to save config after {SaveRetryCount} attempts.", lastException);
    }

    private static CompactModeLevel NormalizeCompactModeLevel(CompactModeLevel mode)
    {
        return Enum.IsDefined(typeof(CompactModeLevel), mode)
            ? mode
            : CompactModeLevel.Mode1;
    }

    private static RemoteCaptureMode NormalizeRemoteCaptureMode(RemoteCaptureMode mode)
    {
        return Enum.IsDefined(typeof(RemoteCaptureMode), mode)
            ? mode
            : RemoteCaptureMode.FullDesktop;
    }

    private static int ClampCoordinate(int value)
    {
        if (value < -200000 || value > 200000)
        {
            return 0;
        }

        return value;
    }

    private static int ClampRegionSize(int value)
    {
        return Math.Max(0, value);
    }

    private static double ClampOpacity(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static double ClampMessageFontSize(double value, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
        return Math.Clamp(value, 6.0, 14.0);
    }

    private static List<PromptPresetConfig> NormalizePromptPresets(IEnumerable<PromptPresetConfig>? presets)
    {
        var result = new List<PromptPresetConfig>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var preset in presets ?? Enumerable.Empty<PromptPresetConfig>())
        {
            var title = (preset.Title ?? string.Empty).Trim();
            var prompt = (preset.Prompt ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(prompt))
            {
                continue;
            }

            var id = string.IsNullOrWhiteSpace(preset.Id)
                ? Guid.NewGuid().ToString("N")
                : preset.Id.Trim();
            while (!usedIds.Add(id))
            {
                id = Guid.NewGuid().ToString("N");
            }

            result.Add(new PromptPresetConfig
            {
                Id = id,
                Title = title,
                Prompt = prompt,
                IsEnabled = preset.IsEnabled
            });
        }

        return result;
    }

    private static void TryDeleteTemp(string tempFilePath)
    {
        try
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
        catch
        {
        }
    }
}
