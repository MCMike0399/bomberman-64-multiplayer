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
        return _gutterHighWaterShader=new Shader{Code=source.Replace(jitter,"float jitter=0.0;",StringComparison.Ordinal)};
    }
}
