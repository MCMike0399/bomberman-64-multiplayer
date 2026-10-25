using System.Numerics;

namespace Bomber.Game.Simulation;

/// <summary>Launch and flight math shared by authority and the carrier's advisory aim arc.</summary>
public static class BombThrowRules
{
    // Settled rendered hands are ~1.65 world units high (the actor is scaled 1.12).
    public const float LaunchHeight = 1.22f;
    public const float SimulationStep = 1f / 30;
    public const float MinAimRange = 0f;
    public const float MaxAimRange = 9f;
    public const float DefaultAimRange = 4.5f;
    public const float AimCursorSpeed = 6f; // arena cells per second, shared by every input device
    public const float AimFineScale = .25f;
    public const float AimRampSeconds = .3f;
    public const float AimLiftSpeed = 8.5f;
    public const float PillarHeight = .9f;
    public const float CoverHeight = .61f;

    /// <summary>Move the landing cursor in arena X/Z. It can cross the carrier to aim behind them.
    /// Neutral retains intent; radial clamping and normalized input prevent diagonal speed gains.</summary>
    public static ThrowAim AdjustAim(ThrowAim aim,float x,float z,float dt)
    {
        x=float.IsFinite(x)?Math.Clamp(x,-1,1):0;
        z=float.IsFinite(z)?Math.Clamp(z,-1,1):0;
        var length=MathF.Sqrt(x*x+z*z);
        if(length>1){x/=length;z/=length;}
        dt=float.IsFinite(dt)?Math.Clamp(dt,0,.1f):0;
        if(x==0&&z==0)return aim;
        var target=new Vector2(aim.X,aim.Z)*aim.Range+new Vector2(x,z)*AimCursorSpeed*dt;
        var distance=target.Length();
        if(distance<.00001f)return new(aim.X,aim.Z,0);
        return new(target.X/distance,target.Y/distance,Math.Min(distance,MaxAimRange));
    }

