using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Models;
using BlastCircuit.Rendering;
using BlastCircuit.UI;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    // Every collectible in rule order: upgrades, abilities, bomb types, then timed effects.
    private static readonly (string Kind,string Label)[] ShowcasePickups=[("fire","Blast up"),("bomb","Bomb up"),("heart","Heart"),("focus","Focus"),
        ("remote","Remote"),("red","Power bomb"),("bounce","Bounce"),("tiny","Tiny"),("big","Big"),("haste","Haste"),("slow","Slow")];
    /// <summary>
    /// Matched pickup evidence for art review: a labelled studio lineup at the battle camera's
    /// angle, then the actual Sunken Temple practice caches from the battle and a closer camera.
    /// </summary>
    private async Task PickupShowcase()
    {
        if(!_headless)
        {
            // Two fixed animation instants show the resting pose and the motion range.
            await PickupStudio("pickup-showcase-studio",.9f);
            await PickupStudio("pickup-showcase-studio-motion",2.35f);
        }
        // Practice keeps a restricted pool of six or fewer intact, so two pools show all eleven kinds.
        foreach(var (pool,disabled) in new[]{("upgrades-equipment","bomb,focus,tiny,big,haste,slow"),("abilities-effects","fire,remote,red,heart,bounce")})
        {
            _root.StartLocal(new SessionSettings(Bots:0){Advanced=new AdvancedRules{ItemSeconds=60,DisabledItems=disabled}},1,practice:true);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"pickup showcase practice reaches fight: "+pool,150);
            await Frames(8);
            var expected=State.GetProperty("items").EnumerateArray().Select(i=>i.GetProperty("type").GetString()!).Order().ToArray();
            var models=AuditDescendants(LightingArena).OfType<PowerupModel>().ToArray();
            Check(expected.Length==6&&models.Select(m=>m.Kind).Order().SequenceEqual(expected)&&models.All(m=>m.IsVisibleInTree()),
                "practice cache renders one visible production model per accepted item: "+pool+" "+string.Join(',',expected));
            await Capture("pickup-showcase-battle-"+pool,6);
            var battleCamera=_root.GetViewport().GetCamera3D();
            var center=models.Aggregate(Vector3.Zero,(sum,m)=>sum+m.GlobalPosition)/models.Length;
            var inspection=new Camera3D {Fov=34,Near=.05f,Far=100};_root.AddChild(inspection);
            inspection.Position=center+new Vector3(0,.6f,.8f)*8.2f;inspection.LookAt(center);inspection.MakeCurrent();
            try{await Capture("pickup-showcase-closeup-"+pool,6);}
            finally{battleCamera?.MakeCurrent();inspection.QueueFree();}
            _root.Back();await Frames(3);
        }
    }
    private async Task PickupStudio(string label,float poseTime)
    {
        var art=new ArtMaterials();
        var layer=new CanvasLayer {Layer=100};_root.AddChild(layer);
        var container=new SubViewportContainer {Stretch=true};container.SetAnchorsPreset(Control.LayoutPreset.FullRect);layer.AddChild(container);
        var viewport=new SubViewport {OwnWorld3D=true,Msaa3D=Viewport.Msaa.Msaa4X,RenderTargetUpdateMode=SubViewport.UpdateMode.Always};container.AddChild(viewport);
        var world=new Node3D();viewport.AddChild(world);
        world.AddChild(new WorldEnvironment {Environment=new Godot.Environment {BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=ArcadeTheme.Cream,
            AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=Colors.White,AmbientLightEnergy=.6f,TonemapMode=Godot.Environment.ToneMapper.Filmic}});
        world.AddChild(new DirectionalLight3D {RotationDegrees=new(-52,-28,0),LightEnergy=1.25f,ShadowEnabled=true});
        // Pickups float .48 above the accepted surface, exactly as ArenaView places them.
        ArtGeometry.Box(world,new(0,-.73f,0),new(11,.5f,4.6f),art.Stone());
        var camera=new Camera3D {Fov=25,Current=true};world.AddChild(camera);
        camera.Position=new Vector3(0,0,.15f)+new Vector3(0,.6f,.8f)*12.4f;camera.LookAt(new(0,0,.15f));
        var heading=ArcadeTheme.Heading(layer,"Pickup lineup",28,ArcadeTheme.Ink);heading.Position=new(28,18);
        var models=new List<PowerupModel>();
        try
        {
            for(int i=0;i<ShowcasePickups.Length;i++)
            {
                var (kind,name)=ShowcasePickups[i];
                bool front=i>=6;int column=front?i-6:i;int count=front?ShowcasePickups.Length-6:6;
                var position=new Vector3((column-(count-1)/2f)*1.45f,0,front?1.05f:-1.05f);
                var model=new PowerupModel(art,kind) {Position=position};world.AddChild(model);models.Add(model);
                model.ProcessMode=ProcessModeEnum.Disabled;model._Process(poseTime);
                world.AddChild(new Label3D {Text=name,Font=ArcadeTheme.Display,FontSize=40,PixelSize=.0042f,OutlineSize=14,Modulate=ArcadeTheme.Ink,OutlineModulate=ArcadeTheme.Cream,
                    Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest=true,Position=position+new Vector3(0,-.44f,.7f)});
            }
            Check(models.Select(m=>m.Kind).SequenceEqual(ShowcasePickups.Select(p=>p.Kind)),"studio lineup builds every collectible kind: "+label);
            await Capture(label,6);
        }
        finally{layer.QueueFree();await Frames(2);}
    }
}
