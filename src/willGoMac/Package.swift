// swift-tools-version: 5.9

import PackageDescription

let package = Package(
    name: "willGoMac",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(name: "willGo", targets: ["willGo"])
    ],
    targets: [
        .executableTarget(
            name: "willGo",
            path: "Sources/willGo"
        )
    ]
)
