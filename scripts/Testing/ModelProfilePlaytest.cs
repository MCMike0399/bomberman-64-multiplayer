using Godot;
using Bomber.Client;
using Bomber.Game;
using BlastCircuit.Rendering;
namespace Bomber.Testing;

public partial class PlaytestDriver
{
    private async Task ModelProfiles()
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"profile practice reaches fight",150);
        var player=_root.FindChildren("*","",true,false).OfType<BomberModel>()
            .Single(m=>m.IsVisibleInTree()&&m.GetViewport()==_root.GetViewport());
        var helmet=(MeshInstance3D)player.FindChild("HelmetShell",true,false);
        var torso=(MeshInstance3D)player.FindChild("Torso",true,false);
        var headSize=helmet.Mesh.GetAabb().Size;
        var bodySize=torso.Mesh.GetAabb().Size*torso.Scale.Abs();
        Check(headSize.Z/headSize.X>=.78f,"actual helmet mesh retains depth in side profile");
        Check(bodySize.Z/bodySize.X>=.88f,"actual torso mesh retains depth in side profile");
        Event("model-profile-dimensions",new {helmet=headSize.ToString(),torso=bodySize.ToString()});
        var arenaCamera=_root.GetViewport().GetCamera3D()!;
        var closeup=new Camera3D {Fov=32,Near=.05f,Far=100};_root.AddChild(closeup);closeup.MakeCurrent();
        try
        {
            foreach(var (name,x,z) in new[]{("front",0f,1f),("right",1f,0f),("back",0f,-1f),("left",-1f,0f)})
            {
                Input(0,new PlayerInput(x,z));await Frames(4);Input(0,default);await Frames(7);
                Check(Math.Abs(Player.GetProperty("fx").GetSingle()-x)<.01&&Math.Abs(Player.GetProperty("fz").GetSingle()-z)<.01,"real movement turns character toward "+name);
                var target=player.GlobalPosition+new Vector3(0,.85f,0);
                closeup.Position=target+new Vector3(0,1.2f,4.8f);closeup.LookAt(target);
                await Capture("model-profile-"+name);
            }
        }
        finally
        {
            Input(0,default);arenaCamera.MakeCurrent();closeup.QueueFree();
            _root.Back();await Frames(3);
        }
    }
}
