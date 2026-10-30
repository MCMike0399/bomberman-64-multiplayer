using Godot;
namespace BlastCircuit.Rendering;

/// <summary>Fixed managed arithmetic at measured presentation-only libm seams. Never used by authority.</summary>
internal static class PresentationMath
{
    private const double Pi=3.1415926535897932384626433832795,Tau=2*Pi;
    internal static (float Sin,float Cos) SinCos(float angle)
    {
        if(!float.IsFinite(angle))return (float.NaN,float.NaN);
        double x=angle;x-=Math.Floor(x/Tau+.5)*Tau;double sign=1;
        if(x>Pi/2){x=Pi-x;sign=-1;}else if(x< -Pi/2){x= -Pi-x;sign=-1;}
        double square=x*x,sine=x,cosine=1,st=x,ct=1;
        for(int n=1;n<=9;n++){st*=-square/((2*n)*(2*n+1));sine+=st;ct*=-square/((2*n-1)*(2*n));cosine+=ct;}
        return ((float)sine,(float)(cosine*sign));
    }
    internal static float Sin(float angle)=>SinCos(angle).Sin;
    internal static float Atan(float value)
    {
        if(float.IsNaN(value))return float.NaN;
        double x=Math.Abs((double)value);bool reciprocal=x>1;if(reciprocal)x=1/x;
        double offset=0;if(x>.4142135623730950488){x=(x-1)/(x+1);offset=Pi/4;}
        double square=x*x,term=x,sum=x;
        for(int n=1;n<=24;n++){term*= -square;sum+=term/(2*n+1);}
        double result=offset+sum;if(reciprocal)result=Pi/2-result;
        return (float)(float.IsNegative(value)?-result:result);
    }
    internal static Basis Rotation(Vector3 angles,Vector3? scale=null)
    {
        var (sx,cx)=SinCos(angles.X);var (sy,cy)=SinCos(angles.Y);var (sz,cz)=SinCos(angles.Z);
        Vector3 size=scale??Vector3.One;
        // YXZ columns, with local scale applied exactly once. Do not assign Euler
        // angles afterward: that would regenerate this basis through native libm.
        return new(new Vector3(cy*cz+sy*sx*sz,cx*sz,-sy*cz+cy*sx*sz)*size.X,
            new Vector3(-cy*sz+sy*sx*cz,cx*cz,sy*sz+cy*sx*cz)*size.Y,
            new Vector3(sy*cx,-sx,cy*cx)*size.Z);
    }
    internal static Basis Relief(int dx,int dz)
    {
        // Preserve the existing Mac-authored rear-facing float angle. Apple atan2
        // rounds this axis one step below the other native libraries.
        float yaw=dz<0?3.1415925f:dx>0?Mathf.Pi/2:dx<0?-Mathf.Pi/2:0;
        return Rotation(new(0,yaw,0));
    }
}
