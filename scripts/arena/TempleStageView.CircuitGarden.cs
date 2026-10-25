using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Arena;

public partial class TempleStageView
{
    private void GardenDetails()
    {
        var grass=new ShaderMaterial {Shader=new Shader {Code="""
            shader_type spatial;
            varying vec3 ground_position;
            void vertex(){ground_position=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;}
            void fragment(){
                vec2 p=ground_position.xz;
                float broad=sin(p.x*.83+sin(p.y*.61))*sin(p.y*.94);
                float grain=sin(p.x*31.0+p.y*17.0)*sin(p.y*37.0-p.x*13.0);
                float visible_grain=1.0-smoothstep(.025,.10,length(fwidth(p)));
                ALBEDO=mix(vec3(.24,.39,.19),vec3(.38,.51,.27),broad*.5+.5)+grain*.018*visible_grain;
                ROUGHNESS=.94;
            }
            """}};
        var chalk=_art.Color(new("f4e8b5"));var teal=_art.Color(new("258d8f"));var gold=_art.Color(new("efc65b"));
        // The garden is an academy built into old stonework: open grass, a pale range,
        // a small terrace and a canal. All markings are flush, without hidden collision.
        for(int z=0;z<Depth;z++)for(int x=0;x<Width;x++)
        {
            var cell=_cells[z*Width+x];
            if(cell.GetProperty("k").GetInt32()!=0)continue;
            float y=cell.GetProperty("t").GetSingle()*HeightScale;
            bool lane=z is >=11 and <=14 && x is >=5 and <=17;
            bool path=x is >=10 and <=12;
            if(!lane&&!path)Box(this,new(x+.5f,y+.008f,z+.5f),new(.999f,.022f,.999f),grass);
        }
        // Distance marks serve throwing practice and make the low court legible at battle scale.
        for(int x=6;x<=17;x++)
        {
            Box(this,new(x, .024f,11.15f),new(.035f,.018f,.4f),chalk);
            Box(this,new(x, .024f,14.85f),new(.035f,.018f,.4f),chalk);
        }
        foreach(float z in new[]{10.9f,15.1f})Box(this,new(11.5f,.025f,z),new(13,.018f,.06f),teal);
        // Destination mosaics on the terrace and practice landing dock.
        foreach(var p in new[]{new Vector3(11.5f,2*HeightScale+.025f,1.5f),new Vector3(21.5f,.025f,11.5f)})
        {
            Box(this,p,new(2,.02f,1.6f),teal,.05f);
            Box(this,p+new Vector3(0,.014f,0),new(1.5f,.02f,.07f),gold);
            Box(this,p+new Vector3(0,.014f,0),new(.07f,.02f,1.1f),gold);
        }
        // Original banners grow from existing solid pillars; nothing blocks the lawn.
        foreach(var (x,z) in new[]{(4,5),(4,9),(16,5),(16,9)})
        {
            Box(this,new(x+.5f,1.65f,z+.5f),new(.06f,1.4f,.06f),gold,.01f);
            Box(this,new(x+.78f,2.05f,z+.5f),new(.55f,.5f,.035f),teal,.025f);
            Box(this,new(x+.78f,2.05f,z+.526f),new(.25f,.05f,.015f),chalk);
        }
    }
}
