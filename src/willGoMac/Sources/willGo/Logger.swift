import Foundation

final class FileLogger {
    private let queue = DispatchQueue(label: "willgo.logger")
    private let fileURL: URL

    init() {
        let root = AppPaths.applicationSupportDirectory
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        fileURL = root.appendingPathComponent("willgo.log")
    }

    func info(_ message: String) {
        write("INFO", message)
    }

    func warn(_ message: String) {
        write("WARN", message)
    }

    func error(_ message: String, _ error: Error? = nil) {
        if let error {
            write("ERROR", "\(message) \(error.localizedDescription)")
        } else {
            write("ERROR", message)
        }
    }

    private func write(_ level: String, _ message: String) {
        queue.async { [fileURL] in
            let line = "\(ISO8601DateFormatter().string(from: Date())) [\(level)] \(message)\n"
            guard let data = line.data(using: .utf8) else { return }
            if FileManager.default.fileExists(atPath: fileURL.path) {
                if let handle = try? FileHandle(forWritingTo: fileURL) {
                    try? handle.seekToEnd()
                    try? handle.write(contentsOf: data)
                    try? handle.close()
                }
            } else {
                try? data.write(to: fileURL)
            }
        }
    }
}

enum AppPaths {
    static var applicationSupportDirectory: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser
        return base.appendingPathComponent("willGo", isDirectory: true)
    }
}
