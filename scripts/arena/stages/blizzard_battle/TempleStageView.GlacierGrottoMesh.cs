using Godot;
using BlastCircuit.Rendering;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private const float GrottoCenterX=-3.75f,GrottoBack=1.5f,GrottoFront=8.5f;
    private const float GrottoPatchLeft=-7f,GrottoPatchRight=-.5f;
    private const int GrottoSections=14,GrottoArchSteps=24;

    private static float GlacierFieldHeight(float x,float z,int width,int depth)
    {
        float d=Math.Max(Math.Max(-x,x-width),Math.Max(-z,z-depth));
        float Smooth(float a,float b,float t){t=Math.Clamp(t,0,1);return Mathf.Lerp(a,b,t*t*(3-2*t));}
        float y=d<.7f?Smooth(-.26f,.31f,d/.7f):d<1.65f?Smooth(.31f,.62f,(d-.7f)/.95f):
            d<2.7f?Smooth(.62f,.16f,(d-1.65f)/1.05f):Smooth(.16f,-.30f,(d-2.7f)/.9f);
        // Three broad, low drifts belong to the same field, including on Low.
        foreach(var (cx,cz,rx,rz,h) in new[]{(-4.8f,depth-4f,3.7f,4.2f,.65f),
            (width+4.5f,7.5f,3.6f,5f,.75f),(width*.68f,-4.5f,5f,3.7f,.8f)})
        {
            float q=(x-cx)*(x-cx)/(rx*rx)+(z-cz)*(z-cz)/(rz*rz);
            float bump=Math.Max(0,1-q);y+=h*bump*bump;
        }
        // The lee pond sits on a deliberately flattened depression, not through a drift.
        float pond=new Vector2(x+4.2f,z-11f).Length();
        return Mathf.Lerp(y,-.26f,1-Smooth(0,1,(pond-1.6f)/.8f));
    }

    private static Vector3 GlacierFieldNormal(float x,float z,int width,int depth)
    {
        const float e=.025f;
        return new Vector3(-(GlacierFieldHeight(x+e,z,width,depth)-GlacierFieldHeight(x-e,z,width,depth))/(2*e),1,
            -(GlacierFieldHeight(x,z+e,width,depth)-GlacierFieldHeight(x,z-e,width,depth))/(2*e)).Normalized();
    }

    private static (float Width,float Height) GrottoProfile(float t,bool inner)
    {
        float swell=PresentationMath.Sin(Math.Clamp(t,0,1)*Mathf.Pi);
        return inner?(1.22f+.10f*swell,1.65f+.35f*swell):(2.7f+.35f*swell,2.6f+1.4f*swell);
    }

    private static Vector3 GrottoSection(float t,float theta,bool inner,int width,int depth)
    {
        var (sin,cos)=PresentationMath.SinCos(theta);var profile=GrottoProfile(t,inner);
        float x=GrottoCenterX+profile.Width*cos,z=Mathf.Lerp(GrottoBack,GrottoFront,t);
        // A quartic shoulder meets the field with a horizontal tangent: a modelled fillet.
        float lift=inner?sin:sin*sin*sin*sin;
        return new(x,GlacierFieldHeight(x,z,width,depth)+profile.Height*lift,z);
    }

    private static Vector3 GrottoNormal(float t,float theta,bool inner,int width,int depth)
    {
        if(!inner&&(theta<.0001f||theta>Mathf.Pi-.0001f))
        {var foot=GrottoSection(t,theta,false,width,depth);return GlacierFieldNormal(foot.X,foot.Z,width,depth);}
        const float e=.001f;
        var along=GrottoSection(Math.Min(1,t+e),theta,inner,width,depth)-GrottoSection(Math.Max(0,t-e),theta,inner,width,depth);
        var across=GrottoSection(t,Math.Min(Mathf.Pi,theta+e),inner,width,depth)-GrottoSection(t,Math.Max(0,theta-e),inner,width,depth);
        var normal=across.Cross(along).Normalized();return inner?-normal:normal;
    }

    private static Vector3 GrottoLip(float end,float theta,float blend,int width,int depth)
    {
        var a=GrottoSection(end,theta,false,width,depth);var b=GrottoSection(end,theta,true,width,depth);
        var p=a.Lerp(b,blend);p.Z+=(end==1?1:-1)*PresentationMath.Sin(blend*Mathf.Pi)*.22f;return p;
    }

    private static ArrayMesh GlacierGrottoMesh(int width,int depth,Material snow,Material ice)
    {
        var powder=new SurfaceTool();powder.Begin(Mesh.PrimitiveType.Triangles);
        var frozen=new SurfaceTool();frozen.Begin(Mesh.PrimitiveType.Triangles);
        var icicles=new SurfaceTool();icicles.Begin(Mesh.PrimitiveType.Triangles);
        float InnerRoof(float x,float z)
        {
            float t=(z-GrottoBack)/(GrottoFront-GrottoBack);var profile=GrottoProfile(t,true);
            float q=(x-GrottoCenterX)/profile.Width;
            return GlacierFieldHeight(x,z,width,depth)+profile.Height*MathF.Sqrt(Math.Max(0,1-q*q));
        }
        void Triangle(SurfaceTool tool,Vector3 a,Vector3 b,Vector3 c,Vector3 na,Vector3 nb,Vector3 nc,bool hero)
        {
            var cross=(b-a).Cross(c-a);if(cross.LengthSquared()<.0000000001f)return;
            if(cross.Dot(na+nb+nc)>0){(b,c)=(c,b);(nb,nc)=(nc,nb);}
            foreach(var (p,n) in new[]{(a,na),(b,nb),(c,nc)})
            {tool.SetNormal(n);tool.SetUV(new(p.X,p.Z));tool.SetUV2(new(hero?1:0,ReferenceEquals(tool,icicles)&&Math.Abs(p.Y-InnerRoof(p.X,p.Z))<.00001f?1:0));tool.AddVertex(p);}
        }
        void Quad(SurfaceTool tool,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 na,Vector3 nb,Vector3 nc,Vector3 nd,bool hero)
        {Triangle(tool,a,b,c,na,nb,nc,hero);Triangle(tool,a,c,d,na,nc,nd,hero);}
        Vector3 Field(float x,float z)=>new(x,GlacierFieldHeight(x,z,width,depth),z);
        Vector3 FieldNormal(Vector3 p)=>GlacierFieldNormal(p.X,p.Z,width,depth);

        // One continuous field/berm; replace its grotto patch with lofted shoulders and collars.
        const float step=.5f;
        for(float z=-9;z<depth+9;z+=step)for(float x=-9;x<width+9;x+=step)
        {
            if(x>=0&&x<width&&z>=0&&z<depth)continue;
            if(x>=GrottoPatchLeft&&x<GrottoPatchRight&&z>=GrottoBack&&z<GrottoFront)continue;
            var a=Field(x,z);var b=Field(x+step,z);var c=Field(x+step,z+step);var d=Field(x,z+step);
            Quad(powder,a,b,c,d,FieldNormal(a),FieldNormal(b),FieldNormal(c),FieldNormal(d),false);
        }
        for(int section=0;section<GrottoSections;section++)
        {
            float t0=section/(float)GrottoSections,t1=(section+1f)/GrottoSections;
            foreach(bool inside in new[]{false,true})for(int arc=0;arc<GrottoArchSteps;arc++)
            {
                float a0=arc*Mathf.Pi/GrottoArchSteps,a1=(arc+1)*Mathf.Pi/GrottoArchSteps;
                var a=GrottoSection(t0,a0,inside,width,depth);var b=GrottoSection(t1,a0,inside,width,depth);
                var c=GrottoSection(t1,a1,inside,width,depth);var d=GrottoSection(t0,a1,inside,width,depth);
                Quad(inside?frozen:powder,a,b,c,d,GrottoNormal(t0,a0,inside,width,depth),GrottoNormal(t1,a0,inside,width,depth),
                    GrottoNormal(t1,a1,inside,width,depth),GrottoNormal(t0,a1,inside,width,depth),true);
            }
            foreach(int side in new[]{-1,1})
            {
                var a=GrottoSection(t0,side<0?Mathf.Pi:0,false,width,depth);
                var b=GrottoSection(t1,side<0?Mathf.Pi:0,false,width,depth);
                var c=Field(side<0?GrottoPatchLeft:GrottoPatchRight,b.Z);var d=Field(c.X,a.Z);
                Quad(powder,a,b,c,d,FieldNormal(a),FieldNormal(b),FieldNormal(c),FieldNormal(d),false);
            }
            // Real ice floor under the open vault, sharing its side-wall contact vertices.
            const int floorSteps=12;
            for(int i=0;i<floorSteps;i++)
            {
                Vector3 Floor(float t,int n)
                {
                    float span=GrottoProfile(t,true).Width,x=GrottoCenterX+Mathf.Lerp(-span,span,n/(float)floorSteps);
                    return Field(x,Mathf.Lerp(GrottoBack,GrottoFront,t));
                }
                var a=Floor(t0,i);var b=Floor(t1,i);var c=Floor(t1,i+1);var d=Floor(t0,i+1);
                Quad(frozen,a,b,c,d,FieldNormal(a),FieldNormal(b),FieldNormal(c),FieldNormal(d),true);
            }
        }
        foreach(float end in new[]{0f,1f})
        {
            for(int band=0;band<6;band++)for(int arc=0;arc<GrottoArchSteps;arc++)
            {
                float u0=band/6f,u1=(band+1f)/6f,theta0=arc*Mathf.Pi/GrottoArchSteps,theta1=(arc+1)*Mathf.Pi/GrottoArchSteps;
                Vector3 N(float theta,float u)
                {
                    if(u<.0001f)return GrottoNormal(end,theta,false,width,depth);
                    if(u>.9999f)return GrottoNormal(end,theta,true,width,depth);
                    const float e=.001f;
                    var tangent=GrottoLip(end,Math.Min(Mathf.Pi,theta+e),u,width,depth)-GrottoLip(end,Math.Max(0,theta-e),u,width,depth);
                    var across=GrottoLip(end,theta,Math.Min(1,u+e),width,depth)-GrottoLip(end,theta,Math.Max(0,u-e),width,depth);
                    var n=tangent.Cross(across).Normalized()*(end==1?1:-1);
                    return n.LengthSquared()>.5f?n:new Vector3(0,0,end==1?1:-1);
                }
                var a=GrottoLip(end,theta0,u0,width,depth);var b=GrottoLip(end,theta1,u0,width,depth);
                var c=GrottoLip(end,theta1,u1,width,depth);var d=GrottoLip(end,theta0,u1,width,depth);
                Quad(band<3?powder:frozen,a,b,c,d,N(theta0,u0),N(theta1,u0),N(theta1,u1),N(theta0,u1),true);
            }
        }
        // Four rounded icicles start exactly at the carved lip, with a broad seated collar.
        for(int tooth=0;tooth<4;tooth++)
        {
            float localX=-.82f+tooth*.55f,span=GrottoProfile(1,true).Width;
            float theta=Mathf.Pi/2-PresentationMath.Atan(localX/MathF.Sqrt(span*span-localX*localX));
            float toothT=(GrottoFront-.24f-GrottoBack)/(GrottoFront-GrottoBack);
            var anchor=GrottoSection(toothT,theta,true,width,depth);float h=new[]{.52f,.76f,.64f,.46f}[tooth];
            (float Y,float Radius)[] profile=[(0,.19f),(-.09f,.22f),(-h*.28f,.16f),(-h*.78f,.085f),(-h,0)];
            const int sides=12;
            (Vector3 P,Vector3 N) At(int ring,int side)
            {
                var (sin,cos)=PresentationMath.SinCos((side%sides)*Mathf.Tau/sides);
                var p=profile[ring];var before=profile[Math.Max(0,ring-1)];var after=profile[Math.Min(profile.Length-1,ring+1)];
                var n=p.Radius==0?Vector3.Down:new Vector3(cos*(before.Y-after.Y),after.Radius-before.Radius,sin*(before.Y-after.Y)).Normalized();
                var point=anchor+new Vector3(cos*p.Radius,p.Y,sin*p.Radius);
                float t=(point.Z-GrottoBack)/(GrottoFront-GrottoBack);var roof=GrottoProfile(t,true);
                float q=(point.X-GrottoCenterX)/roof.Width;
                point.Y=GlacierFieldHeight(point.X,point.Z,width,depth)+roof.Height*MathF.Sqrt(Math.Max(0,1-q*q))+p.Y;
                return (point,n);
            }
            for(int ring=0;ring<profile.Length-1;ring++)for(int side=0;side<sides;side++)
            {
                var a=At(ring,side);var b=At(ring,side+1);var c=At(ring+1,side+1);var d=At(ring+1,side);
                Quad(icicles,a.P,b.P,c.P,d.P,a.N,b.N,c.N,d.N,true);
            }
        }
        powder.Index();frozen.Index();icicles.Index();var mesh=powder.Commit();frozen.Commit(mesh);icicles.Commit(mesh);
        mesh.SurfaceSetMaterial(0,snow);mesh.SurfaceSetMaterial(1,ice);mesh.SurfaceSetMaterial(2,ice);return mesh;
    }

    private static float CapturedGrottoRoof(ArrayMesh body,float x,float z)
    {
        var arrays=body.SurfaceGetArrays(0);var v=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var ids=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        float highest=float.NegativeInfinity;
        for(int i=0;i<ids.Length;i+=3)
        {
            var a=v[ids[i]];var b=v[ids[i+1]];var c=v[ids[i+2]];
            float denominator=(b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z);if(Math.Abs(denominator)<.0000001f)continue;
            float u=((b.Z-c.Z)*(x-c.X)+(c.X-b.X)*(z-c.Z))/denominator;
            float w=((c.Z-a.Z)*(x-c.X)+(a.X-c.X)*(z-c.Z))/denominator,third=1-u-w;
            if(u>=-.00001f&&w>=-.00001f&&third>=-.00001f)highest=Math.Max(highest,u*a.Y+w*b.Y+third*c.Y);
        }
        if(!float.IsFinite(highest))throw new InvalidDataException("Crown has no captured snow support");return highest;
    }

    private static ArrayMesh GrottoCrystalMesh(ArrayMesh body,int width,int depth)
    {
        const int sides=7;var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        float centerZ=(GrottoBack+GrottoFront)/2,crown=CapturedGrottoRoof(body,GrottoCenterX,centerZ);
        Vector3 At(int ring,int side)
        {
            float[] radius=[.40f,.55f,.34f];float[] height=[0,.15f,.75f];
            var (sin,cos)=PresentationMath.SinCos((side%sides)*Mathf.Tau/sides);
            float h=height[ring],x=cos*radius[ring]+h*.20f,z=sin*radius[ring]-h*.08f;
            return new(x,CapturedGrottoRoof(body,GrottoCenterX+x,centerZ+z)-crown+h,z);
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c)
        {
            var n=(c-a).Cross(b-a).Normalized();if(n.Dot((a+b+c)/3-new Vector3(0,.35f,0))<0){(b,c)=(c,b);n=-n;}
            foreach(var p in new[]{a,b,c}){surface.SetNormal(n);surface.SetUV(new(p.X,p.Y));surface.AddVertex(p);}
        }
        for(int side=0;side<sides;side++)
        {
            for(int ring=0;ring<2;ring++){var a=At(ring,side);var b=At(ring,side+1);var c=At(ring+1,side+1);var d=At(ring+1,side);Triangle(a,b,c);Triangle(a,c,d);}
            Triangle(At(2,side),At(2,side+1),new(.27f,1.35f,-.108f));
        }
        surface.Index();return surface.Commit();
    }
}
