import AppKit

final class SettingsWindowController: NSWindowController, NSWindowDelegate {
    private var config: AppConfig
    private var result: AppConfig?

    private let primaryBaseURL = NSTextField()
    private let primaryKey = NSSecureTextField()
    private let primaryModel = NSTextField()
    private let secondaryBaseURL = NSTextField()
    private let secondaryKey = NSSecureTextField()
    private let secondaryModel = NSTextField()
    private let systemPrompt = NSTextField()
    private let fontSize = NSTextField()
    private let opacity = NSTextField()

    init(config: AppConfig) {
        self.config = config
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 520, height: 520),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false
        )
        window.title = "willGo 设置"
        window.center()
        super.init(window: window)
        window.delegate = self
        buildUI()
        fillValues()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    func runModal() -> AppConfig? {
        guard let window else { return nil }
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
        NSApp.runModal(for: window)
        return result
    }

    private func buildUI() {
        guard let content = window?.contentView else { return }

        let root = NSStackView()
        root.orientation = .vertical
        root.spacing = 10
        root.translatesAutoresizingMaskIntoConstraints = false
        content.addSubview(root)

        root.addArrangedSubview(sectionTitle("主模型"))
        root.addArrangedSubview(row("Base URL", primaryBaseURL))
        root.addArrangedSubview(row("API Key", primaryKey))
        root.addArrangedSubview(row("模型", primaryModel))
        root.addArrangedSubview(sectionTitle("备用模型"))
        root.addArrangedSubview(row("Base URL", secondaryBaseURL))
        root.addArrangedSubview(row("API Key", secondaryKey))
        root.addArrangedSubview(row("模型", secondaryModel))
        root.addArrangedSubview(sectionTitle("显示"))
        root.addArrangedSubview(row("系统提示词", systemPrompt))
        root.addArrangedSubview(row("字号", fontSize))
        root.addArrangedSubview(row("窗口透明度", opacity))

        let buttons = NSStackView()
        buttons.orientation = .horizontal
        buttons.spacing = 8
        let cancel = NSButton(title: "取消", target: self, action: #selector(cancelAction))
        let save = NSButton(title: "保存", target: self, action: #selector(saveAction))
        buttons.addArrangedSubview(cancel)
        buttons.addArrangedSubview(save)
        root.addArrangedSubview(buttons)

        NSLayoutConstraint.activate([
            root.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 20),
            root.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -20),
            root.topAnchor.constraint(equalTo: content.topAnchor, constant: 20),
            root.bottomAnchor.constraint(lessThanOrEqualTo: content.bottomAnchor, constant: -20)
        ])
    }

    private func sectionTitle(_ text: String) -> NSTextField {
        let label = NSTextField(labelWithString: text)
        label.font = .systemFont(ofSize: 15, weight: .semibold)
        return label
    }

    private func row(_ title: String, _ field: NSTextField) -> NSView {
        let label = NSTextField(labelWithString: title)
        label.widthAnchor.constraint(equalToConstant: 86).isActive = true
        field.translatesAutoresizingMaskIntoConstraints = false
        field.widthAnchor.constraint(equalToConstant: 370).isActive = true
        let stack = NSStackView(views: [label, field])
        stack.orientation = .horizontal
        stack.spacing = 8
        return stack
    }

    private func fillValues() {
        primaryBaseURL.stringValue = config.primary.baseURL
        primaryKey.stringValue = config.primary.apiKey
        primaryModel.stringValue = config.primary.model
        secondaryBaseURL.stringValue = config.secondary.baseURL
        secondaryKey.stringValue = config.secondary.apiKey
        secondaryModel.stringValue = config.secondary.model
        systemPrompt.stringValue = config.primary.systemPrompt
        fontSize.stringValue = String(format: "%.0f", config.messageFontSize)
        opacity.stringValue = String(format: "%.2f", config.windowOpacity)
    }

    @objc private func saveAction() {
        config.primary.baseURL = primaryBaseURL.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        config.primary.apiKey = primaryKey.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        config.primary.model = primaryModel.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        config.primary.systemPrompt = systemPrompt.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        config.secondary.baseURL = secondaryBaseURL.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        config.secondary.apiKey = secondaryKey.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        config.secondary.model = secondaryModel.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        config.secondary.systemPrompt = config.primary.systemPrompt
        config.messageFontSize = Double(fontSize.stringValue) ?? config.messageFontSize
        config.windowOpacity = Double(opacity.stringValue) ?? config.windowOpacity
        result = config
        window?.close()
        NSApp.stopModal()
    }

    @objc private func cancelAction() {
        result = nil
        window?.close()
        NSApp.stopModal()
    }

    func windowWillClose(_ notification: Notification) {
        NSApp.stopModal()
    }
}
