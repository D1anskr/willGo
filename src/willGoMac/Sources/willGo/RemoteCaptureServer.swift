import Foundation
import Network

final class RemoteCaptureServer {
    private let logger: FileLogger
    private let port: UInt16
    private var listener: NWListener?
    private let captureAction: () -> String
    private let presetAction: (String) -> String
    private let statusProvider: () -> String
    private let presetsProvider: () -> [PromptPresetConfig]

    init(
        logger: FileLogger,
        port: UInt16 = 8765,
        captureAction: @escaping () -> String,
        presetAction: @escaping (String) -> String,
        statusProvider: @escaping () -> String,
        presetsProvider: @escaping () -> [PromptPresetConfig]
    ) {
        self.logger = logger
        self.port = port
        self.captureAction = captureAction
        self.presetAction = presetAction
        self.statusProvider = statusProvider
        self.presetsProvider = presetsProvider
    }

    func start() -> [String] {
        do {
            let listener = try NWListener(using: .tcp, on: NWEndpoint.Port(rawValue: port)!)
            self.listener = listener
            listener.newConnectionHandler = { [weak self] connection in
                self?.handle(connection)
            }
            listener.start(queue: .global(qos: .userInitiated))
            logger.info("远程触发服务已启动，端口 \(port)。")
            return accessURLs()
        } catch {
            logger.error("远程触发服务启动失败。", error)
            return []
        }
    }

    func stop() {
        listener?.cancel()
        listener = nil
    }

    private func handle(_ connection: NWConnection) {
        connection.start(queue: .global(qos: .userInitiated))
        connection.receive(minimumIncompleteLength: 1, maximumLength: 8192) { [weak self] data, _, _, _ in
            guard let self else {
                connection.cancel()
                return
            }
            let request = data.flatMap { String(data: $0, encoding: .utf8) } ?? ""
            let line = request.components(separatedBy: "\r\n").first ?? ""
            let path = line.split(separator: " ").dropFirst().first.map(String.init) ?? "/"
            let body: String
            let contentType: String

            if path.hasPrefix("/capture") {
                body = self.json(["success": true, "message": self.captureAction()])
                contentType = "application/json; charset=utf-8"
            } else if path.hasPrefix("/preset") {
                let id = URLComponents(string: "http://localhost\(path)")?
                    .queryItems?
                    .first(where: { $0.name == "id" })?
                    .value ?? ""
                body = self.json(["success": true, "message": self.presetAction(id)])
                contentType = "application/json; charset=utf-8"
            } else if path.hasPrefix("/status") {
                body = self.json(["status": self.statusProvider()])
                contentType = "application/json; charset=utf-8"
            } else {
                body = self.indexHTML()
                contentType = "text/html; charset=utf-8"
            }

            self.send(body: body, contentType: contentType, connection: connection)
        }
    }

    private func send(body: String, contentType: String, connection: NWConnection) {
        let data = Data(body.utf8)
        let headers = """
        HTTP/1.1 200 OK
        Content-Type: \(contentType)
        Content-Length: \(data.count)
        Connection: close

        """
        var response = Data(headers.replacingOccurrences(of: "\n", with: "\r\n").utf8)
        response.append(data)
        connection.send(content: response, completion: .contentProcessed { _ in
            connection.cancel()
        })
    }

    private func indexHTML() -> String {
        let presetButtons = presetsProvider()
            .filter { $0.isEnabled }
            .map { preset in
                let title = escapeHTML(preset.title)
                return "<button onclick=\"preset('\(preset.id)')\">\(title)</button>"
            }
            .joined(separator: "\n")

        return """
        <!doctype html>
        <html lang="zh-CN">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>willGo 远程触发</title>
        <style>
        body{font-family:-apple-system,BlinkMacSystemFont,sans-serif;margin:24px;background:#111;color:#f5f5f5}
        button{display:block;width:100%;margin:12px 0;padding:14px;border:0;border-radius:8px;background:#2f7df6;color:white;font-size:16px}
        .secondary{background:#333}
        #result{margin-top:18px;color:#a8d5ff;line-height:1.5}
        </style>
        </head>
        <body>
        <h1>willGo</h1>
        <button onclick="capture()">截取 Mac 桌面并发送</button>
        \(presetButtons)
        <button class="secondary" onclick="status()">查看状态</button>
        <div id="result"></div>
        <script>
        async function call(url){const r=await fetch(url); const j=await r.json(); document.getElementById('result').innerText=j.message||j.status||JSON.stringify(j);}
        function capture(){call('/capture')}
        function preset(id){call('/preset?id='+encodeURIComponent(id))}
        function status(){call('/status')}
        </script>
        </body>
        </html>
        """
    }

    private func accessURLs() -> [String] {
        var result = ["http://127.0.0.1:\(port)/"]
        if let lan = ClientIPResolver().currentIPv4Address().nilIfLoopback {
            result.insert("http://\(lan):\(port)/", at: 0)
        }
        return result
    }

    private func json(_ value: [String: Any]) -> String {
        guard let data = try? JSONSerialization.data(withJSONObject: value),
              let text = String(data: data, encoding: .utf8) else {
            return "{}"
        }
        return text
    }

    private func escapeHTML(_ value: String) -> String {
        value.replacingOccurrences(of: "&", with: "&amp;")
            .replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;")
            .replacingOccurrences(of: "\"", with: "&quot;")
    }
}

private extension String {
    var nilIfLoopback: String? {
        self == "127.0.0.1" ? nil : self
    }
}
