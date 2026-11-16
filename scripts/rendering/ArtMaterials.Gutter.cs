using Godot;

namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    private static Shader? _gutterMediumWaterShader;
    /// <summary>The shallow lock has upright posts and cover, with no suspended
    /// decks. Coherent reflection samples keep these short contacts solid;
    /// other arenas retain their jittered march against reflected deck banding.</summary>
    public static Shader GutterWaterShaderFor(GraphicsPreset preset)
    {
        if(preset==GraphicsPreset.Medium)
        {
            // The lock is optically shallow: its quiet mosaic otherwise reads as
            // dry paving without the High reflection pass. Keep this local to
            // the court; Medium still has no scene-reflection march.
            string medium=WaterShaderFor(preset).Code;
            medium=medium.Replace("#define CLEAR_WATER","#define CLEAR_WATER\n#define GUTTER_SHALLOWS",StringComparison.Ordinal)
                .Replace("#ifdef WATER_SPARKLE\n// Drifting Voronoi borders",
                    "#if defined(WATER_SPARKLE) || defined(GUTTER_SHALLOWS)\n// Drifting Voronoi borders",StringComparison.Ordinal)
                .Replace("seen=textureLod(screen_texture,refract_uv,0.0).rgb;\n#ifdef WATER_SPARKLE",
                    "seen=textureLod(screen_texture,refract_uv,0.0).rgb*vec3(.78,.96,.87);\n#if defined(WATER_SPARKLE) || defined(GUTTER_SHALLOWS)",StringComparison.Ordinal)
                .Replace("caustic_detail*1.4+light_tint.rgb*web*reach*caustic_detail*.05;",
                    "caustic_detail*.16+light_tint.rgb*web*reach*caustic_detail*.007;",StringComparison.Ordinal);
            return _gutterMediumWaterShader??=new Shader{Code=medium};
        }
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
    /// <summary>Outer Copper Lock water also benefits from coherent sampling
    /// on its compact cream towers. Keep separate palettes/centers and retain
    /// the normal march budget; all other stages keep their shared jitter.</summary>
    public static Shader GutterOceanShaderFor(GraphicsPreset preset)
    {
        if(preset!=GraphicsPreset.High)return WaterShaderFor(preset);
        if(_gutterOceanHighWaterShader!=null)return _gutterOceanHighWaterShader;
        const string jitter="float jitter=fract(52.9829189*fract(dot(FRAGCOORD.xy,vec2(.06711056,.00583715))));";
        string source=WaterShaderFor(GraphicsPreset.High).Code;
        if(!source.Contains(jitter,StringComparison.Ordinal))
            throw new InvalidOperationException("Review Copper Lock's outer-water specialization after shared water changes.");
        return _gutterOceanHighWaterShader=new Shader{Code=source.Replace(jitter,"float jitter=0.0;",StringComparison.Ordinal)};
    }

}
