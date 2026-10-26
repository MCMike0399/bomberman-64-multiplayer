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
                await Frames(30);_root.Back();await Frames(3);
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
            Check(defects.Count==0,string.Join("; ",defects));
        }
        finally
        {
            Input(0,default);_root.Navigate("Modes");await Frames(3);
            await SelectGraphics(quality);await SelectLocalLighting(lighting);
        }
    }
}
