using Godot;
using BlastCircuit.Rendering;
using BlastCircuit.UI;
using System.Text;
namespace Bomber.Testing;

public partial class PlaytestDriver
{
    private async Task AssetAudit()
    {
        Check(!AuditResourcePresent("res://assets/fonts/asset-audit-missing.ttf"),"asset audit detects a missing bundled resource without accepting a fallback");
        using var empty=new ArrayMesh();
        Check(AuditMeshProblems(empty).Count>0,"asset audit rejects an empty mesh");
        Check(AuditVertexProblems([Vector3.Zero,Vector3.Right,new Vector3(float.NaN,1,0)]).Contains("nonfinite vertex"),"asset audit rejects nonfinite geometry before renderer submission");
        foreach(var (path,font) in new[]{("res://assets/fonts/DelaGothicOne-Regular.ttf",ArcadeTheme.Display),("res://assets/fonts/MPLUSRounded1c-ExtraBold.ttf",ArcadeTheme.Body)})
        {
            Check(AuditResourcePresent(path)&&font is FontFile {Data.Length:>0}&&font.ResourcePath==path,"bundled font loads directly rather than silently falling back: "+path);
            string required="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:!?+-/&()";
            string missing=string.Concat(required.EnumerateRunes().Where(r=>!font.HasChar(r.Value)).Select(r=>r.ToString()));
            var size=font.GetStringSize("Bomb 0123456789",fontSize:24);
            Check(missing.Length==0&&float.IsFinite(size.X)&&size.X>0&&size.Y>0,"bundled font has ordinary UI letters/digits and usable metrics: "+path+" missing="+missing);
        }
        Check(AuditResourcePresent("res://icon.svg")&&GD.Load<Texture2D>("res://icon.svg") is { } icon&&icon.GetWidth()>0&&icon.GetHeight()>0,"bundled application icon imports as a nonempty texture");
        var art=new ArtMaterials();var specimens=new List<(string Name,Node3D Model)>();
        for(int i=0;i<ArtMaterials.PlayerColors.Length;i++)
        {
            var player=new BomberModel(art,ArtMaterials.PlayerColors[i],"");player.ShowName(false);
            player.Animate(1f/30,.15f);player.Pose(true,false,false);player.Pump(2);player.Animate(1f/30,.15f);
            specimens.Add(("suit "+i,player));
        }
        foreach(var (name,red,remote,pumps) in new[]{("normal bomb",false,false,0),("red bomb",true,false,0),("remote bomb",false,true,0),("pumped bomb",false,false,3)})
        {
            var bomb=new BombModel(art);bomb.UpdateState(pumps,3,red,remote,1.5f,null);bomb._Process(.2);
            specimens.Add((name,bomb));
        }
        foreach(string kind in new[]{"fire","bomb","remote","red","heart"})
        {var pickup=new PowerupModel(art,kind);pickup._Process(.2);specimens.Add((kind+" pickup",pickup));}
        var surfaces=new Node3D();
        var shaderMaterials=new[]{art.Stone(),art.Masonry(),art.Bedrock(),art.Water()};
        for(int i=0;i<shaderMaterials.Length;i++)ArtGeometry.Box(surfaces,new(i*1.5f,0,0),Vector3.One,shaderMaterials[i]);
        specimens.Add(("production surface shaders",surfaces));
        try
        {
            foreach(var (name,model) in specimens)
            {
                var problems=AuditModelProblems(model,out int meshes,out int vertices);
                Event("asset-geometry",new {name,meshes,vertices,problems});
                Check(problems.Count==0&&meshes>0&&vertices>0,"production asset has readable materials and finite nonempty geometry: "+name+" "+string.Join("; ",problems.Take(3)));
            }
            var logicalSize=_root.GetViewport().GetVisibleRect().Size;
            var sizes=_headless?new[]{new Vector2I((int)logicalSize.X,(int)logicalSize.Y)}:new[]{new Vector2I(1280,800),new Vector2I(1280,720)};
            if(_headless)Event("asset-audit-mode",new {mode="headless logical viewport",logicalSize=logicalSize.ToString(),nativeResize=false});
            foreach(var size in sizes)
            {
                if(!_headless)await ResizeWindow(size);
                foreach(string screen in new[]{"Modes","Roster","Setup","Online","Controls"})
                {
                    _root.Navigate(screen);await Frames(4);
                    Check(_root.ScreenName==screen,"asset audit opens actual product screen: "+screen);
                    AuditVisibleMenu(screen,size);
                }
            }
            if(!_headless)await ResizeWindow(new Vector2I(1280,800));_root.Navigate("Modes");await Frames(3);
            if(!_headless)await AssetInspectionGallery(specimens);
        }
        finally
        {
            foreach(var (_,model) in specimens)if(GodotObject.IsInstanceValid(model))model.Free();
        }
    }
    private static bool AuditResourcePresent(string path)=>ResourceLoader.Exists(path)&&GD.Load<Resource>(path)!=null;
    private static IEnumerable<Node> AuditDescendants(Node parent)
    {
        foreach(var child in parent.GetChildren()){yield return child;foreach(var descendant in AuditDescendants(child))yield return descendant;}
    }
    private static List<string> AuditVertexProblems(Vector3[] vertices)
    {
        var problems=new List<string>();
        if(vertices.Length<3)problems.Add("fewer than three vertices");
        if(vertices.Any(v=>!v.IsFinite()))problems.Add("nonfinite vertex");
        return problems;
    }
    private static List<string> AuditMeshProblems(Mesh mesh)
    {
        var problems=new List<string>();
        int surfaces=mesh.GetSurfaceCount();if(surfaces==0){problems.Add("empty mesh");return problems;}
        var bounds=mesh.GetAabb();
        if(!bounds.Position.IsFinite()||!bounds.Size.IsFinite()||bounds.Size.X<=0||bounds.Size.Y<=0||bounds.Size.Z<=0)problems.Add("nonfinite or collapsed bounds");
        for(int surface=0;surface<surfaces;surface++)
        {
            var arrays=mesh.SurfaceGetArrays(surface);
            if(arrays.Count<=(int)Mesh.ArrayType.Vertex){problems.Add("missing vertex array");continue;}
            var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();problems.AddRange(AuditVertexProblems(vertices));
            if(arrays[(int)Mesh.ArrayType.Normal].VariantType!=Variant.Type.Nil)
            {
                var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                if(normals.Length!=vertices.Length||normals.Any(n=>!n.IsFinite()||n.LengthSquared()<.01f))problems.Add("missing or invalid vertex normals");
            }
        }
        return problems;
    }
    private static List<string> AuditModelProblems(Node3D model,out int meshes,out int vertices)
    {
        var problems=new List<string>();meshes=vertices=0;
        foreach(var node in AuditDescendants(model).Prepend(model))
        {
            if(node is Node3D spatial&&(!spatial.Transform.Origin.IsFinite()||!spatial.Transform.Basis.X.IsFinite()||!spatial.Transform.Basis.Y.IsFinite()||!spatial.Transform.Basis.Z.IsFinite()))problems.Add("nonfinite transform: "+node.Name);
            if(node is not MeshInstance3D geometry)continue;
            meshes++;var mesh=geometry.Mesh;
            if(mesh==null){problems.Add("missing mesh: "+node.Name);continue;}
            problems.AddRange(AuditMeshProblems(mesh).Select(p=>node.Name+": "+p));
            for(int surface=0;surface<mesh.GetSurfaceCount();surface++)
            {
                vertices+=mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
                var material=geometry.MaterialOverride??geometry.GetSurfaceOverrideMaterial(surface)??mesh.SurfaceGetMaterial(surface);
                if(material is StandardMaterial3D standard)
                {
                    var color=standard.AlbedoColor;
                    if(!float.IsFinite(color.R)||!float.IsFinite(color.G)||!float.IsFinite(color.B)||!float.IsFinite(color.A)||!float.IsFinite(standard.Roughness))problems.Add("nonfinite material");
                }
                else if(material is ShaderMaterial shader)
                {
                    if(shader.Shader==null||!shader.Shader.Code.Contains("shader_type spatial;",StringComparison.Ordinal))problems.Add("missing readable spatial shader source");
                }
                else problems.Add("missing or unsupported material");
            }
        }
        return problems;
    }
    private void AuditVisibleMenu(string screen,Vector2I size)
    {
        var controls=AuditDescendants(_root).OfType<Control>().Where(c=>c.IsVisibleInTree()&&c.GetViewport()==_root.GetViewport()).ToList();
        var viewport=_root.GetViewport().GetVisibleRect().Grow(1);var outside=new List<string>();var missing=new HashSet<string>();int interactive=0,glyphTexts=0;
        foreach(var control in controls)
        {
            if(control is BaseButton or LineEdit)
            {
                interactive++;var transform=control.GetGlobalTransformWithCanvas();
                foreach(var corner in new[]{Vector2.Zero,new Vector2(control.Size.X,0),control.Size,new Vector2(0,control.Size.Y)})
                    if(!viewport.HasPoint(transform*corner)){outside.Add(control.Name.ToString());break;}
            }
            string text=control switch {Label label=>label.Text,Button button=>button.Text,LineEdit field=>field.Text+field.PlaceholderText,_=>""};
            if(string.IsNullOrWhiteSpace(text))continue;
            glyphTexts++;var font=control.GetThemeFont("font");
            foreach(var rune in text.EnumerateRunes())if(!Rune.IsWhiteSpace(rune)&&!font.HasChar(rune.Value))missing.Add(rune+" in "+control.Name);
        }
        Event("asset-menu",new {screen,window=size.ToString(),viewport=viewport.ToString(),interactive,glyphTexts,outside,missing});
        Check(interactive>0&&outside.Count==0,"native interactive controls fit supported viewport: "+screen+" "+size+" "+string.Join(", ",outside));
        Check(glyphTexts>0&&missing.Count==0,"actual menu text has supported font glyphs: "+screen+" "+size+" "+string.Join(", ",missing));
    }
    private async Task AssetInspectionGallery(List<(string Name,Node3D Model)> specimens)
    {
        var layer=new CanvasLayer {Layer=100};_root.AddChild(layer);
        var panel=new PanelContainer {OffsetLeft=30,OffsetTop=30,AnchorRight=1,AnchorBottom=1,OffsetRight=-30,OffsetBottom=-30};layer.AddChild(panel);
        panel.AddThemeStyleboxOverride("panel",ArcadeTheme.Plate());var column=ArcadeTheme.Column(panel);
        ArcadeTheme.Heading(column,"Asset audit inspection",28);ArcadeTheme.Text(column,"Production meshes and materials • inspection gallery",16);
        var container=new SubViewportContainer {Stretch=true,SizeFlagsVertical=Control.SizeFlags.ExpandFill};column.AddChild(container);
        var viewport=new SubViewport {Size=new(1200,650),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always};container.AddChild(viewport);
        var world=new Node3D();viewport.AddChild(world);
        world.AddChild(new WorldEnvironment {Environment=new Godot.Environment {BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color("fff6dc"),AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=Colors.White,AmbientLightEnergy=.65f}});
        world.AddChild(new DirectionalLight3D {RotationDegrees=new(-45,-25,0),LightEnergy=1.3f});
        var camera=new Camera3D {Position=new(0,10,16),Projection=Camera3D.ProjectionType.Orthogonal,Size=9,Current=true};world.AddChild(camera);camera.LookAt(new Vector3(0,.6f,0));
        try
        {
            int suits=0,bombs=0,pickups=0;
            foreach(var (_,model) in specimens)
            {
                model.Position=model switch {BomberModel=>new((suits++-3.5f)*1.5f,0,-2.2f),BombModel=>new((bombs++-1.5f)*2.6f,0,1),PowerupModel=>new((pickups++-2)*1.7f,.5f,3.5f),_=>new(-2.25f,0,5.4f)};
                world.AddChild(model);model.ProcessMode=ProcessModeEnum.Disabled;
            }
            await Capture("asset-audit-inspection-gallery",5);
        }
        finally
        {
            foreach(var (_,model) in specimens)if(model.GetParent()==world)world.RemoveChild(model);
            layer.QueueFree();await Frames(2);
        }
    }
}
