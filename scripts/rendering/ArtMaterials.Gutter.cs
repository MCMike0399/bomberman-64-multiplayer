using Godot;

namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    /// <summary>The shallow lock has upright posts and cover, with no suspended
    /// decks. Coherent reflection samples keep these short contacts solid;
    /// other arenas retain their jittered march against reflected deck banding.</summary>
    public static Shader GutterWaterShaderFor(GraphicsPreset preset)
    {
        if(preset!=GraphicsPreset.High)return WaterShaderFor(preset);
        if(_gutterHighWaterShader!=null)return _gutterHighWaterShader;
        const string jitter="float jitter=fract(52.9829189*fract(dot(FRAGCOORD.xy,vec2(.06711056,.00583715))));";
        string source=WaterShaderFor(GraphicsPreset.High).Code;
        if(!source.Contains(jitter,StringComparison.Ordinal))
            throw new InvalidOperationException("Review the Gutter reflection specialization after shared water changes.");
        const string caustics="seen+=seen*web*reach*caustic_detail*1.4+light_tint.rgb*web*reach*caustic_detail*.05;";
        if(!source.Contains(caustics,StringComparison.Ordinal))
            throw new InvalidOperationException("Review Copper Lock's quiet shallow-water caustics after shared water changes.");
        source=source.Replace(jitter,"float jitter=0.0;",StringComparison.Ordinal)
            .Replace(caustics,"seen+=seen*web*reach*caustic_detail*.22+light_tint.rgb*web*reach*caustic_detail*.012;",StringComparison.Ordinal);
        return _gutterHighWaterShader=new Shader{Code=source};
    }
}
