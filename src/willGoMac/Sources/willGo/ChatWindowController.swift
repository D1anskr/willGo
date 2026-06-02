import AppKit

final class ChatWindowController: NSWindowController {
    private var config: AppConfig
    private let configService: ConfigService
    private let modelRouter: ModelRouter
    private let screenshotService: ScreenshotService
    private let logger: FileLogger

    private var messages: [ChatMessage] = []
    private var sessionTurns: [ConversationTurn] = []
    private var pendingImageData: Data?
    private var pendingImage: NSImage?
    private var pendingImageLabel = ""
    private var lastFailedRequest: ConversationTurn?
    private var isBusy = false
    private var remoteAccessURLs: [String] = []

    var onConfigChanged: ((AppConfig) -> Void)?
    var remoteAccessProvider: (() -> [String])?

    private let transcriptView = NSTextView()
    private let scrollView = NSScrollView()
    private let inputField = NSTextField()
    private let statusLabel = NSTextField(labelWithString: "已就绪")
    private let sendButton = NSButton(title: "发送", target: nil, action: nil)
    private let captureButton = NSButton(title: "截屏发送", target: nil, action: nil)
    private let regionCaptureButton = NSButton(title: "区域截图", target: nil, action: nil)
    private let attachButton = NSButton(title: "附图", target: nil, action: nil)
    private let retryButton = NSButton(title: "重试", target: nil, action: nil)
    private let clearButton = NSButton(title: "清空", target: nil, action: nil)
    private let settingsButton = NSButton(title: "设置", target: nil, action: nil)
    private let compactButton = NSButton(title: "紧凑模式", target: nil, action: nil)
    private let modelButton = NSButton(title: "当前模型：主模型", target: nil, action: nil)
    private let remoteButton = NSButton(title: "手机端", target: nil, action: nil)

    var statusText: String {
        statusLabel.stringValue
    }

    var currentConfig: AppConfig {
        config
    }

    init(
        config: AppConfig,
        configService: ConfigService,
        modelRouter: ModelRouter,
        screenshotService: ScreenshotService,
        logger: FileLogger
    ) {
        self.config = config
        self.configService = configService
        self.modelRouter = modelRouter
        self.screenshotService = screenshotService
        self.logger = logger

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 620, height: 620),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = "willGo"
        window.center()
        super.init(window: window)
        buildUI()
        applyConfigPresentation()
        refreshTranscript()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    func setRemoteAccessURLs(_ urls: [String]) {
        remoteAccessURLs = urls
        remoteButton.toolTip = urls.isEmpty ? "手机端远程触发不可用" : urls.joined(separator: "\n")
    }

    func captureDesktopFromMenu() {
        captureDesktopFromRemote()
    }

    func captureSelectedRegionFromMenu() {
        captureSelectedRegion()
    }

    func captureDesktopFromRemote() {
        guard !isBusy else {
            setStatus("willGo 正在处理上一条请求，请稍后。")
            return
        }
        Task {
            await captureAndSend()
        }
    }

    func sendPresetFromRemote(_ presetId: String) {
        guard !isBusy else {
            setStatus("willGo 正在处理上一条请求，请稍后。")
            return
        }
        guard let preset = config.promptPresets.first(where: { $0.id == presetId && $0.isEnabled }) else {
            setStatus("未找到该预设问题。")
            return
        }
        Task {
            await send(text: preset.prompt, imageData: nil, image: nil, mediaType: nil, displayText: preset.title)
        }
    }

    func toggleCompactModeFromMenu() {
        config.compactChatMode.toggle()
        saveConfigAndApply("紧凑模式已\(config.compactChatMode ? "开启" : "关闭")")
    }

    func openSettingsFromMenu() {
        openSettings()
    }

    func showRemoteInfo() {
        let urls = remoteAccessProvider?() ?? remoteAccessURLs
        let message = urls.isEmpty ? "手机端远程触发不可用。" : urls.joined(separator: "\n")
        let alert = NSAlert()
        alert.messageText = "手机端远程触发"
        alert.informativeText = message
        alert.addButton(withTitle: "好")
        alert.runModal()
    }

