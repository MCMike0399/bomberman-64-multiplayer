using Godot;
namespace BlastCircuit.Arena.Models;

/// <summary>Fixed arithmetic for the antenna's presentation-only soft limit and basis.</summary>
internal static class BomberPresentationMath
{
    internal static Basis AntennaBasis(float pitch,float roll)
    {
        float x=.5f*Tanh(pitch/.5f),z=.5f*Tanh(roll/.5f);
        var (sx,cx)=SinCosLimited(x);var (sz,cz)=SinCosLimited(z);
        // Godot's default YXZ order, with yaw zero. Supplying columns directly
        // avoids passing identical angles back through platform-native sinf/cosf.
        return new(new(cz,cx*sz,sx*sz),new(-sz,cx*cz,sx*cz),new(0,-sx,cx));
    }

    private static float Tanh(float value)
    {
        if(float.IsNaN(value))return float.NaN;
        double x=value<0?-(double)value:value;
        // tanh(10) already rounds to one in binary32. Compute expm1(2x)
        // without cancellation, using a fixed series and four doublings.
        if(x>=10)return value<0?-1:1;
        double reduced=x/8,term=reduced,sum=reduced;
        for(int n=2;n<=24;n++){term*=reduced/n;sum+=term;}
        for(int n=0;n<4;n++)sum*=sum+2;
        float result=(float)(sum/(sum+2));
        return value<0?-result:result;
    }

    private static (float sin,float cos) SinCosLimited(float angle)
    {
        // The soft limit bounds this input to [-.5,.5]. Fixed double-precision
        // Taylor arithmetic, rounded once, requires no operating-system libm.
        double x=angle,square=x*x,sine=x,cosine=1,sineTerm=x,cosineTerm=1;
        for(int n=1;n<=9;n++)
        {
            sineTerm*=-square/((2*n)*(2*n+1));sine+=sineTerm;
            cosineTerm*=-square/((2*n-1)*(2*n));cosine+=cosineTerm;
        }
        return ((float)sine,(float)cosine);
    }
}
