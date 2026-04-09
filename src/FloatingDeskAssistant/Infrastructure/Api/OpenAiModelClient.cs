using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Models;

namespace FloatingDeskAssistant.Infrastructure.Api;

public sealed class OpenAiModelClient : IModelClient
{
    private const string OpenAiDefaultRoot = "https://api.openai.com/v1";
    private const string AnthropicDefaultRoot = "https://api.anthropic.com/v1";
    private static readonly Regex MissingImageReplyRegex = new(
        "(can't|cannot|do not|don't).*(see|access|view).*(image|picture|screenshot)" +
        "|(no|without).*(image|picture|screenshot)" +
        "|(re-?attach|upload).*(image|picture|screenshot)" +
        "|(看不到|没看到|未看到|无法看到|无法接收|无法收到|没法收到|未收到).*(图|图片|截图)" +
        "|(无法|不能|没法).*(分析|识别).*(图|图片|截图)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly ILoggerService _logger;

    public OpenAiModelClient(ILoggerService logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
    }

    public async Task<string> SendAsync(ModelEndpointConfig endpoint, IReadOnlyList<ConversationTurn> turns, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint.ApiKey))
        {
            throw new ModelApiException($"{endpoint.Name} API key is empty.", false, HttpStatusCode.Unauthorized);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var hasImageInput = turns.Any(static t => t.ImageBytes is { Length: > 0 });
        var totalImageBytes = turns.Where(static t => t.ImageBytes is { Length: > 0 }).Sum(static t => t.ImageBytes?.Length ?? 0);
        _logger.Info($"{endpoint.Name} request start. protocol={endpoint.Protocol}, turns={turns.Count}, hasImage={hasImageInput}, imageBytes={totalImageBytes}");
        var timeoutSeconds = Math.Max(1, endpoint.TimeoutSeconds);
        if (hasImageInput)
        {
            timeoutSeconds = Math.Max(timeoutSeconds, 60);
        }

        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        return endpoint.Protocol switch
        {
            ModelApiProtocol.OpenAiChatCompletions => await SendOpenAiChatCompletionsAsync(endpoint, turns, cts.Token),
            ModelApiProtocol.OpenAiResponses => await SendOpenAiResponsesAsync(endpoint, turns, cts.Token),
            ModelApiProtocol.AnthropicMessages => await SendAnthropicMessagesAsync(endpoint, turns, cts.Token),
            _ => await SendAutoAsync(endpoint, turns, cts.Token)
        };
    }

