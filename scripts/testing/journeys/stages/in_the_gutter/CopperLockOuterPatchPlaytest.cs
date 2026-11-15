using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Stages;
using BlastCircuit.Rendering;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task CopperLockOuterPatch()
    {
        Check(!_headless,"outer-water reflection regression requires actual rendered pixels");
        var quality=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        var size=GetWindow().Size;
        try
        {
            await ResizeWindow(new(1280,800));await SelectGraphics(GraphicsPreset.High);
            await SelectLocalLighting(StageTime.Night);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.InTheGutter,Bots:0,SuddenDeath:false),4,seed:64);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"outer reflection regression enters actual Copper Lock",180);
            var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
            CheckCopperWaterInputs(terrain);
            var material=(ShaderMaterial)terrain.GetNode<MeshInstance3D>("CopperLockOuterWater").MaterialOverride;
            Check(material.Shader==ArtMaterials.GutterOceanShaderFor(GraphicsPreset.High),"outer water uses the actual Copper Lock High variant");
            Check(material.Shader.Code.Contains("#define SCENE_REFLECTIONS",StringComparison.Ordinal),"outer High retains actual opaque-scene reflections");
            string legacy=ArtMaterials.WaterShaderFor(GraphicsPreset.High).Code;
            Check(legacy.Contains("float jitter=fract(",StringComparison.Ordinal),"other stages retain their established shared jitter");
            var camera=GetViewport().GetCamera3D()!;
            var points=new[]{new Vector3(-.2f,-.96f,terrain.Depth+.5f),new Vector3(2.8f,-.96f,terrain.Depth+.5f),
                new Vector3(-.2f,-.96f,terrain.Depth+1.65f),new Vector3(2.8f,-.96f,terrain.Depth+1.65f)}
                .Select(p=>camera.UnprojectPosition(terrain.ToGlobal(p))).ToArray();
            var logicalSize=camera.GetViewport().GetVisibleRect().Size;
            string original=material.Shader.Code;
            string Clocked(string code)=>code.Replace("TIME","outer_test_clock",StringComparison.Ordinal)
                .Replace("uniform float flood_amount=0.0;","uniform float outer_test_clock=4.0;\nuniform float flood_amount=0.0;",StringComparison.Ordinal);
            Rect2I patch=default;
            try
            {
                async Task<(string Path,int Noise)> Sample(string control,string code)
                {
                    material.Shader.Code=code;await Frames(6);await Capture("copper-outer-patch-"+control,0);
                    string path=_screenshots[^1];using var image=Image.LoadFromFile(path);
                    // Convert logical camera coordinates to output pixels;
                    // the full captures and the selected world patch stay intact.
                    var pixelScale=new Vector2(image.GetWidth(),image.GetHeight())/logicalSize;
                    var pixels=points.Select(p=>p*pixelScale).ToArray();
                    var min=pixels.Aggregate(new Vector2(float.MaxValue,float.MaxValue),(a,b)=>a.Min(b));
                    var max=pixels.Aggregate(new Vector2(float.MinValue,float.MinValue),(a,b)=>a.Max(b));
                    patch=new((int)min.X,(int)min.Y,(int)Mathf.Ceil(max.X-min.X),(int)Mathf.Ceil(max.Y-min.Y));
                    Check(patch.Position.X>0&&patch.Position.Y>0&&patch.End.X<image.GetWidth()&&patch.End.Y<image.GetHeight(),
                        "world-selected outer patch lies inside the unmodified full capture");
                    int noise=WaterSpeckles(image,patch);
                    Event("copper-outer-water-patch",new{control,patch=patch.ToString(),logicalSize=logicalSize.ToString(),
                        imageSize=image.GetSize().ToString(),pixelScale=pixelScale.ToString(),speckles=noise});
                    return (path,noise);
                }
                var production=await Sample("production",Clocked(original));
                var previous=await Sample("legacy-jitter",Clocked(legacy));
                var off=await Sample("reflection-off",Clocked(original).Replace("#define SCENE_REFLECTIONS","",StringComparison.Ordinal));
                using var onImage=Image.LoadFromFile(production.Path);using var offImage=Image.LoadFromFile(off.Path);
                int mirrored=0;
                for(int y=patch.Position.Y;y<patch.End.Y;y++)for(int x=patch.Position.X;x<patch.End.X;x++)
                {
                    var delta=onImage.GetPixel(x,y)-offImage.GetPixel(x,y);
                    if(Math.Max(Math.Abs(delta.R),Math.Max(Math.Abs(delta.G),Math.Abs(delta.B)))>.04f)mirrored++;
                }
                Event("copper-outer-reflection-comparison",new{production=production.Noise,legacy=previous.Noise,
                    reflectionOff=off.Noise,mirrored,patchPixels=patch.Size.X*patch.Size.Y});
                // Compare against the same-device legacy result, not an absolute
                // GPU-specific count. Quiet legacy output need not invent a defect.
                Check(production.Noise<=previous.Noise*.7f+5,"coherent outer reflection reduces or preserves the same-device legacy hatch");
                Check(mirrored>=Math.Max(4,patch.Size.X*patch.Size.Y*.025f),"outer High retains meaningful actual tower reflection contrast");
            }
            finally{material.Shader.Code=original;}
        }
        finally
        {
            _root.Navigate("Modes");await Frames(3);await SelectGraphics(quality);
            await SelectLocalLighting(lighting);await ResizeWindow(size);
        }
    }
}
