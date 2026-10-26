using Godot;
using System.Diagnostics;
using BlastCircuit.Rendering;
using BlastCircuit.UI.Menu;
using BlastCircuit.Arena.Stages;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task MapPreviewLoading()
    {
        await ControllerFocusReady();
        var menu=AuditDescendants(_root).OfType<ArcadeMenu>().Single();
        var original=_root.Preferences.Graphics;
        var stages=new[]{"sunken-temple","rock-garden","up-and-down","top-rules","blizzard-battle","greedy-trap"};
        TempleStageView[] Miniatures()=>AuditDescendants(menu).OfType<TempleStageView>().Where(t=>t.GetParent().Name=="ArenaDiorama").ToArray();
        var cached=Miniatures().ToDictionary(t=>t.Layout.Id,t=>t.GetInstanceId());
        Check(stages.All(cached.ContainsKey)&&cached.Count==stages.Length,"all map previews are prepared before menu input is enabled");
        try
        {
            foreach(var preset in new[]{GraphicsPreset.Medium,GraphicsPreset.Low,GraphicsPreset.High})
            {
                _root.Navigate("Modes");await SelectGraphics(preset);
                // Preparation must preserve both the selected map and its actual retained scene.
                string selected=menu.Settings.Stage;
                Check(Miniatures().Single(t=>t.Visible).Layout.Id==selected,"graphics preparation restores selected map: "+preset);
                foreach(string page in new[]{"Setup","Practice","HostSetup"})
                {
                    _root.Navigate(page);await Frames(8);
                    var samples=new List<double>();var pipelines=new List<long>();
                    for(int i=0;i<stages.Length*2;i++)
                    {
                        NamedButton("StageSelector").GrabFocus();await Frames(1);
                        int direction=i<stages.Length?1:-1;
                        string expected=stages[(Array.IndexOf(stages,menu.Settings.Stage)+direction+stages.Length)%stages.Length];
                        var before=PipelineCounts();var watch=Stopwatch.StartNew();
                        var key=direction>0?Key.Right:Key.Left;
                        Godot.Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=true});
                        await Frames(1);
                        Godot.Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=false});
                        double worstFrameMs=watch.Elapsed.TotalMilliseconds;
                        for(int frame=0;frame<3;frame++)
                        {
                            watch.Restart();await Frames(1);
                            worstFrameMs=Math.Max(worstFrameMs,watch.Elapsed.TotalMilliseconds);
                        }
                        var delta=PipelineCounts().Zip(before,(a,b)=>a-b).ToArray();
                        samples.Add(worstFrameMs);pipelines.Add(delta.Take(3).Sum());
                        Event("map-preview-selection",new {page,preset=preset.ToString(),stage=menu.Settings.Stage,worstFrameMs,mesh=delta[0],surface=delta[1],draw=delta[2],backgroundSpecialization=delta[3],fixedStep=true});
                        var miniatures=Miniatures();var active=miniatures.Single(t=>t.IsVisibleInTree());
                        Check(menu.Settings.Stage==expected&&active.Layout.Id==expected,"map input updates the actual miniature: "+page+" "+expected);
                        Check(miniatures.Length==cached.Count&&miniatures.All(t=>cached[t.Layout.Id]==t.GetInstanceId()),"map navigation reuses prepared scenes");
                        Check(miniatures.Where(t=>t!=active).All(t=>t.ProcessMode==ProcessModeEnum.Disabled),"inactive previews remain paused");
                        if(page=="Setup"&&i<stages.Length)await Capture("map-preview-"+GraphicsQuality.ToWire(preset)+"-"+expected);
                        if(i==stages.Length-1){GC.Collect();await Frames(3);}
                    }
                    Event("map-preview-stalls",new {page,preset=preset.ToString(),worstFrameMs=samples.Max(),fixedStep=true});
                    Check(pipelines.Sum()==0,"choosing maps needs no synchronous pipeline creation: "+page+" "+preset);
                }
            }
        }
        finally {_root.Navigate("Modes");await SelectGraphics(original);}
    }
}
