using Godot;
using Bomber.Game.Simulation;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena;

public partial class TempleStageView
{
    /// <summary>Original procedural castle art. Thin bridge slabs leave the lower floor visible.</summary>
    private void BuildTopRules()
    {
        var stone = _art.Masonry();
        stone.SetShaderParameter("base_color", new Color("828b96"));
        stone.SetShaderParameter("mortar_color", new Color("343b48"));
        var cap = _art.Stone();
        cap.SetShaderParameter("base_color", new Color("bdc4c5"));
        cap.SetShaderParameter("mortar_color", new Color("616b78"));
        _castleWallMaterial=stone;
        foreach(var material in new[]{stone,cap})material.SetShaderParameter("quality_detail",GraphicsQuality.Current==GraphicsPreset.Low?0:1);
        _castleStone.Add(stone); _castleStone.Add(cap);
        var slate = _art.Color(new("414651"));
        var inset = _art.Color(new("646775"));
        var gold = _art.Color(new("b68c43"), .45f);
        var ivory = _art.Color(new("e6e4cd"));
        var carpet = _art.Color(new("186642"));
        var carpetLight = _art.Color(new("3c9a62"));
        var crimson = _art.Color(new("7c1d2a"));
        var ember = _art.Color(new("e69731"), .65f, true);
        float deck = 2 * HeightScale;
        Box(this, new(Width / 2f, -.49f, Depth / 2f), new(Width, .9f, Depth), stone).Name = "CastleFoundation";
        // The red court is solid flooring. Its restrained checker pattern is drawn in world space.
        Box(this, new(Width / 2f, -.055f, Depth / 2f), new(Width - 2, .11f, Depth - 2),
            new ShaderMaterial { Shader = _castleFloorShader ??= new Shader { Code = CastleFloorShader } }).Name = "CastleCourt";
        for (int z = 0; z < Depth; z++) for (int x = 0; x < Width; x++)
        {
            var cell = _cells[z * Width + x];
            int kind = cell.GetProperty("k").GetInt32();
            float tier = cell.GetProperty("t").GetSingle();
            if (kind == (int)CellKind.Pillar)
            {
                // Rear and side battlements frame the castle; the near wall stays low for visibility.
                float top = cell.GetProperty("oh").GetSingle() * HeightScale - .11f;
                Box(this, new(x + .5f, (top - .08f) / 2, z + .5f), new(1, top + .08f, 1), stone);
                Box(this, new(x + .5f, top + .04f, z + .5f), new(1.06f, .14f, 1.06f), cap, .035f);
                continue;
            }
            if (kind == (int)CellKind.Ramp)
            {
                float from = cell.GetProperty("rf").GetSingle() * HeightScale;
                float to = cell.GetProperty("rt").GetSingle() * HeightScale;
                int direction = cell.GetProperty("rz").GetInt32();
                for (int step = 0; step < 6; step++)
                {
                    float top = Mathf.Lerp(from, to, (step + 1) / 6f);
                    float zz = z + .5f + direction * ((step + .5f) / 6 - .5f);
                    Box(this, new(x + .5f, top / 2, zz), new(.998f, top, 1f / 6), stone);
                    Box(this, new(x + .5f, top - .012f, zz), new(.76f, .035f, 1f / 6), step % 2 == 0 ? carpet : carpetLight);
                    Box(this, new(x + .5f, top + .009f, zz + direction * .064f), new(.82f, .025f, .026f), ivory);
                    foreach (float side in new[] { .06f, .94f })
                        Box(this, new(x + side, top + .025f, zz), new(.105f, .08f, 1f / 6), cap);
                }
                continue;
            }
            if (tier != 2) continue;
            bool bridge = _underpasses.Contains(z * Width + x);
            float thickness = bridge ? StageLayout.DeckThickness * HeightScale : deck;
            Box(this, new(x + .5f, deck - (thickness + .08f) / 2, z + .5f), new(.999f, thickness - .08f, .999f), bridge ? slate : stone);
            bool junction = z == 8 && x is 5 or 9 or 13;
            Box(this, new(x + .5f, deck - .045f, z + .5f), new(.97f, .07f, .97f), bridge ? gold : cap, .018f);
            Box(this, new(x + .5f, deck - .015f, z + .5f), new(.87f, .03f, .87f), junction ? ivory : bridge ? inset : cap, .02f);
            if (bridge && !junction)
            {
                // Subtle paving joints, brass perimeter and visible underside preserve the narrow lanes.
                Box(this, new(x + .5f, deck + .004f, z + .5f), new(.012f, .008f, .82f), slate);
                Box(this, new(x + .5f, deck + .004f, z + .5f), new(.82f, .008f, .012f), slate);
            }
        }
        // Amber edging echoes the reference's warm strips without turning the red court into lava.
        foreach (float x in new[] { 2.015f, Width - 2.015f })
            Box(this, new(x, .018f, Depth / 2f), new(.055f, .03f, Depth - 8), ember);
        foreach (float z in new[] { 2.015f, Depth - 2.015f })
            Box(this, new(Width / 2f, .018f, z), new(Width - 6, .03f, .055f), ember);
        // Recessed columns live on the perimeter's solid riser, clear of the lower walkable floor.
        foreach (int x in new[] { 3, 7, 11, 15 })
        {
            Cylinder(this, new(x + .5f, 1.12f, 1.94f), .13f, 1.65f, cap);
            Box(this, new(x + .5f, .24f, 1.94f), new(.38f, .16f, .25f), cap);
            Box(this, new(x + .5f, 2.02f, 1.94f), new(.38f, .16f, .25f), cap);
        }
        foreach (float x in new[] { 3.5f, Width - 3.5f })
        {
            Box(this, new(x, deck + .7f, .99f), new(1.15f, 1.35f, .07f), gold, .015f);
            Box(this, new(x, deck + .72f, 1.035f), new(.98f, 1.16f, .025f), crimson);
            var crest = Box(this, new(x, deck + .76f, 1.06f), new(.32f, .32f, .018f), gold, .02f);
            crest.Rotation = new(0, 0, Mathf.Pi / 4);
        }
    }

    private Node3D CreateCastleCover(int x, int z)
    {
        var root = new Node3D { Position = new(x + .5f, Surface(x + .5f, z + .5f), z + .5f) };
        if(_castleCoverMaterial is null)
        {
            _castleCoverMaterial=_art.Masonry();
            _castleCoverMaterial.SetShaderParameter("base_color",new Color("a5abb3"));
            _castleCoverMaterial.SetShaderParameter("mortar_color",new Color("505966"));
            _castleCoverMaterial.SetShaderParameter("quality_detail",GraphicsQuality.Current==GraphicsPreset.Low?0:1);
            _castleStone.Add(_castleCoverMaterial);
        }
        var stone=_castleCoverMaterial;
        Box(root, new(0, .4f, 0), new(.81f, .8f, .81f), stone, .035f);
        Box(root, new(0, .79f, 0), new(.84f, .06f, .84f), _art.Color(new("c9ccca")), .018f);
        return root;
    }

    private const string CastleFloorShader = """
shader_type spatial;
varying vec3 world;
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;}
void fragment(){
    vec2 p=world.xz*3.0;
    float pattern=mod(floor(p.x)+floor(p.y),2.0);
    vec2 edge=min(fract(p),1.0-fract(p));
    float seam=smoothstep(.015,.045,min(edge.x,edge.y));
    vec3 red=mix(vec3(.31,.035,.055),vec3(.43,.055,.075),pattern);
    ALBEDO=mix(vec3(.13,.022,.035),red,seam);
    ROUGHNESS=.86; SPECULAR=.18;
}
""";
}
