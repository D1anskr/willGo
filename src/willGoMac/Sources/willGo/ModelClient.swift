import Foundation

final class ModelClient {
    private let logger: FileLogger

    init(logger: FileLogger) {
        self.logger = logger
    }

    func send(endpoint: ModelEndpointConfig, turns: [ConversationTurn]) async throws -> String {
        guard !endpoint.apiKey.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw ModelError.emptyAPIKey(endpoint.name)
        }

        let preparedTurns = prepareTurnsForPayload(turns)
        let protocols: [ModelAPIProtocol]
        if endpoint.apiProtocol == .auto {
            protocols = preparedTurns.contains { $0.imageData != nil }
                ? [.openAIResponses, .openAIChatCompletions, .anthropicMessages]
                : [.openAIChatCompletions, .openAIResponses, .anthropicMessages]
        } else {
            protocols = [endpoint.apiProtocol]
        }

        var lastError: Error?
        for apiProtocol in protocols {
            do {
                logger.info("\(endpoint.name) request start. protocol=\(apiProtocol.rawValue), turns=\(preparedTurns.count)")
                let text: String
                switch apiProtocol {
                case .auto:
                    continue
                case .openAIChatCompletions:
                    text = try await sendOpenAIChat(endpoint: endpoint, turns: preparedTurns)
                case .openAIResponses:
                    text = try await sendOpenAIResponses(endpoint: endpoint, turns: preparedTurns)
                case .anthropicMessages:
                    text = try await sendAnthropic(endpoint: endpoint, turns: preparedTurns)
                }

                if !looksLikeMissingImageContext(text) {
                    return text
                }

                lastError = ModelError.requestFailed("\(endpoint.name) 返回内容显示没有收到图片。", retryable: true)
            } catch {
                lastError = error
                logger.warn("\(endpoint.name) protocol \(apiProtocol.rawValue) failed: \(error.localizedDescription)")
                if let modelError = error as? ModelError, !modelError.isRetryable {
                    break
                }
            }
        }

