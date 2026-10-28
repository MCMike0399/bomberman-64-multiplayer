using Godot;
using Bomber.Client.Sessions;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Stages;
using BlastCircuit.Rendering;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task WaterSurface()
    {
        Check(!_headless,"water surface regression requires real rendered pixels");
        var quality=_root.Preferences.Graphics;
        var lighting=_root.Preferences.StageLighting;
        var size=GetWindow().Size;
        var defects=new List<string>();
        try
        {
            await WindowMode(Window.ModeEnum.Windowed,"water inspection uses a windowed game");
            // These pixel probes belong to this exact ordinary battle-camera composition.
            await ResizeWindow(new(1600,1000));
            _root.Navigate("Modes");await Frames(3);
            _root.StartLocal(new SessionSettings(Stage:"up-and-down",Bots:0),1,practice:true);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"water inspection starts Up & Down",150);
            foreach(var preset in Enum.GetValues<GraphicsPreset>())
            {
                await SelectGraphics(preset);
                var water=AuditDescendants(LightingArena).OfType<TempleStageView>().Single().GetChildren()
                    .OfType<MeshInstance3D>().Single(m=>m.Mesh is PlaneMesh);
                var material=(ShaderMaterial)water.MaterialOverride;
                var shader=material.Shader;
                Check(shader==ArtMaterials.WaterShaderFor(preset),"probe observes the active water shader: "+preset);
                Check(shader.Code.Contains("#define SCENE_REFLECTIONS")== (preset==GraphicsPreset.High),"scene reflections remain High-only: "+preset);
                string original=shader.Code;
                // Pin only the water's animation clock, keeping real gameplay, lighting,
                // geometry and scene-depth/color inputs. Reuse the resource to preserve draw order.
                string clocked=original.Replace("TIME","water_test_clock",StringComparison.Ordinal)
                    .Replace("uniform float flood_amount=0.0;","uniform float water_test_clock=4.0;\nuniform float flood_amount=0.0;",StringComparison.Ordinal);
                try
                {
                    shader.Code=clocked;
                    foreach(var time in Enum.GetValues<StageTime>())
                    {
                        await SelectLocalLighting(time);
                        foreach(float clock in preset==GraphicsPreset.High?new[]{1f,4f,9f}:new[]{4f})
                        {
                            material.SetShaderParameter("water_test_clock",clock);await Frames(3);
                            string label=$"water-up-down-{preset}-{time}-{clock:0}";
                            await Capture(label,0);
                            string reflected=_screenshots[^1];
                            using(var image=Image.LoadFromFile(reflected))
                            {
                                Check(image.GetWidth()==1600&&image.GetHeight()==1000,"water probes use the expected output composition");
                                // Water between the upper piers and crown, away from silhouettes,
                                // foam and real reflected columns. Count isolated high-contrast points.
                                foreach(int left in new[]{655,890})
                                {
                                    int speckles=WaterSpeckles(image,new Rect2I(left,410,55,45));
                                    Event("water-speckles",new{preset=preset.ToString(),lighting=time.ToString(),clock,left,speckles});
                                    // Low's spatial upscaler can ring at subpixel highlights; its
                                    // captures/counts are diagnostic. This defect belongs to High's mirror.
                                    if(preset==GraphicsPreset.High&&speckles>5)defects.Add($"{label} patch {left}: {speckles} isolated bright pixels");
                                }
                            }
                            if(preset==GraphicsPreset.High&&time==StageTime.Night&&clock==4)
                            {
                                // A clean patch must not be achieved by removing High's mirror.
                                // Compare stationary columns with the reflection branch alone disabled.
                                // The mirror uses resolved MSAA depth. The observed llvmpipe
                                // configuration loses the mirror and in-focus sharpness with MSAA;
                                // the raw-sample/resolve/reconstruction fault is not localized yet.
                                // This software-only MSAA-off contrast is diagnostic effect evidence,
                                // never qualification for the unchanged production High+2xMSAA image.
                                var viewport=GetViewport();var msaa=viewport.Msaa3D;bool software=SoftwareRenderer;
                                if(software)
                                {
                                    viewport.Msaa3D=Viewport.Msaa.Disabled;await Frames(6);
                                    await Capture("water-reflection-on-without-msaa",0);reflected=_screenshots[^1];
                                    Event("water-reflection-software-msaa",new{adapter=RenderingServer.GetVideoAdapterName(),msaa=msaa.ToString()});
                                }
                                shader.Code=clocked.Replace("#define SCENE_REFLECTIONS","",StringComparison.Ordinal);
                                try
                                {
                                    await Frames(6);await Capture("water-reflection-control",0);
                                    using var on=Image.LoadFromFile(reflected);
                                    using var off=Image.LoadFromFile(_screenshots[^1]);
                                    int mirrored=0;
                                    for(int y=650;y<915;y++)for(int x=550;x<1050;x++)
                                    {
                                        var delta=on.GetPixel(x,y)-off.GetPixel(x,y);
                                        if(Math.Max(Math.Abs(delta.R),Math.Max(Math.Abs(delta.G),Math.Abs(delta.B)))>.04f)mirrored++;
                                    }
                                    Event("water-scene-reflection",new{mirrored});
                                    if(mirrored<250)defects.Add($"High lost its visible scene reflection: {mirrored} pixels");
                                }
                                finally
                                {
                                    shader.Code=clocked;
                                    if(software)viewport.Msaa3D=msaa;
                                    await Frames(3);
                                }
                            }
                        }
                    }
                }
                finally {shader.Code=original;await Frames(3);}
            }
            Check(defects.Count==0,"open water stays free of white reflection particles while High retains scene reflections"+
                (defects.Count>0?": "+string.Join("; ",defects):""));
        }
        finally
        {
            Input(0,default);_root.Navigate("Modes");await Frames(3);
            await SelectGraphics(quality);await SelectLocalLighting(lighting);await ResizeWindow(size);
        }
    }

    private static int WaterSpeckles(Image image,Rect2I patch)
    {
        float Luma(int x,int y){var c=image.GetPixel(x,y);return c.R*.2126f+c.G*.7152f+c.B*.0722f;}
        int count=0;
        var neighbors=new float[4];
        for(int y=patch.Position.Y;y<patch.End.Y;y++)for(int x=patch.Position.X;x<patch.End.X;x++)
        {
            neighbors[0]=Luma(x-1,y);neighbors[1]=Luma(x+1,y);
            neighbors[2]=Luma(x,y-1);neighbors[3]=Luma(x,y+1);Array.Sort(neighbors);
            if(Luma(x,y)-(neighbors[1]+neighbors[2])*.5f>.06f)count++;
        }
        return count;
    }
}
