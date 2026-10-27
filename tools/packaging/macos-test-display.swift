import CoreGraphics
import Foundation

guard ProcessInfo.processInfo.environment["GITHUB_ACTIONS"] == "true" else {
    fatalError("Display configuration belongs only to the disposable hosted test desktop")
}
let display = CGMainDisplayID()
let options = [kCGDisplayShowDuplicateLowResolutionModes as String: true] as CFDictionary
guard let modes = CGDisplayCopyAllDisplayModes(display, options) as? [CGDisplayMode] else {
    fatalError("No display modes available")
}
let candidates = modes.filter { $0.width >= 1920 && $0.height >= 1200 }
    .sorted { $0.width * $0.height < $1.width * $1.height }
guard let mode = candidates.first else {
    print("Available modes: " + modes.map { "\($0.width)x\($0.height)" }.joined(separator: ", "))
    fatalError("Complete graphical regression needs a 1920x1200 test desktop")
}
guard CGDisplaySetDisplayMode(display, mode, nil) == .success else {
    fatalError("Could not set the owned test display mode")
}
print("Owned test desktop: \(mode.width)x\(mode.height) points, \(mode.pixelWidth)x\(mode.pixelHeight) pixels")
