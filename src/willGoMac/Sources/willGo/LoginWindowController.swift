import AppKit

struct LoginOutcome {
    let userId: String
    let rememberUserId: Bool
}

final class LoginWindowController: NSWindowController, NSWindowDelegate {
    private let authService: AuthService
    private let ipResolver: ClientIPResolver
    private let logger: FileLogger

    private let userField = NSTextField()
    private let rememberButton = NSButton(checkboxWithTitle: "记住账号", target: nil, action: nil)
    private let statusLabel = NSTextField(labelWithString: "请输入 willGo 账号。")
    private let loginButton = NSButton(title: "登录", target: nil, action: nil)
    private var continuation: CheckedContinuation<LoginOutcome?, Never>?

    init(
        rememberedUserId: String,
        rememberUserId: Bool,
        authService: AuthService,
        ipResolver: ClientIPResolver,
        logger: FileLogger
    ) {
        self.authService = authService
        self.ipResolver = ipResolver
        self.logger = logger

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 360, height: 190),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false
        )
        window.title = "willGo 登录"
        window.center()
        super.init(window: window)
        window.delegate = self

        buildUI(rememberedUserId: rememberedUserId, rememberUserId: rememberUserId)
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    func runModal() async -> LoginOutcome? {
        await withCheckedContinuation { continuation in
            self.continuation = continuation
            guard let window else {
                continuation.resume(returning: nil)
                return
            }
            NSApp.activate(ignoringOtherApps: true)
            window.makeKeyAndOrderFront(nil)
            NSApp.runModal(for: window)
        }
    }

    private func buildUI(rememberedUserId: String, rememberUserId: Bool) {
        guard let content = window?.contentView else { return }

        let title = NSTextField(labelWithString: "willGo")
        title.font = .systemFont(ofSize: 22, weight: .semibold)

        let subtitle = NSTextField(labelWithString: "登录后即可使用本机菜单栏助手。")
        subtitle.textColor = .secondaryLabelColor

        userField.placeholderString = "账号"
        userField.stringValue = rememberedUserId
        rememberButton.state = rememberUserId ? .on : .off
        statusLabel.textColor = .secondaryLabelColor
        statusLabel.lineBreakMode = .byWordWrapping

        loginButton.target = self
        loginButton.action = #selector(login)
        loginButton.keyEquivalent = "\r"

        let stack = NSStackView(views: [title, subtitle, userField, rememberButton, statusLabel, loginButton])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 10
        stack.translatesAutoresizingMaskIntoConstraints = false
        content.addSubview(stack)

        for view in [userField, statusLabel, loginButton] {
            view.translatesAutoresizingMaskIntoConstraints = false
            view.widthAnchor.constraint(equalToConstant: 300).isActive = true
        }
        loginButton.alignment = .center

        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 30),
            stack.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -30),
            stack.topAnchor.constraint(equalTo: content.topAnchor, constant: 22)
        ])
    }

    @objc private func login() {
        let userId = userField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !userId.isEmpty else {
            statusLabel.stringValue = "请输入账号。"
            return
        }

        loginButton.isEnabled = false
        statusLabel.stringValue = "正在校验账号..."

        Task {
            let ip = ipResolver.currentIPv4Address()
            let result = await authService.validate(userId: userId, ip: ip)
            await MainActor.run {
                if result.isAllowed {
                    let outcome = LoginOutcome(userId: userId, rememberUserId: rememberButton.state == .on)
                    continuation?.resume(returning: outcome)
                    continuation = nil
                    window?.close()
                    NSApp.stopModal()
                    return
                }

                logger.warn("登录失败：\(result.statusCode) \(result.message)")
                loginButton.isEnabled = true
                statusLabel.stringValue = result.message
            }
        }
    }

    func windowWillClose(_ notification: Notification) {
        if let continuation {
            self.continuation = nil
            continuation.resume(returning: nil)
            NSApp.stopModal()
        }
    }
}