    /// <summary>Range is the landing distance on level ground; elevation and combat can change it.
    /// Pumps affect the blast, never this deliberate range. Uses the authority's discrete flight clock.</summary>
    public static Vector3 AimedVelocity(float directionX,float directionZ,float range,float motionRate=1,int pumps=0)
    {
        var position=new Vector3(0,LaunchHeight,0);
        var velocity=new Vector3(0,AimLiftSpeed,0);
        var ticks=0;
        do {var flight=Advance(position,velocity,SimulationStep*motionRate,Weight(pumps));position=flight.Position;velocity=flight.Velocity;ticks++;}while(position.Y>0&&ticks<150);
        var length=MathF.Sqrt(directionX*directionX+directionZ*directionZ);
        if(!float.IsFinite(length)||length<.001f){directionX=0;directionZ=1;length=1;}
        range=float.IsFinite(range)?Math.Clamp(range,MinAimRange,MaxAimRange):DefaultAimRange;
        var speed=range/(ticks*SimulationStep*motionRate);
        return new(directionX/length*speed,AimLiftSpeed,directionZ/length*speed);
    }
    public static float Weight(int pumps) => 1 + .2f*Math.Clamp(pumps,0,3);
    public static bool CanBounce(int count,float verticalSpeed) => count<3 && verticalSpeed < -2;
    public static float BounceVelocity(float verticalSpeed) => MathF.Abs(verticalSpeed)*.62f;
    public static float Speed(float stickMagnitude,int pumps) =>
        BlastBomb.FlySpeed * (.75f + .25f * Math.Clamp(stickMagnitude,0,1)) + Math.Clamp(pumps,0,3) * .65f;
    public static Vector3 Velocity(float facingX,float facingZ,float stickMagnitude,int pumps)
    {
        var speed=Speed(stickMagnitude,pumps);
        return new(facingX*speed,speed*MathF.Tan(MathF.PI*55/180)*.5f,facingZ*speed);
    }
    public static ThrowFlight Advance(Vector3 position,Vector3 velocity,float dt,float gravityScale=1)
    {
        position.X+=velocity.X*dt;position.Z+=velocity.Z*dt;
        velocity.Y-=BlastBomb.Gravity*gravityScale*dt;position.Y+=velocity.Y*dt;
        return new(position,velocity);
    }
    /// <summary>The same swept static footprint is used by the server and advisory guide.
    /// A wall removes horizontal velocity; gravity and the remaining fuse continue normally.</summary>
    public static ThrowFlight AdvanceStatic(Vector3 position,Vector3 velocity,float dt,Func<float,float,float> surface,
        Func<int,int,CellKind> cellKind,Func<int,int,bool> hasCover,bool bounce=false,float gravityScale=1,StageLayout? stage=null)
    {
        var flight=Advance(position,velocity,dt,gravityScale);
        if(stage is not null)
        {
            float ceiling=stage.Ceiling(flight.Position.X,flight.Position.Z,BlastBomb.Radius,position.Y,flight.Position.Y,CoverHeight);
            if(ceiling<flight.Position.Y)flight=new(new(flight.Position.X,ceiling,flight.Position.Z),new(flight.Velocity.X,Math.Min(0,flight.Velocity.Y),flight.Velocity.Z));
        }
        int steps=Math.Max(1,(int)MathF.Ceiling(Vector2.Distance(new(position.X,position.Z),new(flight.Position.X,flight.Position.Z))/.1f));
        if(velocity.X==0&&velocity.Z==0)return flight;
        var last=position;
        for(int step=1;step<=steps;step++)
        {
            var candidate=Vector3.Lerp(position,flight.Position,(float)step/steps);
            if(StaticObstructed(candidate,velocity,surface,cellKind,hasCover,stage))
            {
                if(!bounce)return new(new(last.X,flight.Position.Y,last.Z),new(0,flight.Velocity.Y,0));
                bool hitX=StaticObstructed(new(candidate.X,candidate.Y,last.Z),velocity,surface,cellKind,hasCover,stage);
                bool hitZ=StaticObstructed(new(last.X,candidate.Y,candidate.Z),velocity,surface,cellKind,hasCover,stage);
                if(!hitX&&!hitZ)hitX=hitZ=true;
                return new(new(last.X,flight.Position.Y,last.Z),new(hitX?-velocity.X:velocity.X,flight.Velocity.Y,hitZ?-velocity.Z:velocity.Z));
            }
            last=candidate;
        }
        return flight;
    }
    private static bool StaticObstructed(Vector3 position,Vector3 velocity,Func<float,float,float> surface,
        Func<int,int,CellKind> cellKind,Func<int,int,bool> hasCover,StageLayout? stage)
    {
        const float radius=BlastBomb.Radius;
        var centerFloor=surface(position.X,position.Z);
        for(int x=(int)MathF.Floor(position.X-radius);x<=(int)MathF.Floor(position.X+radius);x++)
        for(int z=(int)MathF.Floor(position.Z-radius);z<=(int)MathF.Floor(position.Z+radius);z++)
        {
            float nearX=Math.Clamp(position.X,x+.0001f,x+.9999f),nearZ=Math.Clamp(position.Z,z+.0001f,z+.9999f);
            float dx=position.X-nearX,dz=position.Z-nearZ;
            if(dx*dx+dz*dz>=radius*radius)continue;
            if(stage?.DeckOverlaps(x,z,position.Y,CoverHeight)==true)return true;
            var kind=cellKind(x,z);
            if(kind is CellKind.Water or CellKind.Void)continue;
            bool cover=hasCover(x,z);
            // Terrain behind the moving center is the ledge being left, not an
            // uphill face. Its trailing footprint must not catch a downward exit.
            if(kind!=CellKind.Pillar&&!cover&&dx*velocity.X+dz*velocity.Z>0)continue;
            var top=surface(nearX,nearZ)+(kind==CellKind.Pillar?(stage?.At(x,z).ObstacleHeight??PillarHeight):cover?CoverHeight:0);
            // Descending onto the current floor is a landing, not a side impact.
            if(top>centerFloor+.001f&&position.Y<top-.001f)return true;
        }
        return false;
    }
    /// <summary>Bombs descending onto solid cover rest on its top; side impacts fall beside it.</summary>
    public static float LandingHeight(float x,float z,Func<float,float,float> surface,
        Func<int,int,CellKind> cellKind,Func<int,int,bool> hasCover,Func<int,int,float>? obstacleHeight=null)
    {
        var cx=(int)MathF.Floor(x);var cz=(int)MathF.Floor(z);var kind=cellKind(cx,cz);
        return surface(x,z)+(kind==CellKind.Pillar?(obstacleHeight?.Invoke(cx,cz)??PillarHeight):
            kind is not (CellKind.Water or CellKind.Void)&&hasCover(cx,cz)?CoverHeight:0);
    }
    /// <summary>Static scenery only. Players, moving bombs and future changes may interrupt the arc.</summary>
    public static ThrowProjection Project(Vector3 origin,Vector3 velocity,Func<float,float,float> surface,
        Func<Vector3,bool> obstructed,float fuseSeconds=BlastBomb.FuseSeconds,Func<float,float,CellKind>? landingKind=null,
        Func<int,int,CellKind>? cellKind=null,Func<int,int,bool>? hasCover=null,float? waterLevel=null,float motionRate=1,bool bounce=false,float gravityScale=1,int pumps=0,StageLayout? stage=null)
    {
        var points=new List<Vector3>{origin};
        var position=origin;
        bool stopped=false;int bounces=0;
        float referenceY=position.Y;
        // Reuse the height-aware callbacks for the whole arc. Recreating captured
        // delegates per sample would add allocations on every aiming frame.
        Func<float,float,float> floorAt=(x,z) => stage is null ? surface(x,z) :
            stage.AtHeight((int)MathF.Floor(x),(int)MathF.Floor(z),referenceY).Kind switch
            { CellKind.Water => waterLevel ?? -1, CellKind.Void => -1, _ => stage.SurfaceAt(x,z,referenceY) };
        Func<int,int,CellKind> kindAt=(x,z) => stage is null ? cellKind!(x,z) : stage.AtHeight(x,z,referenceY).Kind;
        for(int tick=0;tick<150;tick++)
        {
            referenceY=position.Y;
            fuseSeconds-=SimulationStep;
            if(fuseSeconds<=0)return new(points,ThrowProjectionEnd.Fuse);
            var flight=cellKind is null&&stage is null?Advance(position,velocity,SimulationStep*motionRate,gravityScale):
                AdvanceStatic(position,velocity,SimulationStep*motionRate,floorAt,kindAt,hasCover??((x,z)=>false),bounce,gravityScale,stage);
            stopped|=(velocity.X!=0||velocity.Z!=0)&&flight.Velocity.X==0&&flight.Velocity.Z==0;
            position=flight.Position;velocity=flight.Velocity;
            if(obstructed(position))return new(points,ThrowProjectionEnd.Obstructed);
            if(waterLevel is { } level&&float.IsFinite(level)&&position.Y<=level)
            {
                position.Y=level;points.Add(position);
                return new(points,ThrowProjectionEnd.Water);
            }
            var floor=cellKind is null&&stage is null?surface(position.X,position.Z):
                LandingHeight(position.X,position.Z,floorAt,kindAt,hasCover??((x,z)=>false),(x,z)=>stage?.At(x,z).ObstacleHeight??PillarHeight);
            if(position.Y<=floor)
            {
                position.Y=floor;points.Add(position);
                var end=(stage is null ? landingKind?.Invoke(position.X,position.Z) : kindAt((int)MathF.Floor(position.X),(int)MathF.Floor(position.Z))) switch{
                    CellKind.Water=>ThrowProjectionEnd.Water,CellKind.Void=>ThrowProjectionEnd.Void,_=>stopped?ThrowProjectionEnd.Obstructed:ThrowProjectionEnd.Landing};
                if(bounce && pumps>0 && end is not (ThrowProjectionEnd.Water or ThrowProjectionEnd.Void) && CanBounce(bounces,velocity.Y))
                { bounces++;velocity.Y=BounceVelocity(velocity.Y);continue; }
                return new(points,end);
            }
            points.Add(position);
        }
        return new(points,ThrowProjectionEnd.Fuse);
    }
}
public readonly record struct ThrowAim(float X,float Z,float Range);
public readonly record struct ThrowFlight(Vector3 Position,Vector3 Velocity);
public enum ThrowProjectionEnd {Landing,Obstructed,Fuse,Water,Void}
public sealed record ThrowProjection(IReadOnlyList<Vector3> Points,ThrowProjectionEnd End)
{
    public Vector3 Last=>Points[^1];
}
