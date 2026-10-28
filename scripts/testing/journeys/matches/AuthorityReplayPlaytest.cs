using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bomber.Game.Matches;
using Bomber.Game.Online;
using Godot;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    // Executes the actual loaded authority assembly. This is observable-output
    // evidence, not a complete private-state or PRNG-checkpoint comparison.
    private Task AuthorityReplay()
    {
        using var resource=Godot.FileAccess.Open("res://assets/testing/authority-replay.json",Godot.FileAccess.ModeFlags.Read)
            ??throw new IOException("Missing authority replay recipe");
        string recipeText=resource.GetAsText();
        using var recipe=JsonDocument.Parse(recipeText);
        var specification=recipe.RootElement;
        Check(specification.GetProperty("schemaVersion").GetInt32()==1,"authority replay recipe schema is supported");
        var cases=specification.GetProperty("cases").EnumerateArray().ToArray();
        Check(cases.Length is >0 and <=4,"authority replay corpus is bounded");
        var jsonOptions=new JsonSerializerOptions{PropertyNameCaseInsensitive=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
        string path=Path.Combine(_output,"authority-replay.jsonl.gz");
        int dataRecords=0,totalTicks=0;var evidence=new List<object>();
        using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using(var file=File.Create(path))
        using(var gzip=new GZipStream(file,CompressionLevel.Optimal))
        {
            void Write(object record,bool data=true)
            {
                using var bytes=new MemoryStream();
                using(var writer=new Utf8JsonWriter(bytes))ReplayCanonical(writer,JsonSerializer.SerializeToElement(record));
                byte[] line=bytes.ToArray();gzip.Write(line);gzip.WriteByte((byte)'\n');
                if(data){digest.AppendData(line);digest.AppendData([(byte)'\n']);dataRecords++;}
            }
            Write(new{kind="header",schemaVersion=1,shared=new{
                recipeId=specification.GetProperty("id").GetString(),recipeSha256=ReplayHash(Encoding.UTF8.GetBytes(recipeText)),
                sourceIdentity=OnlineProtocol.SourceRevision,engineIdentity=Engine.GetVersionInfo()["hash"].AsString(),
                release=new{version=OnlineProtocol.ReleaseVersion,build=OnlineProtocol.ReleaseBuild,protocol=OnlineProtocol.Revision,token=OnlineProtocol.WireRevision},
                cases=cases.Select(c=>new{id=c.GetProperty("id").GetString(),lastTick=c.GetProperty("lastTick").GetInt32()}),
                coverage="Ordered tick-indexed inputs and observable messages after LocalMatch constructor priming; not hidden-state equivalence.",
                canaries="Independent System.Random(seed) and MathF bit probes; not the authority PRNG state."
            },provenance=new{platform=OS.GetName(),os=RuntimeInformation.OSDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),
                runtime=RuntimeInformation.FrameworkDescription,engine=Engine.GetVersionInfo()["string"].AsString(),
                authorityAssemblySha256=ReplayAssemblyHash(typeof(LocalMatch).Assembly),authorityModuleMvid=typeof(LocalMatch).Module.ModuleVersionId.ToString(),
                entryAssemblySha256=ReplayAssemblyHash(typeof(PlaytestDriver).Assembly),entryModuleMvid=typeof(PlaytestDriver).Module.ModuleVersionId.ToString(),
                assemblyBinding="Loaded MVID matched to an inventoried on-disk module; not cryptographic process attestation."}},false);
            foreach(var item in cases)
            {
                string id=item.GetProperty("id").GetString()!;int lastTick=item.GetProperty("lastTick").GetInt32();
                Check(lastTick is >=120 and <=900,"replay tick budget is bounded: "+id);
                var options=item.GetProperty("options").Deserialize<MatchOptions>(jsonOptions)??throw new InvalidDataException("Missing replay options");
                var changes=item.GetProperty("inputs").EnumerateArray().ToArray();
                int previous=2;
                foreach(var change in changes)
                {
                    int tick=change.GetProperty("tick").GetInt32(),slot=change.GetProperty("slot").GetInt32();
                    if(tick<=2||tick<previous||tick>lastTick||slot<0||slot>=options.HumanPlayers)throw new InvalidDataException("Invalid ordered replay intent: "+id);
                    previous=tick;
                }
                var match=new LocalMatch(options);var messages=new List<JsonElement>();
                match.StructuredMessage+=message=>messages.Add(message);
                var inputs=new PlayerInput[options.HumanPlayers];int next=0,snapshots=0,bombs=0;bool moved=false;
                var initial=match.StateMessage!.Value.GetProperty("d").GetProperty("players").EnumerateArray()
                    .ToDictionary(p=>p.GetProperty("id").GetString()!,p=>(p.GetProperty("x").GetSingle(),p.GetProperty("z").GetSingle()));
                Write(new{kind="case-start",id,initialTick=match.TickNumber,lastTick,options,
                    stage=match.StageMessage,snapshot=match.StateMessage});
                var random=new Random(options.Seed);
                while(match.TickNumber<lastTick)
                {
                    long tick=match.TickNumber+1;var delivered=new List<object>();var changed=new HashSet<int>();messages.Clear();
                    void Send(int slot,PlayerInput input)
                    {
                        inputs[slot]=input;match.SetInput(slot,input);
                        delivered.Add(new{slot,input=new{x=input.X,z=input.Z,a=input.A,b=input.B,r=input.R,aim=input.Aim,stop=input.Stop,
                            tx=input.AimTarget?.X,tz=input.AimTarget?.Y,tr=input.AimRelative}});
                    }
                    while(next<changes.Length&&changes[next].GetProperty("tick").GetInt64()==tick)
                    {
                        var change=changes[next++];int slot=change.GetProperty("slot").GetInt32();changed.Add(slot);
                        Send(slot,ReplayInput(change.GetProperty("input")));
                    }
                    for(int slot=0;slot<inputs.Length;slot++)if(!changed.Contains(slot))Send(slot,inputs[slot]);
                    match.Tick();
                    if(match.TickNumber!=tick)throw new InvalidDataException("Replay ended before its declared tick budget: "+id);
                    foreach(var message in messages.Where(m=>m.GetProperty("t").GetString()=="state"))
                    {
                        snapshots++;var state=message.GetProperty("d");bombs+=state.GetProperty("bombs").GetArrayLength();
                        foreach(var actor in state.GetProperty("players").EnumerateArray())
                            if(initial.TryGetValue(actor.GetProperty("id").GetString()!,out var start)&&
                                Math.Abs(actor.GetProperty("x").GetSingle()-start.Item1)+Math.Abs(actor.GetProperty("z").GetSingle()-start.Item2)>.12f)moved=true;
                    }
                    float operand=tick*.125f;
                    Write(new{kind="tick",id,tick,inputs=delivered,messages,latestSnapshotTick=match.StateMessage?.GetProperty("k").GetInt64(),
                        canary=new{operand=ReplayBits(operand),sin=ReplayBits(MathF.Sin(operand)),cos=ReplayBits(MathF.Cos(operand)),
                            atan2=ReplayBits(MathF.Atan2(tick%17-8,tick%11-5)),randomInt=random.Next(-1000,1000),
                            randomDouble=BitConverter.DoubleToInt64Bits(random.NextDouble()).ToString("x16",CultureInfo.InvariantCulture)}});
                    totalTicks++;
                }
                Check(next==changes.Length&&snapshots>0,"every replay input and expected observable stream completed: "+id);
                Check(moved,"replay includes actual actor displacement: "+id);
                if(item.GetProperty("requireBomb").GetBoolean())Check(bombs>0,"replay includes an actual authoritative bomb: "+id);
                Write(new{kind="case-end",id,lastTick=match.TickNumber,snapshots,bombObservations=bombs,moved});
                evidence.Add(new{id,lastTick=match.TickNumber,snapshots,bombObservations=bombs,moved});
            }
            Write(new{kind="complete",caseCount=cases.Length,totalTicks,dataRecords,dataSha256=Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant()},false);
        }
        File.WriteAllText(Path.Combine(_output,"authority-replay-receipt.json"),JsonSerializer.Serialize(new{
            schemaVersion=1,scope="Observable replay only; runtime/package binding is validated externally.",source=OnlineProtocol.SourceRevision,
            file=Path.GetFileName(path),sha256=ReplayHash(File.ReadAllBytes(path)),cases=evidence,totalTicks,dataRecords},new JsonSerializerOptions{WriteIndented=true}));
        Event("authority-replay-complete",new{cases=cases.Length,totalTicks,dataRecords});
        return Task.CompletedTask;
    }
    private static string ReplayHash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string ReplayAssemblyHash(Assembly assembly)
    {
        // Godot loads managed modules from streams, so Assembly.Location can be
        // empty even when its exact DLL is present in the installed package.
        string executable=Path.GetDirectoryName(OS.GetExecutablePath())!;
        var roots=new HashSet<string>(StringComparer.Ordinal){AppContext.BaseDirectory,executable};
        if(OS.HasFeature("editor"))roots.Add(ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin"));
        if(Directory.GetParent(executable) is {Name:"Contents"} contents)roots.Add(Path.Combine(contents.FullName,"Resources"));
        var candidates=new HashSet<string>(StringComparer.Ordinal);
        if(File.Exists(assembly.Location))candidates.Add(assembly.Location);
        foreach(string root in roots.Where(Directory.Exists))
            foreach(string path in Directory.EnumerateFiles(root,assembly.GetName().Name+".dll",SearchOption.AllDirectories))candidates.Add(path);
        var hashes=new HashSet<string>(StringComparer.Ordinal);
        foreach(string path in candidates)
        {
            using var file=File.OpenRead(path);using var pe=new PEReader(file);
            if(!pe.HasMetadata)continue;
            var metadata=pe.GetMetadataReader();
            if(metadata.GetGuid(metadata.GetModuleDefinition().Mvid)==assembly.ManifestModule.ModuleVersionId)
                hashes.Add(ReplayHash(File.ReadAllBytes(path)));
        }
        if(hashes.Count!=1)throw new IOException("Cannot uniquely bind loaded module "+assembly.GetName().Name+" MVID "+assembly.ManifestModule.ModuleVersionId);
        return hashes.Single();
    }
    private static string ReplayBits(float value)=>BitConverter.SingleToInt32Bits(value).ToString("x8",CultureInfo.InvariantCulture);
    private static PlayerInput ReplayInput(JsonElement value)
    {
        float Number(string key)=>value.TryGetProperty(key,out var number)?number.GetSingle():0;
        bool Flag(string key)=>value.TryGetProperty(key,out var flag)&&flag.GetBoolean();
        System.Numerics.Vector2? target=value.TryGetProperty("tx",out var x)&&value.TryGetProperty("tz",out var z)?new(x.GetSingle(),z.GetSingle()):null;
        var input=new PlayerInput(Number("x"),Number("z"),Flag("a"),Flag("b"),Flag("r"),Flag("aim"),Flag("stop"),target,Flag("tr"));
        if(!float.IsFinite(input.X)||!float.IsFinite(input.Z)||Math.Abs(input.X)>1||Math.Abs(input.Z)>1)throw new InvalidDataException("Invalid replay movement");
        return input;
    }
    private static void ReplayCanonical(Utf8JsonWriter writer,JsonElement value)
    {
        switch(value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();foreach(var property in value.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal))
                {writer.WritePropertyName(property.Name);ReplayCanonical(writer,property.Value);}writer.WriteEndObject();break;
            case JsonValueKind.Array:
                writer.WriteStartArray();foreach(var item in value.EnumerateArray())ReplayCanonical(writer,item);writer.WriteEndArray();break;
            case JsonValueKind.Number:writer.WriteRawValue(value.GetRawText());break;
            default:value.WriteTo(writer);break;
        }
    }
}