    private async Task<string> SendAutoAsync(ModelEndpointConfig endpoint, IReadOnlyList<ConversationTurn> turns, CancellationToken cancellationToken)
    {
        if (turns.Any(static t => t.ImageBytes is { Length: > 0 }))
        {
            return await SendAutoWithImageAsync(endpoint, turns, cancellationToken);
        }

        ModelApiException? lastException = null;

        var attempts = new List<(string Name, Func<Task<string>> Handler)>
        {
            ("OpenAI Chat Completions", () => SendOpenAiChatCompletionsAsync(endpoint, turns, cancellationToken)),
            ("OpenAI Responses", () => SendOpenAiResponsesAsync(endpoint, turns, cancellationToken)),
            ("Anthropic Messages", () => SendAnthropicMessagesAsync(endpoint, turns, cancellationToken))
        };

        foreach (var (name, handler) in attempts)
        {
            try
            {
                _logger.Info($"{endpoint.Name} protocol attempt '{name}' start.");
                var result = await handler();
                _logger.Info($"{endpoint.Name} protocol attempt '{name}' success.");
                return result;
            }
            catch (ModelApiException ex)
            {
                lastException = ex;
                _logger.Warn($"{endpoint.Name} protocol attempt '{name}' failed: {ex.Message}");

                if (ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
                {
                    break;
                }
            }
        }

        throw new ModelApiException(
            $"{endpoint.Name} all protocol attempts failed. Last error: {lastException?.Message ?? "unknown"}",
            lastException?.IsRetryable ?? true,
            lastException?.StatusCode,
            lastException);
    }

    private async Task<string> SendAutoWithImageAsync(ModelEndpointConfig endpoint, IReadOnlyList<ConversationTurn> turns, CancellationToken cancellationToken)
    {
        ModelApiException? lastException = null;

        try
        {
            _logger.Info($"{endpoint.Name} protocol attempt 'OpenAI Responses' start.");
            var result = await SendOpenAiResponsesAsync(endpoint, turns, cancellationToken);
            if (!LooksLikeNoImageContext(result))
            {
                _logger.Info($"{endpoint.Name} protocol attempt 'OpenAI Responses' success.");
                return result;
            }

            lastException = new ModelApiException(
                $"{endpoint.Name} protocol 'OpenAI Responses' replied without image context.",
                true);
            _logger.Warn($"{endpoint.Name} protocol attempt 'OpenAI Responses' returned text indicating image was not seen.");
        }
        catch (ModelApiException ex)
        {
            lastException = ex;
            _logger.Warn($"{endpoint.Name} protocol attempt 'OpenAI Responses' failed: {ex.Message}");
            if (ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
            {
                throw;
            }
        }

        if (turns.Count > 1)
        {
            var oneShotTurns = BuildOneShotVisionTurns(turns);
            try
            {
                _logger.Info($"{endpoint.Name} fallback attempt 'OpenAI Responses one-shot' start.");
                var result = await SendOpenAiResponsesAsync(endpoint, oneShotTurns, cancellationToken);
                if (!LooksLikeNoImageContext(result))
                {
                    _logger.Info($"{endpoint.Name} fallback attempt 'OpenAI Responses one-shot' success.");
                    return result;
                }

                lastException = new ModelApiException(
                    $"{endpoint.Name} fallback protocol 'OpenAI Responses one-shot' replied without image context.",
                    true);
                _logger.Warn($"{endpoint.Name} fallback attempt 'OpenAI Responses one-shot' returned text indicating image was not seen.");
            }
            catch (ModelApiException ex)
            {
                lastException = ex;
                _logger.Warn($"{endpoint.Name} fallback attempt 'OpenAI Responses one-shot' failed: {ex.Message}");
                if (ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw;
                }
            }
        }

        if (ShouldSkipChatCompletionsImageFallback(endpoint))
        {
            _logger.Warn($"{endpoint.Name} skipped 'OpenAI Chat Completions' image fallback for baseUrl '{endpoint.BaseUrl}' because many third-party gateways ignore multimodal image parts on that route.");
            throw new ModelApiException(
                $"{endpoint.Name} image request stopped after OpenAI Responses attempts to avoid a text-only fallback. Last error: {lastException?.Message ?? "unknown"}",
                lastException?.IsRetryable ?? true,
                lastException?.StatusCode,
                lastException);
        }

        try
        {
            _logger.Info($"{endpoint.Name} protocol attempt 'OpenAI Chat Completions' start.");
            var result = await SendOpenAiChatCompletionsAsync(endpoint, turns, cancellationToken);
            if (!LooksLikeNoImageContext(result))
            {
                _logger.Info($"{endpoint.Name} protocol attempt 'OpenAI Chat Completions' success.");
                return result;
            }

            lastException = new ModelApiException(
                $"{endpoint.Name} protocol 'OpenAI Chat Completions' replied without image context.",
                true);
            _logger.Warn($"{endpoint.Name} protocol attempt 'OpenAI Chat Completions' returned text indicating image was not seen.");
        }
        catch (ModelApiException ex)
        {
            lastException = ex;
            _logger.Warn($"{endpoint.Name} protocol attempt 'OpenAI Chat Completions' failed: {ex.Message}");
            if (ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
            {
                throw;
            }
        }

        throw new ModelApiException(
            $"{endpoint.Name} all protocol attempts failed. Last error: {lastException?.Message ?? "unknown"}",
            lastException?.IsRetryable ?? true,
            lastException?.StatusCode,
            lastException);
    }

    private static bool ShouldSkipChatCompletionsImageFallback(ModelEndpointConfig endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.BaseUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(endpoint.BaseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        var isOfficialOpenAiHost = host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase);
        var isDashScopeCompatibleHost = host.Equals("dashscope.aliyuncs.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("dashscope-intl.aliyuncs.com", StringComparison.OrdinalIgnoreCase);

        return !isOfficialOpenAiHost && !isDashScopeCompatibleHost;
    }

    private static HttpContent CreateJsonContent(string requestBody)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(requestBody));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        return content;
    }
    private async Task<string> SendOpenAiChatCompletionsAsync(ModelEndpointConfig endpoint, IReadOnlyList<ConversationTurn> turns, CancellationToken cancellationToken)
    {
        ModelApiException? lastException = null;
        var apiRoots = BuildApiRootCandidates(endpoint.BaseUrl, OpenAiDefaultRoot, includeV1Fallback: true);
        var preparedTurns = PrepareTurnsForPayload(turns);

        foreach (var apiRoot in apiRoots)
        {
            var requestUri = CombineUrl(apiRoot, "/chat/completions");
            var payload = BuildOpenAiChatPayload(endpoint, preparedTurns);
            var requestBody = JsonSerializer.Serialize(payload);

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = CreateJsonContent(requestBody)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
            request.Headers.UserAgent.ParseAdd("FloatingDeskAssistant/1.0");

            try
            {
                var (response, body) = await SendAsyncInternal(endpoint.Name, request, cancellationToken);
                return ParseOpenAiChatResponse(endpoint.Name, response, body, requestUri);
            }
            catch (ModelApiException ex)
            {
                lastException = ex;
                if (ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw;
                }
            }
        }

        throw lastException ?? new ModelApiException($"{endpoint.Name} request failed.", true);
    }

    private async Task<string> SendOpenAiResponsesAsync(ModelEndpointConfig endpoint, IReadOnlyList<ConversationTurn> turns, CancellationToken cancellationToken)
    {
        ModelApiException? lastException = null;
        var apiRoots = BuildApiRootCandidates(endpoint.BaseUrl, OpenAiDefaultRoot, includeV1Fallback: true);
        var preparedTurns = PrepareTurnsForPayload(turns);

        foreach (var apiRoot in apiRoots)
        {
            var requestUri = CombineUrl(apiRoot, "/responses");
            var payload = BuildOpenAiResponsesPayload(endpoint, preparedTurns);
            var requestBody = JsonSerializer.Serialize(payload);

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = CreateJsonContent(requestBody)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
            request.Headers.UserAgent.ParseAdd("FloatingDeskAssistant/1.0");

            try
            {
                var (response, body) = await SendAsyncInternal(endpoint.Name, request, cancellationToken);
                return ParseOpenAiResponsesResponse(endpoint.Name, response, body, requestUri);
            }
            catch (ModelApiException ex)
            {
                lastException = ex;
                if (ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw;
                }
            }
        }

        throw lastException ?? new ModelApiException($"{endpoint.Name} request failed.", true);
    }

    private async Task<string> SendAnthropicMessagesAsync(ModelEndpointConfig endpoint, IReadOnlyList<ConversationTurn> turns, CancellationToken cancellationToken)
    {
        ModelApiException? lastException = null;
        var apiRoots = BuildApiRootCandidates(endpoint.BaseUrl, AnthropicDefaultRoot, includeV1Fallback: true);
        var preparedTurns = PrepareTurnsForPayload(turns);

        foreach (var apiRoot in apiRoots)
        {
            var requestUri = CombineUrl(apiRoot, "/messages");
            var payload = BuildAnthropicPayload(endpoint, preparedTurns);
            var requestBody = JsonSerializer.Serialize(payload);

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = CreateJsonContent(requestBody)
            };
            request.Headers.TryAddWithoutValidation("x-api-key", endpoint.ApiKey);
            request.Headers.UserAgent.ParseAdd("FloatingDeskAssistant/1.0");
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

            try
            {
                var (response, body) = await SendAsyncInternal(endpoint.Name, request, cancellationToken);
                return ParseAnthropicResponse(endpoint.Name, response, body, requestUri);
            }
            catch (ModelApiException ex)
            {
                lastException = ex;
                if (ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw;
                }
            }
        }

        throw lastException ?? new ModelApiException($"{endpoint.Name} request failed.", true);
    }

    private async Task<(HttpResponseMessage Response, string Body)> SendAsyncInternal(
        string endpointName,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows() && ShouldUseWebRequest(request.RequestUri))
        {
            return await SendWithWebRequestAsync(endpointName, request, cancellationToken);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            throw new ModelApiException($"{endpointName} request timed out.", true, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ModelApiException($"{endpointName} connection failed.", true, null, ex);
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateByStatus(endpointName, response.StatusCode, responseBody);
        }

        return (response, responseBody);
    }

#pragma warning disable SYSLIB0014
    private static async Task<(HttpResponseMessage Response, string Body)> SendWithWebRequestAsync(
        string endpointName,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is null)
        {
            throw new ModelApiException($"{endpointName} request URI is missing.", false);
        }

        var webRequest = WebRequest.CreateHttp(request.RequestUri);
        webRequest.Method = request.Method.Method;
        webRequest.AllowAutoRedirect = false;
        webRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli;
        webRequest.ReadWriteTimeout = Timeout.Infinite;
        webRequest.Timeout = Timeout.Infinite;

        foreach (var header in request.Headers)
        {
            var value = string.Join(",", header.Value);
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                webRequest.Headers[HttpRequestHeader.Authorization] = value;
                continue;
            }

            if (header.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase))
            {
                webRequest.Accept = value;
                continue;
            }

