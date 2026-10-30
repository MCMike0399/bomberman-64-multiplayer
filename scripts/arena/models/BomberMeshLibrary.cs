using System.Security.Cryptography;
using System.Text.Json;
using BlastCircuit.Rendering;
using Bomber.Game.Online;
using Godot;
namespace BlastCircuit.Arena.Models;

/// <summary>Mac-authored packed geometry; editable builders remain the authoring source.</summary>
internal static class BomberMeshLibrary
{
    internal const string LibraryPath="res://assets/models/bomber-meshes.res";
    internal const string ManifestPath="res://assets/models/bomber-meshes.json";
    private static readonly Dictionary<string,ArrayMesh> Generated=[];
    private static bool _generating;
    private static MeshLibrary? _library;
    private static Dictionary<string,string>? _hashes;
    // Mac SDK authoring stays procedural; exports and other hosts share Mac bytes.
    internal static bool SharedEnabled=>!_generating&&(!OS.HasFeature("editor")||OS.GetName()!="macOS"||OS.GetCmdlineUserArgs().Contains("--bomber-meshes-shared"));
    internal static string CanonicalHash(string key){Load();return _hashes![key];}
    internal static string Side(float x)=>x<0?"left":"right";
    internal static string SpiralKey(float x,int frame)=>"spiral-"+Side(x)+"-"+frame.ToString("D2",System.Globalization.CultureInfo.InvariantCulture);
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static ArrayMesh Get(string key,Func<ArrayMesh> builder)
    {
        if(_generating){var mesh=builder();Generated.Add(key,mesh);return mesh;}
        if(!SharedEnabled)return builder();
        Load();int index=_library!.FindItemByName(key);
        if(index<0||_library.GetItemMesh(index) is not ArrayMesh result)throw new InvalidDataException("Missing canonical Bomber mesh: "+key);
        if(Hash(GD.VarToBytes(result.Get("_surfaces")))!=_hashes![key])throw new InvalidDataException("Canonical Bomber surface bytes changed: "+key);
        return result;
    }
    private static void Load()
    {
        if(_library is not null)return;
        using var manifest=JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ManifestPath));
        if(manifest.RootElement.GetProperty("schemaVersion").GetInt32()!=1)throw new InvalidDataException("Unknown Bomber mesh manifest");
        string expected=manifest.RootElement.GetProperty("librarySha256").GetString()!;
        if(Hash(Godot.FileAccess.GetFileAsBytes(LibraryPath))!=expected)throw new InvalidDataException("Bomber mesh library hash differs");
        _hashes=manifest.RootElement.GetProperty("meshes").EnumerateArray().ToDictionary(m=>m.GetProperty("key").GetString()!,m=>m.GetProperty("packedSurfaceSha256").GetString()!);
        _library=ResourceLoader.Load<MeshLibrary>(LibraryPath)??throw new InvalidDataException("Bomber mesh library did not load");
    }
    internal static void Generate()
    {
        if(OS.GetName()!="macOS"||!OS.HasFeature("editor"))throw new InvalidOperationException("Generate canonical meshes with the pinned Mac authoring SDK");
        if(Generated.Count!=0)throw new InvalidOperationException("Use a fresh process for geometry generation");
        _generating=true;
        try
        {
            var actor=new BomberModel(new ArtMaterials(),new Color("f6f0df"),"");actor.Free();
            if(Generated.Count!=32)throw new InvalidDataException("Canonical mesh roster differs from the authored 32 meshes");
            var library=new MeshLibrary();var entries=new List<object>();int index=0;
            foreach(var (key,mesh) in Generated.OrderBy(p=>p.Key,StringComparer.Ordinal))
            {
                library.CreateItem(index);library.SetItemName(index,key);library.SetItemMesh(index,mesh);
                entries.Add(new{key,index,packedSurfaceSha256=Hash(GD.VarToBytes(mesh.Get("_surfaces")))});index++;
            }
            string[] sources=["scripts/arena/models/BomberModel.cs","scripts/arena/models/BomberModel.Mishaps.cs","scripts/arena/models/BomberMeshLibrary.cs"];
            var sourceHashes=sources.ToDictionary(s=>s,s=>Hash(File.ReadAllBytes(ProjectSettings.GlobalizePath("res://"+s))));
            library.SetMeta("source_files_sha256",JsonSerializer.Serialize(sourceHashes));
            string output=ProjectSettings.GlobalizePath(LibraryPath);Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if(ResourceSaver.Save(library,LibraryPath,ResourceSaver.SaverFlags.Compress)!=Error.Ok)throw new IOException("Canonical mesh library save failed");
            var result=new{schemaVersion=1,producer=new{sourceRevision=OnlineProtocol.SourceRevision,platform="macOS",engine=Engine.GetVersionInfo()["hash"].AsString()},
                sourceFiles=sourceHashes,librarySha256=Hash(File.ReadAllBytes(output)),meshes=entries};
            File.WriteAllText(ProjectSettings.GlobalizePath(ManifestPath),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true})+"\n");
            // Reload through the actual resource reader and require untouched packed bytes.
            using var loaded=ResourceLoader.Load<MeshLibrary>(LibraryPath,"",ResourceLoader.CacheMode.Ignore);
            foreach(var (key,mesh) in Generated)
            {
                var decoded=(ArrayMesh)loaded.GetItemMesh(loaded.FindItemByName(key));
                if(!GD.VarToBytes(mesh.Get("_surfaces")).AsSpan().SequenceEqual(GD.VarToBytes(decoded.Get("_surfaces"))))
                    throw new InvalidDataException("Resource serialization changed mesh surface: "+key);
            }
            GD.Print("BOMBER_MESH_LIBRARY_OK meshes=32 packed-roundtrip=exact");
        }
        finally{_generating=false;}
    }
}
