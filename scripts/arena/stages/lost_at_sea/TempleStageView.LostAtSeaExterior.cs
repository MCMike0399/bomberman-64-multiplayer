using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildLostAtSeaExterior()
    {
        var scenery=_scenery!;
        var sand=_art.Color(new("92b4ad"),.95f);
        var reef=_art.Toy(new("567a80"),.9f);
        // Original rounded reef shelves and branching corals establish the
        // observation window's depth, including real fish beneath its purple glass.
        for(int i=0;i<12;i++)
        {
            float angle=i*Mathf.Tau/12;var (s,c)=PresentationMath.SinCos(angle);
            var p=new Vector3(Width/2f+c*16,-3.2f,Depth/2f+s*16);
            var bed=Prop(scenery,"CoralReef",p,angle);
            Bead(bed,new(0,.1f,0),new(4.2f,.5f,3.1f),sand);
            Bead(bed,new(.5f,.4f,.3f),new(2.1f,.85f,1.7f),reef);
            for(int stem=0;stem<5;stem++)
            {
                float xx=-1.4f+stem*.6f,height=.6f+(stem%3)*.22f;
                var coral=_art.Toy(new(stem%2==0?"df9b87":"b1a1b9"),.85f);
                var stalk=RoundedBox(bed,new(xx,height/2,-.35f),new(.14f,height,.14f),coral);
                stalk.Rotation=new(0,0,(stem-2)*.12f);
                Bead(bed,new(xx,height,-.35f),new(.23f,.24f,.23f),coral);
                foreach(float sign in new[]{-1f,1f})
                {
                    var branch=RoundedBox(bed,new(xx+sign*.15f,height*.65f,-.35f),new(.11f,height*.45f,.11f),coral);
                    branch.Rotation=new(0,0,-sign*.65f);
                }
            }
            if(i%2==0)SeaKelp(scenery,new(p.X,-3.05f,p.Z+1.3f),i);
        }
        for(int school=0;school<7;school++)
        {
            // Four schools swim directly below the glass; the rest animate its surroundings.
            float x=school<4?6+(school%2)*8:school==4?-4:school==5?Width+4:Width/2f;
            float z=school<4?6+(school/2)*8:school==6?-5:Depth*.5f;
            var root=Prop(scenery,"ObservationFishSchool",new(x,-1.35f,z));
            for(int fish=0;fish<5;fish++)
                ObservationFish(root,new((fish-2)*.55f,-(fish%2)*.12f,(fish%3)*.45f),school*.7f+fish);
            int index=school;
            _sceneryMotion.Add(t=>root.Position=new(x+PresentationMath.Sin(t*.32f+index)*1.4f,-1.35f+PresentationMath.Sin(t*.55f+index)*.12f,z+PresentationMath.Sin(t*.24f+index)*.8f));
        }
        // A tiny research bathysphere, deck lights and an off-court buoy make
        // this an inhabited marine outpost without crossing the playable volume.
        var pod=Prop(scenery,"ObservationBathysphere",new(Width+4,-.2f,-3),-.3f);
        var enamel=_art.Toy(new("d3d7bf"),.65f);var brass=_art.Toy(new("b99c62"),.55f);
        Sphere(pod,new(0,.7f,0),new(2.5f,1.8f,2.2f),enamel);
        Sphere(pod,new(0,.85f,1.07f),new(1.35f,1.2f,.2f),brass);
        Sphere(pod,new(0,.85f,1.19f),new(1.05f,.94f,.08f),_art.Toy(new("548e9d"),.35f));
        Box(pod,new(0,-.12f,0),new(2.1f,.2f,1.6f),brass,.08f);
        foreach(float side in new[]{-.85f,.85f})Box(pod,new(side,-.4f,0),new(.14f,.45f,1.8f),brass,.04f);
        Box(pod,new(0,2.1f,0),new(.06f,1.2f,.06f),brass,.01f);
        Bead(pod,new(0,2.75f,0),new(.18f,.18f,.18f),Glow(new("e8bc79"),1.1f));
        _sceneryMotion.Add(t=>pod.Position=new(Width+4,-.2f+PresentationMath.Sin(t*.7f)*.08f,-3));
        SeaKelp(scenery,new(-3,-3.1f,Depth*.35f),1);
        SeaKelp(scenery,new(Width+3,-3.1f,Depth*.7f),3);
    }

    private void ObservationFish(Node3D parent,Vector3 position,float phase)
    {
        var fish=Prop(parent,"ReefFish",position,0,.7f);
        var paint=_art.Toy(new(((int)phase)%2==0?"dfbd78":"91bdc3"),.75f);
        Bead(fish,Vector3.Zero,new(.6f,.28f,.22f),paint);
        Plate(fish,new(-.4f,0,0),[new(0,0),new(-.2f,.18f),new(-.2f,-.18f)],.045f,paint).Name="ReefFishTail";
        Bead(fish,new(.19f,.05f,.1f),new(.045f,.045f,.03f),_art.Color(new("314f60")));
        _sceneryMotion.Add(t=>fish.Basis=PresentationMath.Rotation(new(0,PresentationMath.Sin(t*3.1f+phase)*.15f,PresentationMath.Sin(t*1.3f+phase)*.035f),Vector3.One*.7f));
    }

    private void SeaKelp(Node3D parent,Vector3 position,float phase)
    {
        var root=Prop(parent,"ObservationKelp",position);
        var paint=_art.Foliage(new("5c9d87"));_ambientMaterials.Add(paint);
        for(int stalk=0;stalk<3;stalk++)
        {
            var stem=Prop(root,"KelpStem",new((stalk-1)*.28f,0,0));
            for(int leaf=0;leaf<4;leaf++)
                Bead(stem,new((leaf%2==0?-.12f:.12f),.25f+leaf*.29f,0),new(.33f,.55f,.09f),paint);
            int index=stalk;
            _sceneryMotion.Add(t=>stem.Basis=PresentationMath.Rotation(new(0,0,PresentationMath.Sin(t*.65f+phase+index)*.11f)));
        }
    }
}