            if (header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
            {
                webRequest.UserAgent = value;
                continue;
            }

            webRequest.Headers[header.Key] = value;
        }

        byte[]? requestBodyBytes = null;
        if (request.Content is not null)
        {
            requestBodyBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = request.Content.Headers.ContentType?.ToString();
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                webRequest.ContentType = contentType;
            }

            if (request.Content.Headers.ContentLength is long contentLength && contentLength >= 0)
            {
                webRequest.ContentLength = contentLength;
            }
            else
            {
                webRequest.ContentLength = requestBodyBytes.Length;
            }

            foreach (var header in request.Content.Headers)
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                webRequest.Headers[header.Key] = string.Join(",", header.Value);
            }

            await using var requestStream = await webRequest.GetRequestStreamAsync();
            await requestStream.WriteAsync(requestBodyBytes, cancellationToken);
        }

        try
        {
            using var response = (HttpWebResponse)await webRequest.GetResponseAsync();
            return await CreateResponseTupleAsync(request, response, cancellationToken);
        }
        catch (WebException ex) when (ex.Response is HttpWebResponse errorResponse)
        {
            var result = await CreateResponseTupleAsync(request, errorResponse, cancellationToken);
            throw CreateByStatus(endpointName, result.Response.StatusCode, result.Body);
        }
        catch (OperationCanceledException ex)
        {
            throw new ModelApiException($"{endpointName} request timed out.", true, null, ex);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ProtocolViolationException)
        {
            throw new ModelApiException($"{endpointName} connection failed.", true, null, ex);
        }
    }
