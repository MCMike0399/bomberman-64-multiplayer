using Godot;
namespace BlastCircuit.Rendering;

public enum StageTime { Morning, Midday, Afternoon, Night }

/// <summary>Art direction shared by the arena atmosphere and its water. Never changes match rules.</summary>
public sealed record StageLighting(
    string Label, Vector3 KeyRotation, Color KeyColor, float KeyEnergy,
    Color Ambient, float AmbientEnergy, Color Fill, float FillEnergy,
    Color Zenith, Color Horizon, Color Ground, float SkyEnergy,
    Color DeepWater, Color ShallowWater, Color Foam, float Wind, float WaterRoughness)
{
    public static StageTime FromWire(string? value) => value switch
    {
        "morning" => StageTime.Morning, "afternoon" => StageTime.Afternoon,
        "night" => StageTime.Night, _ => StageTime.Midday
    };
    public static string ToWire(StageTime time) => time.ToString().ToLowerInvariant();
    private static readonly StageLighting Morning=new("Morning", new(-32,-112,0), new("ffe1b0"), .88f,
            new("b5d4de"), .34f, new("b1cfe4"), .15f,
            new("579bb8"), new("eed7bd"), new("607d86"), .8f,
            new("225d70"), new("62b5af"), new("e1e8d4"), .7f, .28f);
    private static readonly StageLighting Afternoon=new("Afternoon", new(-27,-62,0), new("ffbf80"), 1.05f,
            new("b0a8d2"), .32f, new("aaa9e5"), .18f,
            new("746994"), new("efb38d"), new("634e65"), .85f,
            new("244b61"), new("668e91"), new("f7d7ab"), .8f, .29f);
    private static readonly StageLighting Night=new("Night", new(-43,25,0), new("a4c8ff"), .68f,
            new("829bc5"), .32f, new("9b88c5"), .16f,
            new("101d3d"), new("53658d"), new("18293f"), .65f,
            new("112e48"), new("36758a"), new("a5d8e3"), .55f, .25f);
    private static readonly StageLighting Midday=new("Midday", new(-64,-34,0), new("fff5e2"), 1.0f,
            new("aec9df"), .30f, new("a0c7e1"), .13f,
            new("418caf"), new("b2d5d5"), new("50737c"), .8f,
            new("155669"), new("46aca9"), new("c5e3cf"), 1f, .31f);
    public static StageLighting For(StageTime time) => time switch
    {
        StageTime.Morning=>Morning,StageTime.Afternoon=>Afternoon,StageTime.Night=>Night,_=>Midday
    };

    public void ApplyWater(ShaderMaterial material)
    {
        material.SetShaderParameter("deep_color", DeepWater);
        material.SetShaderParameter("surface_color", ShallowWater);
        material.SetShaderParameter("foam_color", Foam);
        material.SetShaderParameter("wind_strength", Wind);
        material.SetShaderParameter("surface_roughness", WaterRoughness);
    }
}
