using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BlastCircuit.Arena.Models;
using Godot;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private void CaptureMeshInputs()
    {
        if(OS.GetThreadCallerId()!=OS.GetMainThreadId())throw new InvalidOperationException("Mesh observation requires the owner thread");
        if(BomberModel.MeshBuildObservation is not null)throw new InvalidOperationException("Another mesh observer already owns this process");
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Static;
        var type=typeof(BomberModel);var eyes=(Dictionary<float,ArrayMesh>)type.GetField("EyeMeshes",flags)!.GetValue(null)!;
        ArrayMesh Cached(string name)=>(ArrayMesh)type.GetField(name,flags)!.GetValue(null)!;
        var recipes=new (string name,string builder,object[] arguments,ArrayMesh cached)[]{
            ("torso","CreateHelmet",[false],Cached("_torsoMesh")),("helmet","CreateHelmet",[true],Cached("_helmetMesh")),
            ("face-rim","CreateCurvedFacePatch",[.646f,.466f,0f,-.010f,.009f,.47f,true],Cached("_faceRimMesh")),
            ("face","CreateCurvedFacePatch",[.634f,.454f,0f,-.010f,.016f,.47f,true],Cached("_faceMesh")),
            ("left-eye","CreateCurvedFacePatch",[.036f,.193f,-.134f,.010f,.022f,.8f,false],eyes[-.134f]),
            ("right-eye","CreateCurvedFacePatch",[.036f,.193f,.134f,.010f,.022f,.8f,false],eyes[.134f])};
        string trace=Path.Combine(_output,"mesh-inputs.jsonl.gz"),binary=Path.Combine(_output,"mesh-inputs.bin");
        var evidence=new List<object>();long sequence=0;string current="";
        using(var blob=File.Create(binary))
        using(var file=File.Create(trace))
        using(var gzip=new GZipStream(file,CompressionLevel.Optimal))
        using(var lines=new StreamWriter(gzip,new UTF8Encoding(false)))
        {
            lines.NewLine="\n";
            void Record(string kind,object payload)
            {
                if(payload is byte[] bytes)
                {
                    long offset=blob.Position;blob.Write(bytes);payload=new{offset,length=bytes.Length,sha256=ReplayHash(bytes)};
                }
                lines.WriteLine(JsonSerializer.Serialize(new{sequence=sequence++,mesh=current,kind,payload}));
            }
            Record("header",new{schemaVersion=1,scope="Fresh procedural builder observations; image captures already completed; not release qualification",floatEncoding="IEEE754 binary32 hexadecimal",shared=new{sourceIdentity=Bomber.Game.Online.OnlineProtocol.SourceRevision,recipeId="bomber-six-mesh-inputs-v1",engineIdentity=Engine.GetVersionInfo()["string"].AsString()+":"+Engine.GetVersionInfo()["hash"].AsString()},
                provenance=new{platform=OS.GetName(),api=RenderingServer.GetCurrentRenderingDriverName(),device=RenderingServer.GetVideoAdapterName(),runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    entryAssemblySha256=ReplayAssemblyHash(typeof(BomberModel).Assembly),entryModuleMvid=typeof(BomberModel).Module.ModuleVersionId.ToString()}});
            BomberModel.MeshBuildObservation=Record;
            try
            {
                foreach(var recipe in recipes)
                {
                    current=recipe.name;Record("begin",new{recipe.builder,arguments=recipe.arguments});
                    using var generated=(ArrayMesh)type.GetMethod(recipe.builder,flags)!.Invoke(null,recipe.arguments)!;
                    byte[] actual=GD.VarToBytes(generated.Get("_surfaces")),cached=GD.VarToBytes(recipe.cached.Get("_surfaces"));
                    bool same=actual.AsSpan().SequenceEqual(cached);
                    string canonicalKey=current switch {"left-eye"=>"eye-left","right-eye"=>"eye-right",_=>current};
                    string? canonicalSha256=BomberMeshLibrary.SharedEnabled?BomberMeshLibrary.CanonicalHash(canonicalKey):null;
                    var result=new{name=current,generatedSha256=ReplayHash(actual),cachedSha256=ReplayHash(cached),identicalToCapturedMesh=same,canonicalSha256};
                    Record("end",result);evidence.Add(result);
                    Check(canonicalSha256 is not null?ReplayHash(cached)==canonicalSha256:same,"captured mesh matches its declared source; fresh procedural differences remain diagnostic: "+current);
                }
                Record("complete",new{meshes=evidence.Count,records=sequence+1,binaryBytes=blob.Position});
            }
            finally{BomberModel.MeshBuildObservation=null;}
        }
        File.WriteAllText(Path.Combine(_output,"mesh-inputs-summary.json"),JsonSerializer.Serialize(new{schemaVersion=1,scope="Fresh builders remain diagnostic; captured canonical geometry verified separately; no graphics qualification",meshSource=BomberMeshLibrary.SharedEnabled?"canonical-mac-resource":"procedural",records=sequence,
            traceSha256=ReplayHash(File.ReadAllBytes(trace)),binarySha256=ReplayHash(File.ReadAllBytes(binary)),meshes=evidence},new JsonSerializerOptions{WriteIndented=true,NewLine="\n"}));
    }
}
