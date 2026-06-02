import AppKit
import CoreGraphics
import Foundation

final class ScreenshotService {
    private let logger: FileLogger

    init(logger: FileLogger) {
        self.logger = logger
    }

    func captureFullDesktop() async -> ScreenshotCaptureResult? {
        guard let image = CGWindowListCreateImage(
            CGRect.infinite,
            .optionOnScreenOnly,
            kCGNullWindowID,
            .bestResolution
        ) else {
            logger.warn("整屏截图失败，可能缺少屏幕录制权限。")
            return nil
        }
        return result(from: image, fileNamePrefix: "desktop")
    }

    func captureRegion(_ rect: CGRect) async -> ScreenshotCaptureResult? {
        guard rect.width >= 6, rect.height >= 6 else { return nil }
        guard let image = CGWindowListCreateImage(
            rect,
            .optionOnScreenOnly,
            kCGNullWindowID,
            .bestResolution
        ) else {
            logger.warn("区域截图失败。")
            return nil
        }
        return result(from: image, fileNamePrefix: "region")
    }

    func chooseImageFile() -> ScreenshotCaptureResult? {
        let panel = NSOpenPanel()
        panel.title = "选择要附加的图片"
        panel.allowedFileTypes = ["png", "jpg", "jpeg", "gif", "bmp", "webp"]
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        guard panel.runModal() == .OK, let url = panel.url else { return nil }
        guard let data = try? Data(contentsOf: url), let image = NSImage(data: data) else {
            return nil
        }
        return ScreenshotCaptureResult(imageData: data, image: image, imagePath: url.path)
    }

    private func result(from cgImage: CGImage, fileNamePrefix: String) -> ScreenshotCaptureResult? {
        let bitmap = NSBitmapImageRep(cgImage: cgImage)
        guard let data = bitmap.representation(using: .png, properties: [:]) else {
            return nil
        }
        let image = NSImage(cgImage: cgImage, size: .zero)
        let directory = AppPaths.applicationSupportDirectory.appendingPathComponent("Screenshots", isDirectory: true)
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let url = directory.appendingPathComponent("\(fileNamePrefix)-\(Int(Date().timeIntervalSince1970)).png")
        try? data.write(to: url)
        return ScreenshotCaptureResult(imageData: data, image: image, imagePath: url.path)
    }
}