#pragma warning restore SYSLIB0014

    private static bool ShouldUseWebRequest(Uri? requestUri)
    {
        if (requestUri is null)
        {
            return false;
        }

        var host = requestUri.Host;
        return host.Equals("vpsairobot.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".vpsairobot.com", StringComparison.OrdinalIgnoreCase);
    }
    private static async Task<(HttpResponseMessage Response, string Body)> CreateResponseTupleAsync(
        HttpRequestMessage request,
        HttpWebResponse response,
        CancellationToken cancellationToken)
    {
        await using var responseStream = response.GetResponseStream();
        using var reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);

        var responseMessage = new HttpResponseMessage(response.StatusCode)
        {
            RequestMessage = request,
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        if (!string.IsNullOrWhiteSpace(response.ContentType))
        {
            responseMessage.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(response.ContentType);
        }

        return (responseMessage, body);
    }

    private static string ParseOpenAiChatResponse(string endpointName, HttpResponseMessage response, string body, string requestUri)
    {
        using var doc = ParseJsonOrThrow(endpointName, response, body, requestUri);
        var root = doc.RootElement;

        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            throw new ModelApiException($"{endpointName} returned an empty choices array.", true, response.StatusCode);
        }

        var message = choices[0].GetProperty("message");
        var content = message.GetProperty("content");
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in content.EnumerateArray())
            {
                if (item.TryGetProperty("text", out var textElement))
                {
                    parts.Add(textElement.GetString() ?? string.Empty);
                }
            }

            var joined = string.Join(Environment.NewLine, parts.Where(static s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(joined))
            {
                return joined;
            }
        }

        return content.ToString();
    }

    private static string ParseOpenAiResponsesResponse(string endpointName, HttpResponseMessage response, string body, string requestUri)
    {
        using var doc = ParseJsonOrThrow(endpointName, response, body, requestUri);
        var root = doc.RootElement;

        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            var text = outputText.GetString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        var collected = new List<string>();
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                    {
                        collected.Add(textElement.GetString() ?? string.Empty);
                    }
                }
            }
        }

        var joined = string.Join(Environment.NewLine, collected.Where(static s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(joined))
        {
            return joined;
        }

        // Fallback for providers that still return chat-completions shape on /responses.
        if (root.TryGetProperty("choices", out _))
        {
            return ParseOpenAiChatResponse(endpointName, response, body, requestUri);
        }

        throw new ModelApiException($"{endpointName} response format is not supported for /responses.", true, response.StatusCode);
    }

    private static string ParseAnthropicResponse(string endpointName, HttpResponseMessage response, string body, string requestUri)
    {
        using var doc = ParseJsonOrThrow(endpointName, response, body, requestUri);
        var root = doc.RootElement;

        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            throw new ModelApiException($"{endpointName} returned an unsupported Anthropic response body.", true, response.StatusCode);
        }

        var parts = new List<string>();
        foreach (var item in content.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (!string.Equals(typeElement.GetString(), "text", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (item.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
            {
                parts.Add(textElement.GetString() ?? string.Empty);
            }
        }

        var joined = string.Join(Environment.NewLine, parts.Where(static s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(joined))
        {
            throw new ModelApiException($"{endpointName} returned empty Anthropic text content.", true, response.StatusCode);
        }

        return joined;
    }

    private static JsonDocument ParseJsonOrThrow(string endpointName, HttpResponseMessage response, string body, string requestUri)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var trimmed = body.TrimStart();
        if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("<", StringComparison.Ordinal))
        {
            throw new ModelApiException(
                $"{endpointName} returned HTML instead of JSON from '{requestUri}'. Check BaseUrl format (for OpenAI-compatible APIs, include /v1).",
                false,
                response.StatusCode);
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (Exception ex)
        {
            throw new ModelApiException($"{endpointName} response parse failed: {ex.Message}", true, response.StatusCode, ex);
        }
    }

    private static ModelApiException CreateByStatus(string endpointName, HttpStatusCode statusCode, string body)
    {
        var statusCodeNumber = (int)statusCode;
        var bodySnippet = MakeSnippet(body);

        return statusCode switch
        {
            HttpStatusCode.Unauthorized => new ModelApiException($"{endpointName} authentication failed (401). Check API key. {bodySnippet}", false, statusCode),
            HttpStatusCode.Forbidden => new ModelApiException($"{endpointName} request forbidden (403). {bodySnippet}", false, statusCode),
            HttpStatusCode.TooManyRequests => new ModelApiException($"{endpointName} rate-limited (429). {bodySnippet}", true, statusCode),
            HttpStatusCode.NotFound => new ModelApiException($"{endpointName} endpoint not found (404). Check BaseUrl and protocol path, often missing '/v1'. {bodySnippet}", false, statusCode),
            _ when statusCodeNumber >= 500 => new ModelApiException($"{endpointName} server error ({statusCodeNumber}). {bodySnippet}", true, statusCode),
            _ when statusCodeNumber >= 400 => new ModelApiException($"{endpointName} request error ({statusCodeNumber}). {bodySnippet}", false, statusCode),
            _ => new ModelApiException($"{endpointName} request failed ({statusCodeNumber}). {bodySnippet}", true, statusCode)
        };
    }

    private static object BuildOpenAiChatPayload(ModelEndpointConfig endpoint, IReadOnlyList<ConversationTurn> turns)
    {
        var messages = new List<object>
        {
            new
            {
                role = "system",
                content = endpoint.SystemPrompt
            }
        };

        foreach (var turn in turns)
        {
            var role = turn.Role switch
            {
                ChatRole.Assistant => "assistant",
                ChatRole.User => "user",
                _ => "user"
            };

            if (turn.ImageBytes is not { Length: > 0 })
            {
                messages.Add(new
                {
                    role,
                    content = turn.Text ?? string.Empty
                });
            }
            else
            {
                var contentItems = new List<object>();
                if (!string.IsNullOrWhiteSpace(turn.Text))
                {
                    contentItems.Add(new { type = "text", text = turn.Text });
                }

                var imageDataUri = BuildImageDataUri(turn.ImageBytes, turn.ImageMediaType);
                contentItems.Add(new
                {
                    type = "image_url",
                    image_url = new { url = imageDataUri }
                });

                messages.Add(new
                {
                    role,
                    content = contentItems
                });
            }
        }

        return new
        {
            model = endpoint.Model,
            messages
        };
    }

    private static object BuildOpenAiResponsesPayload(ModelEndpointConfig endpoint, IReadOnlyList<ConversationTurn> turns)
    {
        var input = new List<object>();

        foreach (var turn in turns)
        {
            var role = turn.Role switch
            {
                ChatRole.Assistant => "assistant",
                ChatRole.User => "user",
                _ => "user"
            };

            var content = new List<object>();
            if (!string.IsNullOrWhiteSpace(turn.Text))
            {
                content.Add(new
                {
                    type = "input_text",
                    text = turn.Text
                });
            }

            if (turn.ImageBytes is { Length: > 0 } imageBytes)
            {
                content.Add(new
                {
                    type = "input_image",
                    image_url = BuildImageDataUri(imageBytes, turn.ImageMediaType)
                });
            }

            if (content.Count == 0)
            {
                content.Add(new
                {
                    type = "input_text",
                    text = string.Empty
                });
            }

            input.Add(new
            {
                role,
                content
            });
        }

        return new
        {
            model = endpoint.Model,
            instructions = endpoint.SystemPrompt,
            input
        };
    }

    private static object BuildAnthropicPayload(ModelEndpointConfig endpoint, IReadOnlyList<ConversationTurn> turns)
    {
        var messages = new List<object>();
        foreach (var turn in turns)
        {
            var role = turn.Role == ChatRole.Assistant ? "assistant" : "user";
            var contentItems = new List<object>();

            if (!string.IsNullOrWhiteSpace(turn.Text))
            {
                contentItems.Add(new
                {
                    type = "text",
                    text = turn.Text
                });
            }

            if (turn.ImageBytes is { Length: > 0 } imageBytes)
            {
                contentItems.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = NormalizeImageMediaType(turn.ImageMediaType),
                        data = Convert.ToBase64String(imageBytes)
                    }
                });
            }

            if (contentItems.Count == 0)
            {
                contentItems.Add(new
                {
                    type = "text",
                    text = string.Empty
                });
            }

            messages.Add(new
            {
                role,
                content = contentItems
            });
        }

        return new
        {
            model = endpoint.Model,
            system = endpoint.SystemPrompt,
            max_tokens = 1024,
            messages
        };
    }

    private static IReadOnlyList<string> BuildApiRootCandidates(string baseUrl, string defaultRoot, bool includeV1Fallback)
    {
        var normalized = NormalizeApiRoot(baseUrl, defaultRoot);
        var candidates = new List<string> { normalized };

        var hasV1 = normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase);
        if (includeV1Fallback && !hasV1)
        {
            candidates.Add(normalized + "/v1");
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string NormalizeApiRoot(string baseUrl, string defaultRoot)
    {
        var value = string.IsNullOrWhiteSpace(baseUrl) ? defaultRoot : baseUrl.Trim();
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value;
        }

        value = value.TrimEnd('/');
        value = TrimKnownSuffix(value, "/chat/completions");
        value = TrimKnownSuffix(value, "/responses");
        value = TrimKnownSuffix(value, "/messages");

        return value;
    }

    private static string TrimKnownSuffix(string value, string suffix)
    {
        if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return value[..^suffix.Length];
        }

        return value;
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        return baseUrl.TrimEnd('/') + path;
    }

    private static string MakeSnippet(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var normalized = raw.Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Length > 140)
        {
            normalized = normalized[..140] + "...";
        }

        return $"Response: {normalized}";
    }

    private static IReadOnlyList<ConversationTurn> PrepareTurnsForPayload(IReadOnlyList<ConversationTurn> turns)
    {
        if (turns.Count == 0)
        {
            return turns;
        }

        // For fresh screenshot turns, send a one-shot vision context to avoid cross-turn drift
        // and maximize multimodal stability on OpenAI-compatible gateways.
        if (turns[^1].ImageBytes is { Length: > 0 })
        {
            return BuildOneShotVisionTurns(turns);
        }

        const int maxTurns = 16;
        var start = Math.Max(0, turns.Count - maxTurns);
        var window = turns.Skip(start).ToList();

        var lastImageIndex = -1;
        for (var i = 0; i < window.Count; i++)
        {
            if (window[i].ImageBytes is { Length: > 0 })
            {
                lastImageIndex = i;
            }
        }

        if (lastImageIndex < 0)
        {
            return window;
        }

        var result = new List<ConversationTurn>(window.Count);
        for (var i = 0; i < window.Count; i++)
        {
            var turn = window[i];
            var keepImage = i == lastImageIndex;
            var text = turn.Text;

            if (!keepImage && turn.ImageBytes is { Length: > 0 } && string.IsNullOrWhiteSpace(text))
            {
                text = "[Previous screenshot omitted to keep request size stable.]";
            }

            result.Add(new ConversationTurn
            {
                Role = turn.Role,
                Text = text,
                ImageBytes = keepImage ? turn.ImageBytes : null,
                ImageMediaType = keepImage ? turn.ImageMediaType : null
            });
        }

        return result;
    }

    private static IReadOnlyList<ConversationTurn> BuildOneShotVisionTurns(IReadOnlyList<ConversationTurn> turns)
    {
        ConversationTurn? lastWithImage = null;
        for (var i = turns.Count - 1; i >= 0; i--)
        {
            if (turns[i].ImageBytes is { Length: > 0 })
            {
                lastWithImage = turns[i];
                break;
            }
        }

        if (lastWithImage is null)
        {
            return turns;
        }

        var prompt = string.IsNullOrWhiteSpace(lastWithImage.Text)
            ? "Please analyze the attached screenshot."
            : lastWithImage.Text;

        return
        [
            new ConversationTurn
            {
                Role = ChatRole.User,
                Text = prompt,
                ImageBytes = lastWithImage.ImageBytes,
                ImageMediaType = lastWithImage.ImageMediaType
            }
        ];
    }

    private static string BuildImageDataUri(byte[] imageBytes, string? imageMediaType)
    {
        return $"data:{NormalizeImageMediaType(imageMediaType)};base64,{Convert.ToBase64String(imageBytes)}";
    }

    private static string NormalizeImageMediaType(string? imageMediaType)
    {
        if (string.IsNullOrWhiteSpace(imageMediaType))
        {
            return "image/png";
        }

        var normalized = imageMediaType.Trim().ToLowerInvariant();
        if (!normalized.StartsWith("image/", StringComparison.Ordinal))
        {
            return "image/png";
        }

        return normalized;
    }

    private static bool LooksLikeNoImageContext(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return MissingImageReplyRegex.IsMatch(text);
    }
}