    private func buildUI() {
        guard let content = window?.contentView else { return }

        transcriptView.isEditable = false
        transcriptView.isSelectable = true
        transcriptView.font = .monospacedSystemFont(ofSize: config.messageFontSize, weight: .regular)
        transcriptView.textColor = .labelColor
        transcriptView.backgroundColor = .clear

        scrollView.documentView = transcriptView
        scrollView.hasVerticalScroller = true
        scrollView.borderType = .bezelBorder
        scrollView.translatesAutoresizingMaskIntoConstraints = false

        inputField.placeholderString = "输入问题，或先附图/截图后发送"
        inputField.target = self
        inputField.action = #selector(sendCurrentInput)

        let topBar = NSStackView(views: [modelButton, compactButton, settingsButton, remoteButton])
        topBar.orientation = .horizontal
        topBar.spacing = 8

        let actions = NSStackView(views: [sendButton, captureButton, regionCaptureButton, attachButton, retryButton, clearButton])
        actions.orientation = .horizontal
        actions.spacing = 8

        let root = NSStackView(views: [topBar, scrollView, inputField, actions, statusLabel])
        root.orientation = .vertical
        root.spacing = 10
        root.translatesAutoresizingMaskIntoConstraints = false
        content.addSubview(root)

        NSLayoutConstraint.activate([
            root.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 14),
            root.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -14),
            root.topAnchor.constraint(equalTo: content.topAnchor, constant: 14),
            root.bottomAnchor.constraint(equalTo: content.bottomAnchor, constant: -14),
            scrollView.heightAnchor.constraint(greaterThanOrEqualToConstant: 320),
            inputField.heightAnchor.constraint(equalToConstant: 30)
        ])

        wireButtons()
    }

    private func wireButtons() {
        sendButton.target = self
        sendButton.action = #selector(sendCurrentInput)
        captureButton.target = self
        captureButton.action = #selector(captureCurrentDesktop)
        regionCaptureButton.target = self
        regionCaptureButton.action = #selector(captureSelectedRegion)
        attachButton.target = self
        attachButton.action = #selector(attachImage)
        retryButton.target = self
        retryButton.action = #selector(retryLast)
        clearButton.target = self
        clearButton.action = #selector(clearSession)
        settingsButton.target = self
        settingsButton.action = #selector(openSettingsAction)
        compactButton.target = self
        compactButton.action = #selector(toggleCompact)
        modelButton.target = self
        modelButton.action = #selector(togglePreferredModel)
        remoteButton.target = self
        remoteButton.action = #selector(showRemoteInfoAction)
    }

    @objc private func sendCurrentInput() {
        let text = inputField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty || pendingImageData != nil else { return }
        let displayText: String
        if let pendingImageData {
            displayText = text.isEmpty ? "[已附加图片：\(pendingImageLabel)]" : "\(text)\n[已附加图片：\(pendingImageLabel)]"
            Task {
                await send(text: text.isEmpty ? "请分析所附图片。" : text, imageData: pendingImageData, image: pendingImage, mediaType: "image/png", displayText: displayText)
            }
        } else {
            Task {
                await send(text: text, imageData: nil, image: nil, mediaType: nil, displayText: nil)
            }
        }
        inputField.stringValue = ""
        pendingImageData = nil
        pendingImage = nil
        pendingImageLabel = ""
    }

    @objc private func captureCurrentDesktop() {
        Task {
            await captureAndSend()
        }
    }

    @objc private func captureSelectedRegion() {
        guard !isBusy else { return }
        let selector = ScreenshotSelectionWindowController()
        guard let rect = selector.runModal() else {
            setStatus("已取消区域截图")
            return
        }

        Task {
            await captureRegionAndSend(rect)
        }
    }

    @objc private func attachImage() {
        guard let result = screenshotService.chooseImageFile() else { return }
        pendingImageData = result.imageData
        pendingImage = result.image
        pendingImageLabel = result.imagePath.map { URL(fileURLWithPath: $0).lastPathComponent } ?? "图片"
        setStatus("已附加图片：\(pendingImageLabel)")
    }

    @objc private func retryLast() {
        guard let lastFailedRequest, !isBusy else { return }
        Task {
            await send(
                text: lastFailedRequest.text,
                imageData: lastFailedRequest.imageData,
                image: lastFailedRequest.imageData.flatMap(NSImage.init(data:)),
                mediaType: lastFailedRequest.imageMediaType,
                displayText: "正在重试上一次请求..."
            )
        }
    }

    @objc private func clearSession() {
        messages.removeAll()
        sessionTurns.removeAll()
        lastFailedRequest = nil
        refreshTranscript()
        setStatus("会话已清空")
    }

    @objc private func openSettingsAction() {
        openSettings()
    }

    @objc private func toggleCompact() {
        toggleCompactModeFromMenu()
    }

    @objc private func togglePreferredModel() {
        config.preferredModel = config.preferredModel == .primary ? .secondary : .primary
        saveConfigAndApply(config.preferredModel == .primary ? "已切换为主模型" : "已切换为备用模型")
    }

    @objc private func showRemoteInfoAction() {
        showRemoteInfo()
    }

    private func captureAndSend() async {
        guard !isBusy else { return }
        setStatus("正在截取整屏...")
        guard let result = await screenshotService.captureFullDesktop() else {
            addMessage(role: .error, text: "整屏截图失败，请在系统设置中授予屏幕录制权限。", image: nil)
            setStatus("整屏截图失败")
            return
        }
        let userText = inputField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        inputField.stringValue = ""
        let modelText = userText.isEmpty ? "请直接基于所附截图进行分析。" : "\(userText)\n请结合所附截图作答，不要读取任何本地文件路径。"
        let displayText = userText.isEmpty ? "请分析这张截图。" : "\(userText)\n截图路径：\(result.imagePath ?? "")"
        await send(text: modelText, imageData: result.imageData, image: result.image, mediaType: "image/png", displayText: displayText)
    }

    private func captureRegionAndSend(_ rect: CGRect) async {
        guard !isBusy else { return }
        setStatus("正在截取区域...")
        guard let result = await screenshotService.captureRegion(rect) else {
            addMessage(role: .error, text: "区域截图失败，请检查屏幕录制权限后重试。", image: nil)
            setStatus("区域截图失败")
            return
        }

        let userText = inputField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        inputField.stringValue = ""
        let modelText = userText.isEmpty ? "请直接基于所附区域截图进行分析。" : "\(userText)\n请结合所附区域截图作答，不要读取任何本地文件路径。"
        let displayText = userText.isEmpty ? "请分析这张区域截图。" : "\(userText)\n区域截图路径：\(result.imagePath ?? "")"
        await send(text: modelText, imageData: result.imageData, image: result.image, mediaType: "image/png", displayText: displayText)
    }

    private func send(text: String, imageData: Data?, image: NSImage?, mediaType: String?, displayText: String?) async {
        guard !isBusy else { return }
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || imageData != nil else { return }

        isBusy = true
        updateButtonStates()
        setStatus("处理中...")
        lastFailedRequest = nil

        addMessage(role: .user, text: displayText ?? text, image: image)
        let turn = ConversationTurn(role: .user, text: text, imageData: imageData, imageMediaType: mediaType)
        sessionTurns.append(turn)

        do {
            let reply = try await modelRouter.ask(config: config, turns: sessionTurns)
            sessionTurns.append(ConversationTurn(role: .assistant, text: reply.text, imageData: nil, imageMediaType: nil))
            addMessage(role: .assistant, text: reply.text, image: nil)
            setStatus(reply.warning ?? (reply.usedSecondary ? "已完成（备用模型）" : "已完成"))
        } catch {
            logger.error("模型请求失败。", error)
            lastFailedRequest = turn
            addMessage(role: .error, text: error.localizedDescription, image: nil)
            setStatus("请求失败，可再次重试。")
        }

        isBusy = false
        updateButtonStates()
    }

    private func openSettings() {
        let controller = SettingsWindowController(config: config)
        guard let updated = controller.runModal() else { return }
        config = updated
        saveConfigAndApply("设置已保存")
    }

    private func saveConfigAndApply(_ status: String) {
        do {
            try configService.save(config)
            applyConfigPresentation()
            onConfigChanged?(config)
            setStatus(status)
        } catch {
            logger.error("保存设置失败。", error)
            addMessage(role: .error, text: "保存设置失败，请检查配置文件权限。", image: nil)
            setStatus("保存设置失败")
        }
    }

    private func applyConfigPresentation() {
        transcriptView.font = .monospacedSystemFont(ofSize: config.messageFontSize, weight: .regular)
        window?.alphaValue = CGFloat(config.windowOpacity)
        compactButton.title = config.compactChatMode ? "完整模式" : "紧凑模式"
        modelButton.title = config.preferredModel == .secondary ? "当前模型：备用" : "当前模型：主模型"
        if config.compactChatMode {
            window?.setContentSize(NSSize(width: 420, height: 320))
        }
        refreshTranscript()
    }

    private func addMessage(role: ChatRole, text: String, image: NSImage?) {
        messages.append(ChatMessage(role: role, text: text, image: image))
        refreshTranscript()
    }

    private func refreshTranscript() {
        let visible = config.compactChatMode ? Array(messages.suffix(1)) : messages
        let attributed = NSMutableAttributedString()
        for message in visible {
            let prefix: String
            switch message.role {
            case .user: prefix = "我"
            case .assistant: prefix = "willGo"
            case .system: prefix = "系统"
            case .error: prefix = "错误"
            }
            let block = "\(prefix)：\n\(message.text)\n\n"
            let attrs: [NSAttributedString.Key: Any] = [
                .font: NSFont.monospacedSystemFont(ofSize: config.messageFontSize, weight: .regular),
                .foregroundColor: color(for: message.role)
            ]
            attributed.append(NSAttributedString(string: block, attributes: attrs))
        }
        transcriptView.textStorage?.setAttributedString(attributed)
        transcriptView.scrollToEndOfDocument(nil)
    }

    private func color(for role: ChatRole) -> NSColor {
        switch role {
        case .user: return .labelColor
        case .assistant: return .controlAccentColor
        case .system: return .secondaryLabelColor
        case .error: return .systemRed
        }
    }

    private func setStatus(_ value: String) {
        statusLabel.stringValue = value
    }

    private func updateButtonStates() {
        for button in [sendButton, captureButton, regionCaptureButton, attachButton, settingsButton, modelButton, compactButton] {
            button.isEnabled = !isBusy
        }
        retryButton.isEnabled = !isBusy && lastFailedRequest != nil
    }
}
