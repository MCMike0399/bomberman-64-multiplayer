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
        Check(headSize.Z/headSize.X>=.90f,"actual helmet mesh has a near-spherical side profile");
        var shellArrays=helmet.Mesh.SurfaceGetArrays(0);
        var shellVertices=shellArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var shellNormals=shellArrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var shellBounds=helmet.Mesh.GetAabb();var shellCenter=shellBounds.GetCenter();var shellRadii=shellBounds.Size*.5f;
        bool spherical=shellVertices.Length>0,normalSound=true;
        for(int i=0;i<shellVertices.Length;i++)
        {
            var local=shellVertices[i]-shellCenter;var unit=local/shellRadii;
            spherical&=Math.Abs(unit.LengthSquared()-1)<.001f;
            normalSound&=float.IsFinite(shellNormals[i].LengthSquared())&&Math.Abs(shellNormals[i].LengthSquared()-1)<.001f&&shellNormals[i].Dot(local)>0;
        }
        Check(spherical,"actual helmet vertices follow one continuous ellipsoid without a compressed front");
        Check(normalSound,"actual spherical helmet normals remain finite unit and outward");
        Check(bodySize.Z/bodySize.X>=.88f,"actual torso mesh retains depth in side profile");
        var face=(MeshInstance3D)player.FindChild("FaceSurface",true,false);
        Check(face.Mesh.GetAabb().Size.Z>.055f,"cream face has a curved volume and returning rim");
        var arrays=face.Mesh.SurfaceGetArrays(0);
        var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        bool sound=vertices.Length>0&&vertices.Length%3==0;
        for(int i=0;i<vertices.Length;i+=3)
        {
            var cross=(vertices[i+1]-vertices[i]).Cross(vertices[i+2]-vertices[i]);
            sound&=cross.LengthSquared()>1e-12f&&cross.Dot(normals[i]+normals[i+1]+normals[i+2])<0;
        }
        Check(sound,"actual curved face mesh has nondegenerate outward clockwise triangles");
        Event("model-profile-dimensions",new {helmet=headSize.ToString(),torso=bodySize.ToString()});
        var arenaCamera=_root.GetViewport().GetCamera3D()!;
        var closeup=new Camera3D {Fov=32,Near=.05f,Far=100};_root.AddChild(closeup);closeup.MakeCurrent();
        try
        {
            foreach(var (name,x,z) in new[]{("front",0f,1f),("right",1f,0f),("back",0f,-1f),("left",-1f,0f),("three-quarter",.7071f,.7071f)})
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
