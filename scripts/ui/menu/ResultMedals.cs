using Godot;
namespace BlastCircuit.UI.Menu;

/// <summary>Little struck metal medals, lit as objects instead of flat score icons.</summary>
public partial class ResultMedals : RenderSurface
{
    public int Wins {get;set;}
    private SubViewport _viewport=null!;
    private bool _drawn;
    public override void _Ready()
    {
        MouseFilter=MouseFilterEnum.Ignore;
        _viewport=new SubViewport {OwnWorld3D=true,TransparentBg=true,Size=new(340,94),RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled};SetViewport(_viewport);
        var world=new Node3D();_viewport.AddChild(world);
        world.AddChild(new WorldEnvironment {Environment=new Godot.Environment {AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new Color("d6c9aa"),AmbientLightEnergy=.55f,TonemapMode=Godot.Environment.ToneMapper.Filmic}});
        world.AddChild(new DirectionalLight3D {RotationDegrees=new(-35,-25,0),LightColor=new Color("fff2ca"),LightEnergy=1.9f});
        world.AddChild(new DirectionalLight3D {RotationDegrees=new(20,140,0),LightColor=new Color("a7bdff"),LightEnergy=.6f});
        var camera=new Camera3D {Position=new(0,.18f,5),Projection=Camera3D.ProjectionType.Orthogonal,Size=.95f,Current=true};world.AddChild(camera);camera.LookAt(Vector3.Zero);
        var gold=ArcadeBackdrop.Surface("dfb94e",.29f,.75f);var bright=ArcadeBackdrop.Surface("ffdf83",.36f,.5f);var dark=ArcadeBackdrop.Surface("987133",.4f,.65f);var ribbon=ArcadeBackdrop.Surface("b6537a",.65f);
        int count=Math.Min(5,Wins);
        for(int i=0;i<count;i++)
        {
            // Medals stack leftward from the win count so a single medal sits beside its number.
            var medal=new Node3D {Position=new(1.42f-(count-1-i)*.63f,.035f,0),RotationDegrees=new(0,-11,0)};world.AddChild(medal);
            foreach(float x in new[]{-.1f,.1f}){var tail=ArcadeBackdrop.Box(medal,new(x,-.23f,-.035f),new(.17f,.27f,.035f),ribbon);tail.RotationDegrees=new(0,0,x<0?-12:12);}
            var coin=ArcadeBackdrop.Cylinder(medal,Vector3.Zero,.255f,.095f,gold);coin.RotationDegrees=new(90,0,0);
            var inset=ArcadeBackdrop.Cylinder(medal,new(0,0,.056f),.205f,.015f,dark);inset.RotationDegrees=new(90,0,0);
            var face=ArcadeBackdrop.Cylinder(medal,new(0,0,.066f),.185f,.015f,gold);face.RotationDegrees=new(90,0,0);
            var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
            for(int j=0;j<10;j++)
            {
                Vector3 Point(int n){float a=Mathf.Pi/2+n*Mathf.Tau/10,r=n%2==0?.145f:.065f;return new(Mathf.Cos(a)*r,Mathf.Sin(a)*r,.083f);}
                surface.SetNormal(Vector3.Back);surface.AddVertex(Vector3.Back*.083f);surface.AddVertex(Point(j+1));surface.AddVertex(Point(j));
            }
            medal.AddChild(new MeshInstance3D {Mesh=surface.Commit(),MaterialOverride=bright});
        }
    }
    public override void _Process(double delta)
    {
        base._Process(delta);
        if(_drawn||!IsVisibleInTree())return;
        _viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;_drawn=true;
    }
}
