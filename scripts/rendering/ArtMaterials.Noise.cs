namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    internal const string LegacyNoiseHash = "float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}";

    // User-approved presentation hash. Lattice callers sometimes add fractional seed
    // offsets; quantize those keys, not the noise interpolation or world geometry.
    internal const string IntegerNoiseHash = """
uint presentation_mix(uint value) {
    uint state = value * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}
float hash(vec2 p) {
    ivec2 cell = ivec2(floor(p * 256.0));
    uvec2 bits = uvec2(cell);
    uint value = presentation_mix(bits.x ^ presentation_mix(bits.y + 2654435769u));
    return float(value >> 8u) * (1.0 / 16777216.0);
}
""";

    internal static string LegacyNoiseForExperiment(string source)=>source.Replace(IntegerNoiseHash,LegacyNoiseHash,StringComparison.Ordinal);

    internal static string IntegerNoiseForExperiment(string source)
    {
        int first = source.IndexOf(LegacyNoiseHash, StringComparison.Ordinal);
        if (first < 0 || source.LastIndexOf(LegacyNoiseHash, StringComparison.Ordinal) != first)
            throw new ArgumentException("Expected one recognized presentation noise hash.", nameof(source));
        return source.Replace(LegacyNoiseHash, IntegerNoiseHash, StringComparison.Ordinal);
    }
}
