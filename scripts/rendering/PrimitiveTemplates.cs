using System.Security.Cryptography;
using System.Text.Json;
using Godot;
namespace BlastCircuit.Rendering;

/// <summary>Immutable Mac-authored primitive surfaces. Missing requests fail instead of regenerating through platform libm.</summary>
internal static class PrimitiveTemplates
{
    private const string Root="res://assets/models/primitives/";
    private static readonly Dictionary<string,Mesh> Cache=[];
    private static readonly Dictionary<ulong,(string Type,(StringName Name,Variant Value)[] Properties)> Requests=[];
    private static readonly string Capture=OS.GetEnvironment("BLAST_PRIMITIVE_CAPTURE_DIR");
    private static readonly Dictionary<string,object> Captured=[];
    private static Dictionary<string,string>? _hashes;
    private static int _requestsSincePrune;
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static Mesh Get(string type,params (StringName Name,Variant Value)[] properties)
    {
        using var request=new Godot.Collections.Array {type};
        foreach(var (name,value) in properties){request.Add(name);request.Add(value);}
        string key=Hash(GD.VarToBytes(request));
        Mesh mesh;
        if(Capture.Length>0)
        {
            if(OS.GetName()!="macOS"||!OS.HasFeature("editor")||Engine.GetVersionInfo()["major"].AsInt32()!=4||Engine.GetVersionInfo()["minor"].AsInt32()!=7||Engine.GetVersionInfo()["patch"].AsInt32()!=2)
                throw new InvalidOperationException("Primitive capture requires the pinned Mac 4.7.2 authoring SDK");
            if(ClassDB.Instantiate(type).AsGodotObject() is not PrimitiveMesh primitive)throw new InvalidDataException("Not a primitive mesh: "+type);
            foreach(var (name,value) in properties)primitive.Set(name,value);
            if(!Captured.ContainsKey(key))CaptureMesh(key,type,properties,primitive);mesh=primitive;
        }
        else
        {
            if(!Cache.TryGetValue(key,out var template))
            {
                _hashes??=JsonSerializer.Deserialize<Dictionary<string,string>>(Godot.FileAccess.GetFileAsString(Root+"hashes.json"))
                    ??throw new InvalidDataException("Missing primitive template manifest");
                if(!_hashes.TryGetValue(key,out string? hash))throw new InvalidDataException("Uncaptured primitive request: "+type+" "+key);
                string path=Root+key+".res";
                if(Hash(Godot.FileAccess.GetFileAsBytes(path))!=hash)throw new InvalidDataException("Primitive template hash differs: "+key);
                template=ResourceLoader.Load<ArrayMesh>(path)??throw new InvalidDataException("Primitive template did not load: "+key);
                Cache.Add(key,template);
            }
            // Preserve original constructor ownership and draw ordering. Existing
            // ArtGeometry/Scenery caches still choose what they intentionally share.
            mesh=(Mesh)template.Duplicate();
        }
        Requests.Add(mesh.GetInstanceId(),(type,properties));
        if(++_requestsSincePrune>=256)
        {
            foreach(ulong id in Requests.Keys.Where(id=>GodotObject.InstanceFromId(id) is null).ToArray())Requests.Remove(id);
            _requestsSincePrune=0;
        }
        return mesh;
    }
    internal static bool Is(Mesh? mesh,string type)=>mesh is not null&&Requests.TryGetValue(mesh.GetInstanceId(),out var request)&&request.Type==type;
    internal static Variant Parameter(Mesh mesh,string name)=>Requests[mesh.GetInstanceId()].Properties.Single(p=>p.Name==name).Value;
    internal static Mesh With(Mesh mesh,string name,Variant value)
    {
        var request=Requests[mesh.GetInstanceId()];var properties=request.Properties.ToList();int index=properties.FindIndex(p=>p.Name==name);
        if(index<0)properties.Add((name,value));else properties[index]=(name,value);
        return Get(request.Type,properties.ToArray());
    }
    private static void CaptureMesh(string key,string type,(StringName Name,Variant Value)[] properties,PrimitiveMesh original)
    {
        Directory.CreateDirectory(Capture);
        var surfaces=new Godot.Collections.Array();
        for(int i=0;i<original.GetSurfaceCount();i++)
        {
            var packed=RenderingServer.MeshGetSurface(original.GetRid(),i);
            if(packed.ContainsKey("material")||packed.ContainsKey("lods")||packed.ContainsKey("blend_shape_data"))
                throw new InvalidDataException("Unexpected primitive material/LOD/blend data");
            surfaces.Add(packed);
        }
        using var mesh=new ArrayMesh();mesh.Set("_surfaces",surfaces);
        string path=Path.Combine(Capture,key+".res");
        if(ResourceSaver.Save(mesh,path,ResourceSaver.SaverFlags.Compress)!=Error.Ok)throw new IOException("Primitive template save failed");
        using var loaded=ResourceLoader.Load<ArrayMesh>(path,"",ResourceLoader.CacheMode.Ignore);
        for(int i=0;i<original.GetSurfaceCount();i++)
            if(!GD.VarToBytes(RenderingServer.MeshGetSurface(original.GetRid(),i)).AsSpan().SequenceEqual(GD.VarToBytes(RenderingServer.MeshGetSurface(loaded.GetRid(),i))))
                throw new InvalidDataException("Packed primitive roundtrip differs: "+key);
        var caller=new System.Diagnostics.StackTrace(true).GetFrames().FirstOrDefault(f=>f.GetMethod()?.DeclaringType!=typeof(PrimitiveTemplates)&&f.GetFileName() is not null);
        string? source=caller?.GetFileName();if(source is not null)source=Path.GetRelativePath(ProjectSettings.GlobalizePath("res://"),source).Replace('\\','/');
        Captured[key]=new{type,source,line=caller?.GetFileLineNumber(),properties=properties.Select(p=>new{name=p.Name.ToString(),variantBytes=Convert.ToBase64String(GD.VarToBytes(p.Value))}),sha256=Hash(File.ReadAllBytes(path))};
        File.WriteAllText(Path.Combine(Capture,"capture-"+System.Environment.ProcessId+".json"),JsonSerializer.Serialize(Captured,new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
}
