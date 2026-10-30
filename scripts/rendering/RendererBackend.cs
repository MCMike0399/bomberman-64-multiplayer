using Godot;
using Bomber.Client.Startup;
namespace BlastCircuit.Rendering;

/// <summary>Observed API selection, independent of quality presets and authority.</summary>
public static class RendererBackend
{
    public const string VulkanFallbackReason = "vulkan-initialization-unavailable";
    public readonly record struct Selection(string RequestedDriver, string Driver, string? FallbackReason);

    public static string EngineRequestedDriver()
    {
        // Godot consumes engine switches before publishing GetCmdlineArgs().
        // Keep the selected engine API separate from the user's original intent:
        // a recovered launch selects D3D12 while its original intent remains Vulkan.
        string? selected=WindowsLaunchGuard.Argument(OS.GetCmdlineUserArgs(),"--launcher-engine-driver");
        if(OS.GetName()=="Windows"&&selected is "vulkan" or "d3d12")return selected;
        string platform = OS.GetName() switch { "Windows" => "windows", "macOS" => "macos", _ => "linuxbsd" };
        string key = "rendering/rendering_device/driver." + platform;
        string requested = ProjectSettings.HasSetting(key) ? ProjectSettings.GetSetting(key).AsString() :
            ProjectSettings.GetSetting("rendering/rendering_device/driver", "vulkan").AsString();
        var arguments = OS.GetCmdlineArgs();
        for (int i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == "--rendering-driver" && i + 1 < arguments.Length) requested = arguments[++i];
            else if (arguments[i].StartsWith("--rendering-driver=", StringComparison.Ordinal)) requested = arguments[i][19..];
        }
        return requested;
    }
    public static string State=>RenderingServer.GetCurrentRenderingMethod()=="gl_compatibility"?"compatibility-reduced":"rendering-device";
    public static Selection Observe()
    {
        string requested=EngineRequestedDriver();
        string actual = RenderingServer.GetCurrentRenderingDriverName();
        string? reason = OS.GetName() == "Windows" && requested == "vulkan" && actual == "d3d12" ? VulkanFallbackReason : null;
        var user=OS.GetCmdlineUserArgs();
        string? launchReason=WindowsLaunchGuard.Argument(user,"--launcher-fallback-reason");
        string? intended=WindowsLaunchGuard.Argument(user,"--launcher-requested-driver");
        if(OS.GetName()=="Windows"&&actual=="d3d12")
        {
            if(intended=="vulkan"&&launchReason is VulkanFallbackReason or WindowsLaunchGuard.PreviousVulkanFailure){requested=intended;reason=launchReason;}
            else if(requested=="d3d12")reason="directx12-explicit-choice";
        }
        return new(requested, actual, reason);
    }
}
