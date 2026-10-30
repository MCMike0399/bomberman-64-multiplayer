using Godot;
namespace BlastCircuit.Arena.Models;

/// <summary>
/// Comic mishap poses layered over the accepted action state: a dazed flop on the back,
/// a backflip through impact/throw flights and a frantic struggle while held overhead.
/// Bomberman 64 informs the timing and gags; geometry and proportions stay this model's.
/// </summary>
public partial class BomberModel
{
    private const float StandingStarHeight=1.62f;
    private const int SpiralFrames=12;
    private static readonly Dictionary<(float X,int Frame),ArrayMesh> SpiralMeshes=[];
    private static readonly Dictionary<float,ArrayMesh> SquintMeshes=[];
    private readonly List<MeshInstance3D> _spiralEyes=[],_squintEyes=[];
    private bool _liftingPlayer;
    private float _impactPulse,_recoverPulse,_tumbleSpin,_ghostFade;
    // Convex weights of the three mishap poses; they freeze while recovering so the exit blend never pops.
    private float _dazeWeight=1,_tumbleWeight,_holdWeight;
    public float TumbleSpin=>_tumbleSpin;
    public bool DizzyEyes=>_spiralEyes[0].Visible;
    public bool SquintEyes=>_squintEyes[0].Visible;
    public bool StarsVisible=>_stars.Visible;
    /// <summary>Head and dizzy-star centers in world space, for checking that stars circle the actual head.</summary>
    public Vector3 HeadWorldCenter=>_head.GlobalPosition;
    public Vector3 StarsWorldCenter=>_stars.GlobalPosition;
    /// <summary>Lowest world height of the helmet and torso, used to keep the downed body on the floor or the carrier's gloves.</summary>
    public float BodyLowestPoint=>Mathf.Min(_head.GlobalPosition.Y-.36f*Scale.Y,_upperParts.ToGlobal(new Vector3(0,.53f,0)).Y-.185f*Scale.Y);
    private static float GhostTransparency(float fade)=>Mathf.Lerp(.48f,.9f,fade);

    private void BuildMishapFace(Material dark)
    {
        foreach(float x in new[]{-.134f,.134f})
        {
            for(int frame=0;frame<SpiralFrames;frame++)
                if(!SpiralMeshes.ContainsKey((x,frame)))SpiralMeshes[(x,frame)]=BomberMeshLibrary.Get(BomberMeshLibrary.SpiralKey(x,frame),()=>CreateFaceStroke(x,SpiralPoints(x,frame),.0105f));
            var spiral=new MeshInstance3D {Name="DizzyEye",Mesh=SpiralMeshes[(x,0)],MaterialOverride=dark,Position=new(x,EyeCenterY,0),Visible=false};
            _head.AddChild(spiral);_spiralEyes.Add(spiral);
            // > < squeezed eyes: the left eye points toward the nose, the right mirrors it.
            float side=x<0?1:-1;
            if(!SquintMeshes.TryGetValue(x,out var squint))
                SquintMeshes[x]=squint=BomberMeshLibrary.Get("squint-"+BomberMeshLibrary.Side(x),()=>CreateFaceStroke(x,[new(-.046f*side,.052f),new(.036f*side,0),new(-.046f*side,-.052f)],.014f));
            var squeezed=new MeshInstance3D {Name="SquintEye",Mesh=squint,MaterialOverride=dark,Position=new(x,EyeCenterY,0),Visible=false};
            _head.AddChild(squeezed);_squintEyes.Add(squeezed);
        }
    }
    private static Vector2[] SpiralPoints(float centerX,int frame)
    {
        // An Archimedean swirl, mirrored per eye so both spin outward like cartoon dizziness.
        const int count=44;const float turns=2.6f;
        var points=new Vector2[count];float phase=Mathf.Tau*frame/SpiralFrames,mirror=centerX<0?1:-1;
        for(int i=0;i<count;i++)
        {
            float u=i/(float)(count-1),angle=u*turns*Mathf.Pi+phase,radius=.010f+.062f*u;
            points[i]=new(Mathf.Cos(angle)*radius*mirror,Mathf.Sin(angle)*radius);
        }
        return points;
    }
    /// <summary>A thin ink ribbon laid over the faceplate's curvature, like the painted eyes.</summary>
    private static ArrayMesh CreateFaceStroke(float centerX,Vector2[] points,float halfWidth)
    {
        Vector3 Lay(Vector2 p)
        {
            float x=centerX+p.X,y=EyeCenterY+p.Y;
            float faceRadiusSquared=Mathf.Pow(x/.317f,2)+Mathf.Pow((y-FaceCenterY)/.227f,2);
            return new(p.X,p.Y,FaceHemisphereZ(x,y)+.025f+.03f*faceRadiusSquared);
        }
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        var left=new Vector3[points.Length];var right=new Vector3[points.Length];
        for(int i=0;i<points.Length;i++)
        {
            var tangent=(points[Math.Min(i+1,points.Length-1)]-points[Math.Max(i-1,0)]).Normalized();
            var normal=new Vector2(-tangent.Y,tangent.X)*halfWidth;
            left[i]=Lay(points[i]+normal);right[i]=Lay(points[i]-normal);
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c)
        {
            // Godot's clockwise front faces must look out of the face (+Z).
            if((b-a).Cross(c-a).Z>0){surface.AddVertex(a);surface.AddVertex(c);surface.AddVertex(b);}
            else {surface.AddVertex(a);surface.AddVertex(b);surface.AddVertex(c);}
        }
        for(int i=0;i<points.Length-1;i++){Triangle(left[i],right[i],right[i+1]);Triangle(left[i],right[i+1],left[i+1]);}
        surface.GenerateNormals();surface.Index();return surface.Commit();
    }

