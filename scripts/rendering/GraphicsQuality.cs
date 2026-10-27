using Godot;
namespace BlastCircuit.Rendering;

public enum GraphicsPreset { Low, Medium, High }

/// <summary>Device-local rendering policy. Never changes simulation or shared stage lighting.</summary>
public static class GraphicsQuality
{
    public static GraphicsPreset Current {get;private set;}=GraphicsPreset.Medium;
    public static int Revision {get;private set;}
    public static Viewport.Msaa Antialiasing=>Current==GraphicsPreset.Low?Viewport.Msaa.Msaa2X:Viewport.Msaa.Msaa4X;
    /// <summary>Seconds between animated menu-world renders: Low keeps 30 Hz, other presets follow the display.</summary>
    public static double MenuFrameInterval=>Current==GraphicsPreset.Low?1d/30:0;
    public static GraphicsPreset FromWire(string? value)=>value switch
    { "low"=>GraphicsPreset.Low,"high"=>GraphicsPreset.High,_=>GraphicsPreset.Medium };
    public static string ToWire(GraphicsPreset value)=>value switch
    { GraphicsPreset.Low=>"low",GraphicsPreset.High=>"high",_=>"medium" };
    public static string Description(GraphicsPreset value)=>value switch
    {
        GraphicsPreset.Low=>"Lighter effects and softer detail for lower power use.",
        GraphicsPreset.High=>"Crisp edges, richer contact shading and finer shadows.",
        _=>"Crisp edges and balanced effects for everyday play."
    };

    public static void Apply(GraphicsPreset preset,Viewport viewport)
    {
        Current=Enum.IsDefined(preset)?preset:GraphicsPreset.Medium;
        Revision++;
        ApplyViewport(viewport);
        viewport.Scaling3DScale=Current==GraphicsPreset.Low?.85f:1;
        // Spatial upscaling is portable across Metal/Vulkan; UI always stays native.
        viewport.Scaling3DMode=Current==GraphicsPreset.Low?Viewport.Scaling3DModeEnum.Fsr:Viewport.Scaling3DModeEnum.Bilinear;
        RenderingServer.DirectionalShadowAtlasSetSize(Current switch {GraphicsPreset.Low=>1024,GraphicsPreset.High=>4096,_=>2048},true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(Current switch
        {GraphicsPreset.Low=>RenderingServer.ShadowQuality.SoftVeryLow,GraphicsPreset.High=>RenderingServer.ShadowQuality.SoftMedium,_=>RenderingServer.ShadowQuality.SoftLow});
        // Half-resolution SSAO on every preset: at native panel density the bilateral upsample
        // measured within 10/255 of full resolution while saving about 1.8 ms per High frame.
        RenderingServer.EnvironmentSetSsaoQuality(Current==GraphicsPreset.High?RenderingServer.EnvironmentSsaoQuality.High:RenderingServer.EnvironmentSsaoQuality.Medium,
            true,.5f,2,50,300);
        RenderingServer.ScreenSpaceRoughnessLimiterSetActive(true,.25f,.18f);
        // Indirect bounce exists only on High; it is half-resolution and fades with distance.
        RenderingServer.EnvironmentSetSsilQuality(RenderingServer.EnvironmentSsilQuality.Medium,true,.5f,2,50,300);
        RenderingServer.CameraAttributesSetDofBlurQuality(Current==GraphicsPreset.High?RenderingServer.DofBlurQuality.Medium:RenderingServer.DofBlurQuality.Low,true);
        RenderingServer.CameraAttributesSetDofBlurBokehShape(RenderingServer.DofBokehShape.Circle);
    }

    public static void ApplyViewport(Viewport viewport)
    {
        viewport.Msaa3D=Antialiasing;
        viewport.ScreenSpaceAA=Viewport.ScreenSpaceAAEnum.Disabled;
        viewport.UseTaa=false;
        viewport.UseDebanding=true;
    }
    public static void ApplyArena(Godot.Environment environment,CameraAttributesPractical? camera=null)
    {
        environment.SsaoEnabled=Current!=GraphicsPreset.Low;
        environment.GlowEnabled=Current!=GraphicsPreset.Low;
        environment.SsilEnabled=Current==GraphicsPreset.High;
        if(camera==null)return;
        // Diorama depth of field only touches scenery outside the fitted arena.
        camera.DofBlurFarEnabled=Current!=GraphicsPreset.Low;
        camera.DofBlurNearEnabled=Current!=GraphicsPreset.Low;
    }
}