        throw lastError ?? ModelError.requestFailed("\(endpoint.name) 请求失败。", retryable: true)
    }

    private func sendOpenAIChat(endpoint: ModelEndpointConfig, turns: [ConversationTurn]) async throws -> String {
        let url = apiRootCandidates(endpoint.baseURL, defaultRoot: "https://api.openai.com/v1")
            .compactMap { appendPath("chat/completions", to: $0) }
        let payload = buildOpenAIChatPayload(endpoint: endpoint, turns: turns)
        return try await postFirstSuccessful(urls: url, endpoint: endpoint, payload: payload, authHeader: ("Authorization", "Bearer \(endpoint.apiKey)")) { json in
            if let choices = json["choices"] as? [[String: Any]],
               let message = choices.first?["message"] as? [String: Any] {
                if let content = message["content"] as? String {
                    return content
                }
                if let content = message["content"] as? [[String: Any]] {
                    return content.compactMap { $0["text"] as? String }.joined(separator: "\n")
                }
            }
            throw ModelError.responseFormat("\(endpoint.name) Chat Completions 返回格式不支持。")
        }
    }

    private func sendOpenAIResponses(endpoint: ModelEndpointConfig, turns: [ConversationTurn]) async throws -> String {
        let url = apiRootCandidates(endpoint.baseURL, defaultRoot: "https://api.openai.com/v1")
            .compactMap { appendPath("responses", to: $0) }
        let payload = buildOpenAIResponsesPayload(endpoint: endpoint, turns: turns)
        return try await postFirstSuccessful(urls: url, endpoint: endpoint, payload: payload, authHeader: ("Authorization", "Bearer \(endpoint.apiKey)")) { json in
            if let outputText = json["output_text"] as? String, !outputText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                return outputText
            }

            if let output = json["output"] as? [[String: Any]] {
                let parts = output.flatMap { item -> [String] in
                    guard let content = item["content"] as? [[String: Any]] else { return [] }
                    return content.compactMap { $0["text"] as? String }
                }
                let joined = parts.joined(separator: "\n")
                if !joined.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    return joined
                }
            }

            if json["choices"] != nil {
                return try Self.parseChatFallback(endpointName: endpoint.name, json: json)
            }

            throw ModelError.responseFormat("\(endpoint.name) Responses 返回格式不支持。")
        }
    }

    private func sendAnthropic(endpoint: ModelEndpointConfig, turns: [ConversationTurn]) async throws -> String {
        let url = apiRootCandidates(endpoint.baseURL, defaultRoot: "https://api.anthropic.com/v1")
            .compactMap { appendPath("messages", to: $0) }
        let payload = buildAnthropicPayload(endpoint: endpoint, turns: turns)
        return try await postFirstSuccessful(
            urls: url,
            endpoint: endpoint,
            payload: payload,
            authHeader: ("x-api-key", endpoint.apiKey),
            extraHeaders: ["anthropic-version": "2023-06-01"]
        ) { json in
            guard let content = json["content"] as? [[String: Any]] else {
                throw ModelError.responseFormat("\(endpoint.name) Anthropic 返回格式不支持。")
            }
            let joined = content.compactMap { item -> String? in
                guard item["type"] as? String == "text" else { return nil }
                return item["text"] as? String
            }.joined(separator: "\n")
            if joined.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                throw ModelError.responseFormat("\(endpoint.name) Anthropic 返回为空。")
            }
            return joined
        }
    }

    private func postFirstSuccessful(
        urls: [URL],
        endpoint: ModelEndpointConfig,
        payload: [String: Any],
        authHeader: (String, String),
        extraHeaders: [String: String] = [:],
        parse: ([String: Any]) throws -> String
    ) async throws -> String {
        var lastError: Error?
        for url in urls {
            do {
                return try await post(url: url, endpoint: endpoint, payload: payload, authHeader: authHeader, extraHeaders: extraHeaders, parse: parse)
            } catch {
                lastError = error
                if let modelError = error as? ModelError, !modelError.isRetryable {
                    throw error
                }
            }
        }
        throw lastError ?? ModelError.requestFailed("\(endpoint.name) 请求失败。", retryable: true)
    }

    private func post(
        url: URL,
        endpoint: ModelEndpointConfig,
        payload: [String: Any],
        authHeader: (String, String),
        extraHeaders: [String: String],
        parse: ([String: Any]) throws -> String
    ) async throws -> String {
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.timeoutInterval = TimeInterval(max(endpoint.timeoutSeconds, payloadHasImage(payload) ? 60 : 1))
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("willGo-mac/1.0", forHTTPHeaderField: "User-Agent")
        request.setValue(authHeader.1, forHTTPHeaderField: authHeader.0)
        for (key, value) in extraHeaders {
            request.setValue(value, forHTTPHeaderField: key)
        }
        request.httpBody = try JSONSerialization.data(withJSONObject: payload)

        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse else {
            throw ModelError.requestFailed("\(endpoint.name) 返回无效响应。", retryable: true)
        }

        guard (200..<300).contains(http.statusCode) else {
            let snippet = String(data: data, encoding: .utf8)?.prefix(180) ?? ""
            let retryable = http.statusCode == 429 || http.statusCode >= 500
            let permanent = http.statusCode == 401 || http.statusCode == 403 || http.statusCode == 404
            throw ModelError.requestFailed("\(endpoint.name) HTTP \(http.statusCode)。\(snippet)", retryable: retryable && !permanent)
        }

        guard let json = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw ModelError.responseFormat("\(endpoint.name) 未返回 JSON。")
        }
        return try parse(json)
    }

    private static func parseChatFallback(endpointName: String, json: [String: Any]) throws -> String {
        guard let choices = json["choices"] as? [[String: Any]],
              let message = choices.first?["message"] as? [String: Any] else {
            throw ModelError.responseFormat("\(endpointName) Chat 返回格式不支持。")
        }
        return message["content"] as? String ?? ""
    }

    private func buildOpenAIChatPayload(endpoint: ModelEndpointConfig, turns: [ConversationTurn]) -> [String: Any] {
        var messages: [[String: Any]] = [
            ["role": "system", "content": endpoint.systemPrompt]
        ]

        for turn in turns {
            let role = turn.role == .assistant ? "assistant" : "user"
            if let imageData = turn.imageData {
                var content: [[String: Any]] = []
                if !turn.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    content.append(["type": "text", "text": turn.text])
                }
                content.append([
                    "type": "image_url",
                    "image_url": ["url": imageDataURI(imageData, mediaType: turn.imageMediaType)]
                ])
                messages.append(["role": role, "content": content])
            } else {
                messages.append(["role": role, "content": turn.text])
            }
        }

        return ["model": endpoint.model, "messages": messages]
    }

    private func buildOpenAIResponsesPayload(endpoint: ModelEndpointConfig, turns: [ConversationTurn]) -> [String: Any] {
        let input = turns.map { turn -> [String: Any] in
            var content: [[String: Any]] = []
            if !turn.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                content.append(["type": "input_text", "text": turn.text])
            }
            if let imageData = turn.imageData {
                content.append(["type": "input_image", "image_url": imageDataURI(imageData, mediaType: turn.imageMediaType)])
            }
            if content.isEmpty {
                content.append(["type": "input_text", "text": ""])
            }
            return ["role": turn.role == .assistant ? "assistant" : "user", "content": content]
        }
        return ["model": endpoint.model, "instructions": endpoint.systemPrompt, "input": input]
    }

    private func buildAnthropicPayload(endpoint: ModelEndpointConfig, turns: [ConversationTurn]) -> [String: Any] {
        let messages = turns.map { turn -> [String: Any] in
            var content: [[String: Any]] = []
            if !turn.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                content.append(["type": "text", "text": turn.text])
            }
            if let imageData = turn.imageData {
                content.append([
                    "type": "image",
                    "source": [
                        "type": "base64",
                        "media_type": normalizeMediaType(turn.imageMediaType),
                        "data": imageData.base64EncodedString()
                    ]
                ])
            }
            if content.isEmpty {
                content.append(["type": "text", "text": ""])
            }
            return ["role": turn.role == .assistant ? "assistant" : "user", "content": content]
        }

        return [
            "model": endpoint.model,
            "system": endpoint.systemPrompt,
            "max_tokens": 1024,
            "messages": messages
        ]
    }

    private func prepareTurnsForPayload(_ turns: [ConversationTurn]) -> [ConversationTurn] {
        guard !turns.isEmpty else { return turns }
        if let lastImageTurn = turns.last(where: { $0.imageData != nil }), turns.last?.imageData != nil {
            return [ConversationTurn(
                role: .user,
                text: lastImageTurn.text.isEmpty ? "请分析所附截图。" : lastImageTurn.text,
                imageData: lastImageTurn.imageData,
                imageMediaType: lastImageTurn.imageMediaType
            )]
        }

        return turns.suffix(16).map {
            ConversationTurn(role: $0.role, text: $0.text, imageData: nil, imageMediaType: nil)
        }
    }

    private func apiRootCandidates(_ raw: String, defaultRoot: String) -> [URL] {
        var value = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        if value.isEmpty {
            value = defaultRoot
        }
        if !value.hasPrefix("http://") && !value.hasPrefix("https://") {
            value = "https://" + value
        }
        value = value.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        for suffix in ["/chat/completions", "/responses", "/messages"] where value.hasSuffix(suffix) {
            value.removeLast(suffix.count)
        }

        var values = [value]
        if !value.lowercased().hasSuffix("/v1") {
            values.append(value + "/v1")
        }
        return values.compactMap(URL.init(string:))
    }

    private func appendPath(_ path: String, to url: URL) -> URL? {
        URL(string: url.absoluteString.trimmingCharacters(in: CharacterSet(charactersIn: "/")) + "/" + path)
    }

    private func imageDataURI(_ data: Data, mediaType: String?) -> String {
        "data:\(normalizeMediaType(mediaType));base64,\(data.base64EncodedString())"
    }

    private func normalizeMediaType(_ mediaType: String?) -> String {
        guard let mediaType, mediaType.hasPrefix("image/") else {
            return "image/png"
        }
        return mediaType
    }

    private func payloadHasImage(_ payload: [String: Any]) -> Bool {
        let raw = String(describing: payload)
        return raw.contains("image_url") || raw.contains("input_image")
    }

    private func looksLikeMissingImageContext(_ text: String) -> Bool {
        let value = text.lowercased()
        return (value.contains("cannot") || value.contains("can't") || value.contains("无法") || value.contains("看不到")) &&
            (value.contains("image") || value.contains("screenshot") || value.contains("图片") || value.contains("截图"))
    }
}