    /// <summary>Blends the walking body into the current mishap pose. Called after the action pose, before the head.</summary>
    private void ApplyMishapBody(float dt)
    {
        bool down=_downBlend>0;
        bool tumbling=_airborne&&!_heldVictim&&!_ghost;
        float dazed=_stunned&&!_heldVictim&&!_airborne&&!_ghost?1:0,held=_heldVictim&&!_ghost?1:0,flight=tumbling?1:0;
        if(dazed+held+flight>0)
        {
            float follow=1-Mathf.Exp(-dt*12);
            _dazeWeight+=(dazed-_dazeWeight)*follow;_tumbleWeight+=(flight-_tumbleWeight)*follow;_holdWeight+=(held-_holdWeight)*follow;
        }
        // A backflip through the air, then the spin keeps going until it lands flat on the back.
        if(tumbling)_tumbleSpin-=dt*13;
        else if(_tumbleSpin!=0)
        {
            // Land on the nearest whole turn quickly; the bonk squash covers the last rock.
            float rest=Mathf.Round(_tumbleSpin/Mathf.Tau)*Mathf.Tau;
            _tumbleSpin=Mathf.MoveToward(_tumbleSpin,rest,dt*18);
            // Whole turns are the same pose; unwind them so recovery never spins back.
            if(Mathf.Abs(_tumbleSpin-rest)<.0001f){_tumbleSpin=0;_antennaPitch.Value-=rest;}
        }
        if(down)
        {
            float t=_time,daze=_dazeWeight,tumble=_tumbleWeight,hold=_holdWeight;
            // Dazed: flat on the back like an upturned beetle, rocking, boots paddling the air.
            // Both turn about the belly, so a landing that finishes the backflip never sweeps the boots through the floor.
            var pivot=new Vector3(0,.6f,0);
            var dazeRotation=new Vector3(-1.42f+_tumbleSpin,0,Mathf.Sin(t*2.2f)*.12f);
            var dazePosition=pivot-Basis.FromEuler(dazeRotation)*pivot+new Vector3(0,-.3f,-.09f);
            // Tumbling: a starfish backflip.
            var tumbleRotation=new Vector3(_tumbleSpin,0,Mathf.Sin(t*7)*.3f);
            var tumblePosition=pivot-Basis.FromEuler(tumbleRotation)*pivot+new Vector3(0,.15f,0);
            // Held: lying face-up across the carrier's raised gloves, wriggling about the long axis.
            var holdRotation=new Vector3(-Mathf.Pi/2+Mathf.Sin(t*14)*.16f,0,-Mathf.Pi/2);
            var holdPosition=new Vector3(-.6f,-.1f+Mathf.Abs(Mathf.Sin(t*11))*.035f,0);
            var rotation=dazeRotation*daze+tumbleRotation*tumble+holdRotation*hold;
            var position=dazePosition*daze+tumblePosition*tumble+holdPosition*hold;
            _body.Position=_body.Position.Lerp(position,_downBlend);
            _body.Rotation=_body.Rotation.Lerp(rotation,_downBlend);
            float paddle=Mathf.Sin(t*9),kick=Mathf.Sin(t*15),flail=Mathf.Sin(t*13);
            var leftLeg=new Vector3(-1.15f+paddle*.32f,0,-.12f)*daze+new Vector3(-.4f+kick*.25f,0,-.38f)*tumble+new Vector3(-.95f+kick*.75f,0,-.1f)*hold;
            var rightLeg=new Vector3(-1.15f-paddle*.32f,0,.12f)*daze+new Vector3(-.4f-kick*.25f,0,.38f)*tumble+new Vector3(-.95f-kick*.75f,0,.1f)*hold;
            var leftArm=new Vector3(-.3f+Mathf.Sin(t*2.3f)*.15f,0,-1.35f+Mathf.Sin(t*3.1f)*.12f)*daze+new Vector3(flail*.4f,0,-1.6f)*tumble+new Vector3(-1.2f+flail*1.1f,0,-1.1f)*hold;
            var rightArm=new Vector3(-.3f-Mathf.Sin(t*2.3f)*.15f,0,1.35f-Mathf.Sin(t*3.1f)*.12f)*daze+new Vector3(-flail*.4f,0,1.6f)*tumble+new Vector3(-1.2f+Mathf.Sin(t*13+2)*1.1f,0,1.1f)*hold;
            _leftLeg.Rotation=_leftLeg.Rotation.Lerp(leftLeg,_downBlend);_rightLeg.Rotation=_rightLeg.Rotation.Lerp(rightLeg,_downBlend);
            _leftBoot.Rotation=new(Mathf.Lerp(_leftBoot.Rotation.X,.3f,_downBlend),_leftBoot.Rotation.Y,0);
            _rightBoot.Rotation=new(Mathf.Lerp(_rightBoot.Rotation.X,.3f,_downBlend),_rightBoot.Rotation.Y,0);
            _leftArm.Rotation=_leftArm.Rotation.Lerp(leftArm,_downBlend);_rightArm.Rotation=_rightArm.Rotation.Lerp(rightArm,_downBlend);
        }
        // The bomb's bonk: a quick squash that springs back while the victim flops.
        float impact=_impactPulse*_impactPulse;
        _body.Scale*=new Vector3(1+impact*.22f,1-impact*.28f,1+impact*.22f);
        // Getting up is a little hop with a head-clearing shake.
        _body.Position+=new Vector3(0,Mathf.Sin(_recoverPulse*Mathf.Pi)*.1f,0);
        if(_liftingPlayer)
        {
            // Teetering under a whole player: knees dip and the stance wobbles.
            _body.Position+=new Vector3(0,(-.025f+Mathf.Sin(_time*7)*.02f)*_carryBlend,0);
            _body.Rotation+=new Vector3(0,0,Mathf.Sin(_time*3.3f)*.07f*_carryBlend);
        }
    }
    private void ApplyMishapHead()
    {
        float t=_time,down=_downBlend;
        var loll=new Vector3(0,Mathf.Sin(t*1.9f)*.38f,Mathf.Sin(t*1.3f)*.12f)*_dazeWeight;
        var thrash=new Vector3(Mathf.Sin(t*17)*.08f,Mathf.Sin(t*19)*.32f,0)*_holdWeight;
        _head.Rotation+=(loll+thrash)*down+new Vector3(0,Mathf.Sin(t*32)*.4f*_recoverPulse,0);
    }
    private void ApplyMishapFace()
    {
        bool dizzy=_downBlend>.5f&&_dazeWeight>.5f;
        bool squint=!dizzy&&_downBlend>.5f&&_tumbleWeight+_holdWeight>.5f;
        int frame=(int)(_time*18)%SpiralFrames;
        foreach(var eye in _eyes)eye.Visible=!dizzy&&!squint;
        foreach(var eye in _spiralEyes){eye.Visible=dizzy;if(dizzy)eye.Mesh=SpiralMeshes[(eye.Position.X,frame)];}
        foreach(var eye in _squintEyes)eye.Visible=squint;
        // Dizzy stars circle the actual head wherever the flop leaves it.
        _stars.Visible=_stunned&&!_heldVictim&&!_ghost;
        var standing=new Vector3(0,StandingStarHeight,0);
        var head=(_body.Transform*_upperBody.Transform*_upperParts.Transform*_head.Transform).Origin;
        _stars.Position=standing.Lerp(head+new Vector3(0,.5f,0),_downBlend);
        _stars.Rotation=new(0,_time*3,0);
    }
}
