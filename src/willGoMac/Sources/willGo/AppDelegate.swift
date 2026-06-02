import AppKit

final class AppDelegate: NSObject, NSApplicationDelegate {
    private let logger = FileLogger()
    private lazy var configService = ConfigService(logger: logger)
    private lazy var authService = AuthService(logger: logger)
    private lazy var screenshotService = ScreenshotService(logger: logger)
    private lazy var modelClient = ModelClient(logger: logger)
    private lazy var modelRouter = ModelRouter(client: modelClient, logger: logger)

    private var config: AppConfig = .default()
    private var statusItem: NSStatusItem?
    private var chatController: ChatWindowController?
    private var remoteServer: RemoteCaptureServer?
    private var remoteAccessURLs: [String] = []

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        config = configService.load()

        Task { @MainActor in
            guard await showLoginIfNeeded() else {
                NSApp.terminate(nil)
                return
            }

            initializeMainWindow()
            initializeStatusItem()
            startRemoteServer()
            chatController?.showWindow(nil)
            NSApp.activate(ignoringOtherApps: true)
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        remoteServer?.stop()
    }

    private func showLoginIfNeeded() async -> Bool {
        let controller = LoginWindowController(
            rememberedUserId: config.rememberedUserId,
            rememberUserId: config.rememberUserId,
            authService: authService,
            ipResolver: ClientIPResolver(),
            logger: logger
        )

        guard let outcome = await controller.runModal() else {
            return false
        }

        config.rememberUserId = outcome.rememberUserId
        config.rememberedUserId = outcome.rememberUserId ? outcome.userId : ""
        do {
            try configService.save(config)
        } catch {
            logger.error("保存登录记忆配置失败。", error)
        }

        return true
    }

    private func initializeMainWindow() {
        let controller = ChatWindowController(
            config: config,
            configService: configService,
            modelRouter: modelRouter,
            screenshotService: screenshotService,
            logger: logger
        )

        controller.onConfigChanged = { [weak self] updated in
            guard let self else { return }
            self.config = updated
            self.refreshStatusMenu()
        }
        controller.remoteAccessProvider = { [weak self] in
            self?.remoteAccessURLs ?? []
        }

        chatController = controller
    }

    private func initializeStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        item.button?.image = NSImage(systemSymbolName: "message.circle", accessibilityDescription: "willGo")
        item.button?.toolTip = "willGo"
        statusItem = item
        refreshStatusMenu()
    }

    private func refreshStatusMenu() {
        let menu = NSMenu()
        menu.addItem(NSMenuItem(title: "显示/隐藏聊天窗口", action: #selector(toggleChatWindow), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: config.compactChatMode ? "切换到完整模式" : "切换到紧凑模式", action: #selector(toggleCompactMode), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "截取整屏并发送", action: #selector(captureDesktop), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "框选区域并发送", action: #selector(captureRegion), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "设置", action: #selector(openSettings), keyEquivalent: ""))

        if !remoteAccessURLs.isEmpty {
            menu.addItem(.separator())
            let remoteItem = NSMenuItem(title: "手机端：\(remoteAccessURLs[0])", action: #selector(showRemoteInfo), keyEquivalent: "")
            menu.addItem(remoteItem)
        }

        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "退出 willGo", action: #selector(quit), keyEquivalent: "q"))

        for item in menu.items {
            item.target = self
        }
        statusItem?.menu = menu
    }

    private func startRemoteServer() {
        let initialPresets = config.promptPresets
        remoteServer = RemoteCaptureServer(
            logger: logger,
            captureAction: { [weak self] in
                Task { @MainActor in
                    self?.chatController?.captureDesktopFromRemote()
                }
                return "已接受整屏截图请求，willGo 正在截取 Mac 桌面。"
            },
            presetAction: { [weak self] presetId in
                Task { @MainActor in
                    self?.chatController?.sendPresetFromRemote(presetId)
                }
                return "已接受预设问题请求。"
            },
            statusProvider: {
                "willGo 已启动"
            },
            presetsProvider: {
                initialPresets
            }
        )
        remoteAccessURLs = remoteServer?.start() ?? []
        DispatchQueue.main.async { [weak self] in
            self?.chatController?.setRemoteAccessURLs(self?.remoteAccessURLs ?? [])
            self?.refreshStatusMenu()
        }
    }

    @objc private func toggleChatWindow() {
        guard let window = chatController?.window else { return }
        if window.isVisible {
            window.orderOut(nil)
        } else {
            chatController?.showWindow(nil)
            NSApp.activate(ignoringOtherApps: true)
        }
    }

    @objc private func toggleCompactMode() {
        chatController?.toggleCompactModeFromMenu()
        config = chatController?.currentConfig ?? config
        refreshStatusMenu()
    }

    @objc private func captureDesktop() {
        chatController?.captureDesktopFromMenu()
    }

    @objc private func captureRegion() {
        chatController?.captureSelectedRegionFromMenu()
    }

    @objc private func openSettings() {
        chatController?.openSettingsFromMenu()
    }

    @objc private func showRemoteInfo() {
        chatController?.showRemoteInfo()
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }

}
