using System.Text.Json;
using Godot;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private static IEnumerable<Material> VisualMaterials(Node root)
    {
        foreach(var node in AuditDescendants(root).Prepend(root).OfType<GeometryInstance3D>())
        {
            if(node.MaterialOverride is {} material)yield return material;
            if(node is MeshInstance3D mesh&&mesh.Mesh is {} geometry)
                for(int surface=0;surface<geometry.GetSurfaceCount();surface++)
                    if((mesh.MaterialOverride??mesh.GetSurfaceOverrideMaterial(surface)??geometry.SurfaceGetMaterial(surface)) is {} active)yield return active;
            if(node is MultiMeshInstance3D multi&&multi.Multimesh?.Mesh is {} instances)
                for(int surface=0;surface<instances.GetSurfaceCount();surface++)
                    if((multi.MaterialOverride??instances.SurfaceGetMaterial(surface)) is {} active)yield return active;
        }
    }
    private object VisualResourceInventory(Node root,string label)
    {
        var resources=new List<Dictionary<string,object?>>();var ids=new Dictionary<ulong,int>();var unsupported=new List<string>();
        using var blob=File.Create(Path.Combine(_output,$"parity-{label}-inventory.bin"));
        object Binary(byte[] bytes)
        {
            long offset=blob.Position;blob.Write(bytes);
            return new{offset,length=bytes.Length,sha256=ReplayHash(bytes)};
        }
        object? Value(Variant value)
        {
            if(value.VariantType==Variant.Type.Nil)return null;
            if(value.VariantType==Variant.Type.Object)
            {
                var item=value.AsGodotObject();if(item is null)return null;
                if(item is not Resource resource){unsupported.Add(item.GetClass());return new{unsupported=item.GetClass()};}
                if(ids.TryGetValue(resource.GetInstanceId(),out int seen))return new{reference=seen};
                int id=resources.Count;ids.Add(resource.GetInstanceId(),id);
                var record=new Dictionary<string,object?>{{"id",id},{"type",resource.GetClass()},{"path",resource.ResourcePath}};resources.Add(record);
                if(resource is PrimitiveMesh)unsupported.Add("runtime primitive mesh: "+resource.GetClass());
                // Per-instance setters leave the headless buffer empty; only a complete
                // buffer makes instance transforms and colors comparable inputs.
                if(resource is MultiMesh multimesh&&multimesh.Buffer.Length!=multimesh.InstanceCount*
                   ((multimesh.TransformFormat==MultiMesh.TransformFormatEnum.Transform3D?12:8)+(multimesh.UseColors?4:0)+(multimesh.UseCustomData?4:0)))
                    unsupported.Add("unretained MultiMesh instance buffer: "+multimesh.Buffer.Length+" floats for "+multimesh.InstanceCount+" instances");
                if(resource is Texture2D texture)
                {
                    using var image=texture.GetImage();
                    if(image is null||image.IsEmpty()){unsupported.Add("unreadable texture:"+resource.ResourcePath);}
                    else record["image"]=new{width=image.GetWidth(),height=image.GetHeight(),format=image.GetFormat().ToString(),
                        mipmaps=image.GetMipmapCount(),compressed=image.IsCompressed(),data=Binary(image.GetData())};
                }
                // Retain actual packed submission data, including generated primitives.
                // A decoded SurfaceGetArrays result is not the original packed bytes.
                if(resource is Mesh mesh)
                    record["packedSurfaces"]=Enumerable.Range(0,mesh.GetSurfaceCount()).Select(surface=>
                    {
                        var packed=RenderingServer.MeshGetSurface(mesh.GetRid(),surface);
                        if(packed.ContainsKey("material"))
                        {
                            var material=mesh.SurfaceGetMaterial(surface);
                            if(material is null||material.GetRid()!=packed["material"].AsRid())
                                unsupported.Add("unbound packed mesh material");
                            else packed["material"]=material;
                        }
                        return new{surface,data=Value(packed)};
                    }).ToArray();
                record["properties"]=Properties(resource);
                return new{reference=id};
            }
            if(value.VariantType==Variant.Type.Array)return value.AsGodotArray().Select(Value).ToArray();
            if(value.VariantType==Variant.Type.Dictionary)
            {
                var dictionary=value.AsGodotDictionary();
                return dictionary.Keys.OrderBy(key=>GD.VarToStr(key),StringComparer.Ordinal)
                    .Select(key=>new{key=Value(key),value=Value(dictionary[key])}).ToArray();
            }
            if(value.VariantType==Variant.Type.Rid){unsupported.Add("stored RID without resource binding");return new{unsupported="RID"};}
            // Godot's Variant encoding retains the actual packed arrays, float
            // bits, colors, transforms and property types; no numeric rounding.
            return new{type=value.VariantType.ToString(),data=Binary(GD.VarToBytes(value))};
        }
        object Properties(GodotObject item)
        {
            var properties=new List<object>();
            foreach(var property in item.GetPropertyList().OrderBy(p=>p["name"].AsString(),StringComparer.Ordinal))
            {
                string name=property["name"].AsString();
                var usage=(PropertyUsageFlags)property["usage"].AsInt64();
                if((usage&PropertyUsageFlags.Storage)==0||name is "script" or "name" or "owner" or "resource_scene_unique_id")continue;
                properties.Add(new{name,value=Value(item.Get(name))});
            }
            return properties;
        }
        var nodes=new List<object>();
        void Visit(Node node,string path)
        {
            if(node is Node3D or WorldEnvironment)
                nodes.Add(new{path,type=node.GetType().FullName,properties=Properties(node)});
            var children=node.GetChildren();for(int i=0;i<children.Count;i++)Visit(children[i],path+"/"+i);
        }
        Visit(root,"0");
        var inventory=new{encoding="Godot Variant bytes; actual packed mesh surfaces and storage properties/resource graph; local instance IDs/names are locators only and excluded",
            binary=Path.GetFileName(blob.Name),nodes,resources,unsupported=unsupported.Distinct().Order().ToArray()};
        using(var file=File.Create(Path.Combine(_output,$"parity-{label}-inventory.json")))
            JsonSerializer.Serialize(file,inventory,new JsonSerializerOptions{WriteIndented=true,NewLine="\n"});
        return inventory;
    }
}
