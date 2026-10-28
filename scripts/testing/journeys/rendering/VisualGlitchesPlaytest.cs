using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Stages;
using BlastCircuit.Rendering;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task VisualGlitches()
    {
        var quality=_root.Preferences.Graphics;
        var lighting=_root.Preferences.StageLighting;
        var defects=new List<string>();
        try
        {
            if(!_headless)
            {
                await WindowMode(Window.ModeEnum.Windowed,"visual comparisons use a windowed game");
                await ResizeWindow(new(1280,800));
            }
            await SelectLocalLighting(StageTime.Midday);
            foreach(var preset in Enum.GetValues<GraphicsPreset>())
            {
                _root.Navigate("Modes");await Frames(3);await SelectGraphics(preset);
                _root.StartLocal(new SessionSettings(Stage:"up-and-down",Bots:0),1,practice:true);
                await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"rear-ring practice starts: "+preset,150);
                await MoveTo(11.5f,2.5f,300);
                Check(Math.Abs(Player.GetProperty("y").GetSingle()-4)<.01f,"ordinary movement reaches the raised rear ring");
                await Capture("ring-before-"+preset);
                Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);
                await WaitState(s=>s.GetProperty("hazards").GetArrayLength()>0,"rear-ring bomb really detonates: "+preset,130);
                var hazard=State.GetProperty("hazards")[0];
                var center=new Vector3(hazard.GetProperty("x").GetSingle(),hazard.GetProperty("y").GetSingle()*TempleStageView.HeightScale+.3f,hazard.GetProperty("z").GetSingle());
                // Let the foreground star fade so it cannot hide a clipped fireball.
                await Frames(10);await Capture("ring-blast-"+preset,0);
                var view=GetViewport().GetCamera3D()!;
                var upper=view.UnprojectPosition(center+Vector3.Up*.8f);
                var lower=view.UnprojectPosition(center+Vector3.Up*.4f);
                if(!_headless)
                using(var image=Image.LoadFromFile(_screenshots[^1]))
                {
                    // UnprojectPosition uses the logical canvas (1440x900 by default),
                    // while captures contain output pixels. Normalize before sampling.
                    var outputScale=new Vector2(image.GetWidth(),image.GetHeight())/GetViewport().GetVisibleRect().Size;
                    upper*=outputScale;lower*=outputScale;
                    int fire=0;
                    for(int y=(int)upper.Y;y<=(int)lower.Y;y++)for(int x=(int)upper.X-3;x<=(int)upper.X+3;x++)
                    {
                        var color=image.GetPixel(x,y);
                        if(color.R>.65f&&color.G>.22f&&color.B<.75f&&color.R>color.B*1.3f)fire++;
                    }
                    Event("rear-ring-fire-probe",new{preset=preset.ToString(),upper=upper.ToString(),lower=lower.ToString(),fire});
                    if(fire<4)defects.Add("rear-ring fireball is hidden above the walkway on "+preset);
                }
                await Frames(3);await Capture("ring-blast-late-"+preset,0);
                await Frames(30);
                if(preset==GraphicsPreset.Medium&&!_headless)
                {
                    // Repeat the accepted placement for an explicitly labelled close view.
                    // Capturing a fresh blast avoids stretching an expired effect for evidence.
                    await MoveTo(11.5f,2.5f,300);
                    var inspection=GetViewport().GetCamera3D()!;var originalPose=inspection.Transform;
                    inspection.Position=new(11.5f,13,15);inspection.LookAt(new(11.5f,5.4f,2.5f));
                    Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);
                    await WaitState(s=>s.GetProperty("hazards").GetArrayLength()>0,"inspection observes a fresh real rear-ring detonation",130);
                    await Frames(9);await Capture("ring-blast-inspection",0);
                    inspection.Transform=originalPose;await Frames(30);
                }
                _root.Back();await Frames(3);
                _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),1,practice:true);
                await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"temple stair practice starts: "+preset,150);
                await Capture("temple-stairs-"+preset);
                var camera=GetViewport().GetCamera3D()!;var pose=camera.Transform;
                camera.Position=new(14,11,17);camera.LookAt(new(12.5f,3,8));
                await Capture("temple-stairs-detail-"+preset);
                camera.Position+=new Vector3(.07f,0,0);camera.LookAt(new(12.5f,3,8));
                await Capture("temple-stairs-shift-"+preset);camera.Transform=pose;
                var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
                var meshes=AuditDescendants(terrain).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null).ToArray();
                int overlaps=0;
                // Sample actual stair tread interiors. A second top at the same height
                // fights the pale tread in the depth buffer as the camera moves.
                for(int step=0;step<6;step++)
                {
                    float z=11.5f-((step+.5f)/6-.5f),top=(1+(step+1)/6f)*TempleStageView.HeightScale;
                    int faces=meshes.Count(m=>
                    {
                        var bounds=(terrain.GlobalTransform.AffineInverse()*m.GlobalTransform)*m.Mesh.GetAabb();
                        return Math.Abs(bounds.End.Y-top)<.0001f&&bounds.Position.X<12.5f&&bounds.End.X>12.5f&&bounds.Position.Z<z&&bounds.End.Z>z;
                    });
                    if(faces!=1)overlaps++;
                }
                Event("temple-stair-depth-probe",new{preset=preset.ToString(),overlaps});
                if(overlaps>0)defects.Add("temple staircase has competing coplanar top faces on "+preset);
                _root.Back();await Frames(3);
            }
            await ShorelineBlast();
            Check(defects.Count==0,"stair surfaces and rear-ring fireballs remain intact across presets"+(defects.Count>0?": "+string.Join("; ",defects):""));
        }
        finally
        {
            Input(0,default);_root.Navigate("Modes");await Frames(3);
            await SelectGraphics(quality);await SelectLocalLighting(lighting);
        }
    }
    private async Task ShorelineBlast()
    {
        await SelectGraphics(GraphicsPreset.Medium);
        await ShoreProbe(occluded:true);
        // Negative control: the same probe must see the pre-fix defect. Remove only
        // the sea's depth writes (the retained before-waterline variant) before a
        // second identical detonation, then restore the shipped shader.
        var water=ArtMaterials.WaterShaderFor(GraphicsPreset.Medium);var shipped=water.Code;
        Check(shipped.Contains(", depth_draw_always;"),"shoreline control can isolate the sea's depth writes");
        water.Code=shipped.Replace(", depth_draw_always;",";",StringComparison.Ordinal);
        try{await ShoreProbe(occluded:false);}
        finally{water.Code=shipped;await Frames(3);}
    }
    private async Task ShoreProbe(bool occluded)
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0){Advanced=new AdvancedRules{DisabledItems="fire,bomb,remote,red,bounce,heart,focus,tiny,big,haste,slow"}},1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"shoreline effect practice starts",150);
        await MoveTo(8.5f,25.5f,350);
        Check(Math.Abs(Player.GetProperty("y").GetSingle())<.01f,"ordinary movement reaches the low shoreline court");
        var camera=GetViewport().GetCamera3D()!;var pose=camera.Transform;
        try
        {
            camera.Position=new(13,10,43);camera.LookAt(new(13,-.3f,25.5f));
            Input(0,new PlayerInput(1,0));await Frames(1);
            Input(0,new PlayerInput(0,0,A:true,B:true));await Frames(2);
            Input(0,new PlayerInput(0,0,B:true));await Frames(2);
            for(int i=0;i<3;i++)
            {
                Input(0,new PlayerInput(0,0,A:true,B:true));await Frames(2);
                Input(0,new PlayerInput(0,0,B:true));await Frames(14);
            }
            Input(0,default);await Frames(3);
            await WaitState(s=>s.GetProperty("hazards").GetArrayLength()>0,"real charged shoreline bomb detonates",130);
            var hazard=State.GetProperty("hazards")[0];
            var center=new Vector3(hazard.GetProperty("x").GetSingle(),hazard.GetProperty("y").GetSingle()*TempleStageView.HeightScale+.3f,hazard.GetProperty("z").GetSingle());
            float radius=hazard.GetProperty("r").GetSingle();
            Check(radius>2&&Math.Abs(hazard.GetProperty("y").GetSingle())<.01f,"charged shoreline fireball intersects the sea below the low court");
            await Frames(7);await Capture(occluded?"shore-blast-waterline-inspection":"shore-blast-waterline-control",0);
            if(!_headless)
            using(var image=Image.LoadFromFile(_screenshots[^1]))
            {
                var world=center+new Vector3(0,-.53f,.8f)*radius;
                Check(world.Y<LightingArena.WaterHeight-.15f,"shore probe samples below the animated sea surface");
                // Retained Mac captures put this column's waterline at rows 629-633 by
                // wave phase. Sample 6 pixels lower, still inside the submerged half of
                // the fireball, so waves cannot reach the patch and missing depth does.
                var pixel=camera.UnprojectPosition(world)*new Vector2(image.GetWidth(),image.GetHeight())/GetViewport().GetVisibleRect().Size+new Vector2(0,6);
                Event("shore-water-probe-target",new{center=center.ToString(),radius,world=world.ToString(),pixel=pixel.ToString(),water=LightingArena.WaterHeight});
                Check(pixel.X>=2&&pixel.X<image.GetWidth()-2&&pixel.Y>=2&&pixel.Y<image.GetHeight()-2,"submerged fire probe stays inside the captured inspection view");
                int fire=0;
                for(int y=(int)pixel.Y-2;y<=(int)pixel.Y+2;y++)for(int x=(int)pixel.X-2;x<=(int)pixel.X+2;x++)
                {
                    var color=image.GetPixel(x,y);
                    if(color.R>.35f&&color.R>color.G*1.4f&&color.R>color.B*1.6f)fire++;
                }
                Event("shore-water-probe",new{occluded,center=center.ToString(),radius,world=world.ToString(),pixel=pixel.ToString(),water=LightingArena.WaterHeight,fire});
                // MSAA may still soften one row. Missing depth fills the 5x5 patch with fire.
                if(occluded)Check(fire<=5,"water occludes submerged fire beyond the moving shoreline edge");
                else Check(fire>=20,"shoreline probe detects submerged fire when the sea's depth writes are removed");
            }
        }
        finally{camera.Transform=pose;Input(0,default);_root.Back();await Frames(3);}
    }

}
