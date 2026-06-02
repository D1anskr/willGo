import Foundation
import Darwin

final class ClientIPResolver {
    func currentIPv4Address() -> String {
        var address = "127.0.0.1"
        var interfaces: UnsafeMutablePointer<ifaddrs>?

        guard getifaddrs(&interfaces) == 0, let first = interfaces else {
            return address
        }
        defer { freeifaddrs(interfaces) }

        var pointer: UnsafeMutablePointer<ifaddrs>? = first
        while let current = pointer {
            let interface = current.pointee
            pointer = interface.ifa_next

            guard interface.ifa_addr.pointee.sa_family == UInt8(AF_INET) else { continue }
            let name = String(cString: interface.ifa_name)
            guard name != "lo0" else { continue }

            var hostname = [CChar](repeating: 0, count: Int(NI_MAXHOST))
            getnameinfo(
                interface.ifa_addr,
                socklen_t(interface.ifa_addr.pointee.sa_len),
                &hostname,
                socklen_t(hostname.count),
                nil,
                0,
                NI_NUMERICHOST
            )
            let candidate = String(cString: hostname)
            if !candidate.hasPrefix("169.254.") {
                address = candidate
                break
            }
        }

        return address
    }
}

final class AuthService {
    private let validationURL = URL(string: "https://43.155.247.243/willGo/api/public/willgo-users/validate")!
    private let logger: FileLogger

    init(logger: FileLogger) {
        self.logger = logger
    }

    func validate(userId: String, ip: String) async -> ValidationResult {
        do {
            var request = URLRequest(url: validationURL)
            request.httpMethod = "POST"
            request.timeoutInterval = 12
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.httpBody = try JSONSerialization.data(withJSONObject: ["userId": userId, "ip": ip])

            let delegate = TrustAllURLSessionDelegate()
            let session = URLSession(configuration: .ephemeral, delegate: delegate, delegateQueue: nil)
            let (data, response) = try await session.data(for: request)
            guard let http = response as? HTTPURLResponse else {
                return networkFailure("登录校验服务返回了无效响应。")
            }

            if http.statusCode == 200 {
                let payload = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
                let success = payload?["success"] as? Bool ?? false
                let status = (payload?["status"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? "UNKNOWN"
                let message = (payload?["message"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? "校验服务未返回提示信息"
                return ValidationResult(
                    isAllowed: success && isAllowed(status: status, message: message),
                    success: success,
                    statusCode: status,
                    message: message,
                    isServerUnavailable: false
                )
            }

            if http.statusCode == 400 {
                let payload = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
                let message = (payload?["message"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? "登录参数不合法"
                return ValidationResult(
                    isAllowed: false,
                    success: false,
                    statusCode: "BAD_REQUEST",
                    message: message,
                    isServerUnavailable: false
                )
            }

            return ValidationResult(
                isAllowed: false,
                success: false,
                statusCode: "HTTP_\(http.statusCode)",
                message: "登录校验失败，服务器返回 HTTP \(http.statusCode)。",
                isServerUnavailable: http.statusCode >= 500
            )
        } catch {
            logger.error("登录校验请求失败。", error)
            return networkFailure("无法连接校验服务器，请稍后重试。")
        }
    }

    private func isAllowed(status: String, message: String) -> Bool {
        let normalized = status.replacingOccurrences(of: "-", with: "_")
            .replacingOccurrences(of: " ", with: "_")
            .uppercased()

        if ["BOUND_SUCCESS", "VALID", "WHITELIST", "WHITE_LIST", "ALLOWLIST", "ALLOW_LIST", "WHITELIST_ALLOW", "ALLOWLIST_ALLOW"].contains(normalized) {
            return true
        }

        return message.localizedCaseInsensitiveContains("白名单") &&
            (message.localizedCaseInsensitiveContains("放行") ||
             message.localizedCaseInsensitiveContains("通过") ||
             message.localizedCaseInsensitiveContains("允许") ||
             message.localizedCaseInsensitiveContains("allow"))
    }

    private func networkFailure(_ message: String) -> ValidationResult {
        ValidationResult(
            isAllowed: false,
            success: false,
            statusCode: "NETWORK_ERROR",
            message: message,
            isServerUnavailable: true
        )
    }
}

private final class TrustAllURLSessionDelegate: NSObject, URLSessionDelegate {
    func urlSession(
        _ session: URLSession,
        didReceive challenge: URLAuthenticationChallenge,
        completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void
    ) {
        if let trust = challenge.protectionSpace.serverTrust {
            completionHandler(.useCredential, URLCredential(trust: trust))
        } else {
            completionHandler(.performDefaultHandling, nil)
        }
    }
}
