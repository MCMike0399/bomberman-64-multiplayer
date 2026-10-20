using Godot;
namespace BlastCircuit.Rendering;

public static class ArtGeometry
{
    private static readonly Dictionary<string,Mesh> Meshes=[];
    public static MeshInstance3D Box(Node parent,Vector3 position,Vector3 size,Material material,float bevel=0)
    {
        string key=$"box:{size}:{bevel}";
        if(!Meshes.TryGetValue(key,out var mesh))Meshes[key]=mesh=bevel>0?BeveledBox(size,bevel):new BoxMesh {Size=size};
        return Add(parent,position,mesh,material);
    }
    public static MeshInstance3D Sphere(Node parent,Vector3 position,Vector3 scale,Material material)
    {
        const string key="sphere";
        if(!Meshes.TryGetValue(key,out var mesh))Meshes[key]=mesh=new SphereMesh {Radius=.5f,Height=1,RadialSegments=32,Rings=16};
        var node=Add(parent,position,mesh,material);node.Scale=scale;return node;
    }
    public static MeshInstance3D Cylinder(Node parent,Vector3 position,float radius,float height,Material material)
    {
        string key=$"cylinder:{radius}:{height}";
        if(!Meshes.TryGetValue(key,out var mesh))Meshes[key]=mesh=new CylinderMesh {TopRadius=radius,BottomRadius=radius,Height=height,RadialSegments=24};
        return Add(parent,position,mesh,material);
    }
    private static MeshInstance3D Add(Node parent,Vector3 position,Mesh mesh,Material material)
    {
        var node=new MeshInstance3D {Mesh=mesh,Position=position,MaterialOverride=material};parent.AddChild(node);return node;
    }
    private static ArrayMesh BeveledBox(Vector3 size,float bevel)
    {
        Vector3 half=size/2;float b=Mathf.Min(bevel,Mathf.Min(half.X,Mathf.Min(half.Y,half.Z))*.9f);
        Vector3 inner=half-Vector3.One*b;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        void Polygon(params Vector3[] v)
        {
            Vector3 normal=(v[1]-v[0]).Cross(v[2]-v[0]).Normalized();
            if(normal.Dot(v.Aggregate(Vector3.Zero,(sum,p)=>sum+p))<0){Array.Reverse(v);normal=-normal;}
            for(int i=1;i<v.Length-1;i++) foreach(var p in new[]{v[0],v[i+1],v[i]})
            {
                surface.SetNormal(normal);
                surface.SetUV(Mathf.Abs(normal.Y)>.5f?new(p.X/size.X+.5f,p.Z/size.Z+.5f):Mathf.Abs(normal.X)>.5f?new(p.Z/size.Z+.5f,p.Y/size.Y+.5f):new(p.X/size.X+.5f,p.Y/size.Y+.5f));
                surface.AddVertex(p);
            }
        }
        Vector3 V(int axis,float value,int second,float value2,int third,float value3){Vector3 p=default;p[axis]=value;p[second]=value2;p[third]=value3;return p;}
        for(int a=0;a<3;a++)for(int s=-1;s<=1;s+=2)
        {
            int c=(a+1)%3,d=(a+2)%3;
            Polygon(V(a,half[a]*s,c,-inner[c],d,-inner[d]),V(a,half[a]*s,c,inner[c],d,-inner[d]),V(a,half[a]*s,c,inner[c],d,inner[d]),V(a,half[a]*s,c,-inner[c],d,inner[d]));
        }
        for(int a=0;a<3;a++)for(int c=a+1;c<3;c++)for(int sa=-1;sa<=1;sa+=2)for(int sc=-1;sc<=1;sc+=2)
        {
            int d=3-a-c;
            Polygon(V(a,half[a]*sa,c,inner[c]*sc,d,-inner[d]),V(a,inner[a]*sa,c,half[c]*sc,d,-inner[d]),V(a,inner[a]*sa,c,half[c]*sc,d,inner[d]),V(a,half[a]*sa,c,inner[c]*sc,d,inner[d]));
        }
        for(int sx=-1;sx<=1;sx+=2)for(int sy=-1;sy<=1;sy+=2)for(int sz=-1;sz<=1;sz+=2)
            Polygon(new(half.X*sx,inner.Y*sy,inner.Z*sz),new(inner.X*sx,half.Y*sy,inner.Z*sz),new(inner.X*sx,inner.Y*sy,half.Z*sz));
        return surface.Commit();
    }
}
