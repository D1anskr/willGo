import AppKit
import Foundation

enum ChatRole: String, Codable {
    case user
    case assistant
    case system
    case error
}

enum ModelAPIProtocol: String, Codable, CaseIterable {
    case auto
    case openAIChatCompletions
    case openAIResponses
    case anthropicMessages

    var title: String {
        switch self {
        case .auto: return "Auto"
        case .openAIChatCompletions: return "OpenAI Chat"
        case .openAIResponses: return "OpenAI Responses"
        case .anthropicMessages: return "Anthropic"
        }
    }
}

enum PreferredModelTarget: String, Codable {
    case primary
    case secondary
}

enum RemoteCaptureMode: String, Codable {
    case fullDesktop
    case presetRegion
}

struct ModelEndpointConfig: Codable {
    var name: String
    var apiProtocol: ModelAPIProtocol
    var baseURL: String
    var apiKey: String
    var model: String
    var systemPrompt: String
    var timeoutSeconds: Int
    var maxRetries: Int

    static func primaryDefault() -> ModelEndpointConfig {
        ModelEndpointConfig(
            name: "Primary",
            apiProtocol: .auto,
            baseURL: "https://api.openai.com/v1",
            apiKey: "",
            model: "gpt-4o-mini",
            systemPrompt: "你是一个桌面截图分析助手。请准确描述截图内容，并给出可执行建议。",
            timeoutSeconds: 30,
            maxRetries: 3
        )
    }

    static func secondaryDefault() -> ModelEndpointConfig {
        var config = primaryDefault()
        config.name = "Secondary"
        config.maxRetries = 2
        return config
    }
}

struct PromptPresetConfig: Codable, Identifiable {
    var id: String
    var title: String
    var prompt: String
    var isEnabled: Bool

    static func defaults() -> [PromptPresetConfig] {
        [
            PromptPresetConfig(
                id: UUID().uuidString,
                title: "生产者消费者",
                prompt: """
                请实现一个线程安全的生产者消费者模型。
                请按以下结构回答：
                1. 简要复述问题
                2. 说明同步策略
                3. 给出正确实现
                4. 分析时间复杂度和并发风险
                5. 说明 wait/notify 或条件变量的适用场景
                """,
                isEnabled: true
            ),
            PromptPresetConfig(
                id: UUID().uuidString,
                title: "LRU 缓存",
                prompt: """
                请实现一个 LRU 缓存。
                请按以下结构回答：
                1. 核心思路
                2. 使用到的数据结构
                3. 复杂度分析
                4. 完整实现
                5. 常见边界情况
                """,
                isEnabled: true
            ),
            PromptPresetConfig(
                id: UUID().uuidString,
                title: "二叉树层序遍历",
                prompt: """
                请解答二叉树层序遍历问题。
                请按以下结构回答：
                1. 题目摘要
                2. BFS 解法
                3. 复杂度分析
                4. 完整实现
                5. 边界情况
                """,
                isEnabled: true
            )
        ]
    }
}

struct AppConfig: Codable {
    var primary: ModelEndpointConfig
    var secondary: ModelEndpointConfig
    var promptPresets: [PromptPresetConfig]
    var preferredModel: PreferredModelTarget
    var compactChatMode: Bool
    var messageFontSize: Double
    var windowOpacity: Double
    var remoteCaptureMode: RemoteCaptureMode
    var remoteCaptureRegionX: Double
    var remoteCaptureRegionY: Double
    var remoteCaptureRegionWidth: Double
    var remoteCaptureRegionHeight: Double
    var rememberUserId: Bool
    var rememberedUserId: String

    static func `default`() -> AppConfig {
        AppConfig(
            primary: .primaryDefault(),
            secondary: .secondaryDefault(),
            promptPresets: PromptPresetConfig.defaults(),
            preferredModel: .primary,
            compactChatMode: false,
            messageFontSize: 14.0,
            windowOpacity: 0.88,
            remoteCaptureMode: .fullDesktop,
            remoteCaptureRegionX: 0,
            remoteCaptureRegionY: 0,
            remoteCaptureRegionWidth: 0,
            remoteCaptureRegionHeight: 0,
            rememberUserId: false,
            rememberedUserId: ""
        )
    }
}

struct ConversationTurn {
    var role: ChatRole
    var text: String
    var imageData: Data?
    var imageMediaType: String?
}

struct ChatMessage {
    let id = UUID()
    let role: ChatRole
    var text: String
    var image: NSImage?
}

struct ModelReply {
    var text: String
    var providerName: String
    var usedSecondary: Bool
    var warning: String?
}

struct ValidationResult {
    var isAllowed: Bool
    var success: Bool
    var statusCode: String
    var message: String
    var isServerUnavailable: Bool
}

struct ScreenshotCaptureResult {
    var imageData: Data
    var image: NSImage
    var imagePath: String?
}

enum ModelError: LocalizedError {
    case emptyAPIKey(String)
    case requestFailed(String, retryable: Bool)
    case responseFormat(String)

    var errorDescription: String? {
        switch self {
        case let .emptyAPIKey(name):
            return "\(name) API key 为空。"
        case let .requestFailed(message, _):
            return message
        case let .responseFormat(message):
            return message
        }
    }

    var isRetryable: Bool {
        switch self {
        case .emptyAPIKey:
            return false
        case let .requestFailed(_, retryable):
            return retryable
        case .responseFormat:
            return true
        }
    }
}
