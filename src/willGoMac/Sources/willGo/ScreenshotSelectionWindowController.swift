import AppKit

final class ScreenshotSelectionWindowController: NSWindowController {
    private var selectedRect: CGRect?

    init() {
        let frame = NSScreen.screens
            .map(\.frame)
            .reduce(CGRect.null) { $0.union($1) }

        let window = NSWindow(
            contentRect: frame,
            styleMask: [.borderless],
            backing: .buffered,
            defer: false
        )
        window.level = .screenSaver
        window.backgroundColor = .clear
        window.isOpaque = false
        window.ignoresMouseEvents = false
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]

        super.init(window: window)

        let overlay = ScreenshotSelectionView(frame: NSRect(origin: .zero, size: frame.size))
        overlay.onFinished = { [weak self] rectInWindow in
            guard let self, let window = self.window else { return }
            if rectInWindow.width >= 6, rectInWindow.height >= 6 {
                let appKitScreenRect = CGRect(
                    x: window.frame.minX + rectInWindow.minX,
                    y: window.frame.minY + rectInWindow.minY,
                    width: rectInWindow.width,
                    height: rectInWindow.height
                )
                self.selectedRect = Self.convertToCoreGraphicsRect(appKitScreenRect)
            }
            window.close()
            NSApp.stopModal()
        }
        overlay.onCancelled = { [weak self] in
            self?.selectedRect = nil
            self?.window?.close()
            NSApp.stopModal()
        }
        window.contentView = overlay
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    func runModal() -> CGRect? {
        guard let window else { return nil }
        selectedRect = nil
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
        NSApp.runModal(for: window)
        return selectedRect
    }

    private static func convertToCoreGraphicsRect(_ appKitRect: CGRect) -> CGRect {
        let virtualFrame = NSScreen.screens
            .map(\.frame)
            .reduce(CGRect.null) { $0.union($1) }

        return CGRect(
            x: appKitRect.minX,
            y: virtualFrame.maxY - appKitRect.maxY,
            width: appKitRect.width,
            height: appKitRect.height
        )
    }
}

private final class ScreenshotSelectionView: NSView {
    var onFinished: ((CGRect) -> Void)?
    var onCancelled: (() -> Void)?

    private var startPoint: CGPoint?
    private var currentPoint: CGPoint?

    override var acceptsFirstResponder: Bool { true }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        window?.makeFirstResponder(self)
    }

    override func draw(_ dirtyRect: NSRect) {
        NSColor.black.withAlphaComponent(0.28).setFill()
        bounds.fill()

        guard let selection = selectionRect else { return }

        NSColor.clear.setFill()
        selection.fill(using: .clear)

        NSColor.systemBlue.setStroke()
        let path = NSBezierPath(rect: selection)
        path.lineWidth = 2
        path.stroke()

        let label = "\(Int(selection.width)) x \(Int(selection.height))"
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 13, weight: .medium),
            .foregroundColor: NSColor.white,
            .backgroundColor: NSColor.black.withAlphaComponent(0.65)
        ]
        label.draw(at: CGPoint(x: selection.minX + 8, y: max(selection.minY - 24, 8)), withAttributes: attrs)
    }

    override func mouseDown(with event: NSEvent) {
        startPoint = convert(event.locationInWindow, from: nil)
        currentPoint = startPoint
        needsDisplay = true
    }

    override func mouseDragged(with event: NSEvent) {
        currentPoint = convert(event.locationInWindow, from: nil)
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        currentPoint = convert(event.locationInWindow, from: nil)
        onFinished?(selectionRect ?? .zero)
    }

    override func keyDown(with event: NSEvent) {
        if event.keyCode == 53 {
            onCancelled?()
            return
        }
        super.keyDown(with: event)
    }

    private var selectionRect: CGRect? {
        guard let startPoint, let currentPoint else { return nil }
        return CGRect(
            x: min(startPoint.x, currentPoint.x),
            y: min(startPoint.y, currentPoint.y),
            width: abs(startPoint.x - currentPoint.x),
            height: abs(startPoint.y - currentPoint.y)
        )
    }
}
