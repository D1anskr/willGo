import Foundation

final class ConfigService {
    private let fileURL: URL
    private let decoder = JSONDecoder()
    private let encoder = JSONEncoder()
    private let logger: FileLogger

    init(logger: FileLogger) {
        self.logger = logger
        fileURL = AppPaths.applicationSupportDirectory.appendingPathComponent("appsettings.user.json")
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
    }

    func load() -> AppConfig {
        do {
            try FileManager.default.createDirectory(
                at: AppPaths.applicationSupportDirectory,
                withIntermediateDirectories: true
            )
            guard FileManager.default.fileExists(atPath: fileURL.path) else {
                let config = AppConfig.default()
                try save(config)
                return config
            }

            let data = try Data(contentsOf: fileURL)
            return normalize(try decoder.decode(AppConfig.self, from: data))
        } catch {
            logger.error("配置读取失败，使用默认配置。", error)
            return AppConfig.default()
        }
    }

    func save(_ config: AppConfig) throws {
        let normalized = normalize(config)
        let data = try encoder.encode(normalized)
        try FileManager.default.createDirectory(
            at: AppPaths.applicationSupportDirectory,
            withIntermediateDirectories: true
        )
        let tempURL = fileURL.appendingPathExtension("tmp")
        try data.write(to: tempURL, options: [.atomic])
        if FileManager.default.fileExists(atPath: fileURL.path) {
            try FileManager.default.removeItem(at: fileURL)
        }
        try FileManager.default.moveItem(at: tempURL, to: fileURL)
    }

    private func normalize(_ config: AppConfig) -> AppConfig {
        var next = config
        next.messageFontSize = min(max(next.messageFontSize, 10), 18)
        next.windowOpacity = min(max(next.windowOpacity, 0.35), 1.0)
        next.primary.timeoutSeconds = max(1, next.primary.timeoutSeconds)
        next.secondary.timeoutSeconds = max(1, next.secondary.timeoutSeconds)
        next.primary.maxRetries = max(1, next.primary.maxRetries)
        next.secondary.maxRetries = max(1, next.secondary.maxRetries)
        next.rememberedUserId = next.rememberUserId ? next.rememberedUserId.trimmingCharacters(in: .whitespacesAndNewlines) : ""
        next.promptPresets = next.promptPresets.filter {
            !$0.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty &&
            !$0.prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        }
        if next.promptPresets.isEmpty {
            next.promptPresets = PromptPresetConfig.defaults()
        }
        return next
    }
}