final class ModelRouter {
    private let client: ModelClient
    private let logger: FileLogger

    init(client: ModelClient, logger: FileLogger) {
        self.client = client
        self.logger = logger
    }

    func ask(config: AppConfig, turns: [ConversationTurn]) async throws -> ModelReply {
        let preferredIsSecondary = config.preferredModel == .secondary
        let preferred = preferredIsSecondary ? config.secondary : config.primary
        let fallback = preferredIsSecondary ? config.primary : config.secondary

        if let text = try await executeWithRetries(endpoint: preferred, turns: turns) {
            return ModelReply(text: text, providerName: preferred.name, usedSecondary: preferredIsSecondary, warning: nil)
        }

        logger.warn("\(preferred.name) 不可用，切换到 \(fallback.name)。")
        if let text = try await executeWithRetries(endpoint: fallback, turns: turns) {
            return ModelReply(
                text: text,
                providerName: fallback.name,
                usedSecondary: !preferredIsSecondary,
                warning: preferredIsSecondary ? "备用模型不可用，已切回主模型。" : "主模型不可用，已切换到备用模型。"
            )
        }

        throw ModelError.requestFailed("主模型和备用模型均不可用，请检查 API Key、Base URL、模型名和网络。", retryable: true)
    }

    private func executeWithRetries(endpoint: ModelEndpointConfig, turns: [ConversationTurn]) async throws -> String? {
        var lastError: Error?
        for attempt in 1...max(1, endpoint.maxRetries) {
            do {
                return try await client.send(endpoint: endpoint, turns: turns)
            } catch {
                lastError = error
                let retryable = (error as? ModelError)?.isRetryable ?? true
                if !retryable || attempt >= max(1, endpoint.maxRetries) {
                    break
                }
                try await Task.sleep(nanoseconds: UInt64(500 * attempt) * 1_000_000)
            }
        }
        if let lastError {
            logger.warn("\(endpoint.name) failed after retries: \(lastError.localizedDescription)")
        }
        return nil
    }
}
