namespace Bomber.Game.Simulation;

/// <summary>Authoritative height-aware body contacts, after input and airborne integration.</summary>
public sealed partial class BlastSession
{
    private int _bodyContactFrame;
    private void BeginBodyContacts()
    {
        foreach(var p in _players.Values)
        {p.BodyStartX=p.X;p.BodyStartZ=p.Z;p.BodyYield=0;}
    }
    private static bool HasBody(BlastPlayer p)=>p.Alive&&!p.PendingRoundStart&&!p.Ghost&&p.CarriedById is null;
    private void ResolveBodyContacts(float dt)
    {
        var bodies=_players.Values.Where(HasBody).OrderBy(p=>p.Slot).ToArray();
        // Alternating traversal avoids granting the first registered seat every final correction.
        // Repeated constraints propagate pressure through a crowd, including a crowd against stone.
        for(int iteration=0;iteration<64;iteration++)
        {
            bool contact=false;
            bool reverse=((_bodyContactFrame+iteration)&1)!=0;
            for(int ai=0;ai<bodies.Length;ai++)for(int bi=ai+1;bi<bodies.Length;bi++)
            {
                var a=bodies[reverse?bodies.Length-1-ai:ai];
                var b=bodies[reverse?bodies.Length-1-bi:bi];
                if(!HasBody(a)||!HasBody(b)||Math.Abs(a.Y-b.Y)>=HeadHeight-.05f)continue;
                float dx=b.X-a.X,dz=b.Z-a.Z,distance=MathF.Sqrt(dx*dx+dz*dz);
                float penetration=PlayerRadius*2-distance;
                if(penetration<=.00002f)continue;
                contact=true;
                if(distance<.00001f)
                {
                    dx=b.BodyStartX-a.BodyStartX;dz=b.BodyStartZ-a.BodyStartZ;
                    distance=MathF.Sqrt(dx*dx+dz*dz);
                    if(distance<.00001f){dx=a.Slot<b.Slot?1:-1;dz=0;distance=1;}
                }
                float nx=dx/distance,nz=dz/distance;
                // A released hold can begin with co-located bodies. Repair that
                // invalid starting contact fully, without treating it as new pressure.
                bool repairing=Dist2(a.BodyStartX,a.BodyStartZ,b.BodyStartX,b.BodyStartZ)<.799f*.799f;
                bool movingA=repairing||a.Airborne||a.StunTimer<=0&&a.Input.Length>.1f;
                bool movingB=repairing||b.Airborne||b.StunTimer<=0&&b.Input.Length>.1f;
                float weightA=movingA?1:.25f,weightB=movingB?1:.25f;
                float movedA=SeparateBody(a,-nx,-nz,penetration*weightA/(weightA+weightB),dt,movingA);
                float movedB=SeparateBody(b,nx,nz,penetration-movedA,dt,movingB);
                if(movedA+movedB<penetration-.00002f)
                    SeparateBody(a,-nx,-nz,penetration-movedA-movedB,dt,movingA);
                // Remove only velocity into contact; retain tangential flight and upward arcs.
                float va=a.AirVX*nx+a.AirVZ*nz,vb=b.AirVX*nx+b.AirVZ*nz;
                if(a.Airborne&&va>0){a.AirVX-=va*nx;a.AirVZ-=va*nz;}
                if(b.Airborne&&vb<0){b.AirVX-=vb*nx;b.AirVZ-=vb*nz;}
            }
            if(!contact)break;
        }
        _bodyContactFrame++;
        // Riders follow the corrected carrier in the same snapshot.
        foreach(var p in _players.Values)
            if(p.CarriedById is {} id&&_players.TryGetValue(id,out var carrier))
            {p.X=carrier.X;p.Z=carrier.Z;p.SurfaceY=carrier.Y+PlayerCarryHeight;}
    }
    private float SeparateBody(BlastPlayer p,float nx,float nz,float amount,float dt,bool moving)
    {
        // Idle/stunned bodies give ground slowly rather than forming an immovable barrier.
        // Cap total yielding across all crowd constraints, rather than per neighbour.
        if(!moving)amount=Math.Min(amount,Math.Max(0,1.2f*dt-p.BodyYield));
        if(amount<=0)return 0;
        float moved=0;
        while(amount>.000001f)
        {
            float step=Math.Min(amount,.04f),lo=0,hi=step;
            bool Blocked(float d)=>p.Airborne?AirborneTerrainBlocks(p.X+nx*d,p.Z+nz*d,p):CollidesWithSolid(p.X+nx*d,p.Z+nz*d,p.Tier,p);
            if(Blocked(step))
            {
                for(int i=0;i<12;i++){float mid=(lo+hi)*.5f;if(Blocked(mid))hi=mid;else lo=mid;}
                step=lo;
            }
            if(step<.000001f)break;
            if(p.Airborne){p.X+=nx*step;p.Z+=nz*step;}
            else ApplyMoveDelta(p,nx*step,nz*step,dt);
            moved+=step;amount-=step;
            if(step<.00001f)break;
        }
        if(!moving)p.BodyYield+=moved;
        return moved;
    }
    private static void ApplyPlayerImpulse(BlastPlayer p,float vx,float vz,float vy)
    {
        float speed=MathF.Sqrt(vx*vx+vz*vz),scale=speed>8?8/speed:1;
        p.AirVX=vx*scale;p.AirVZ=vz*scale;p.AirVY=Math.Clamp(vy,0,6);
        p.Airborne=true;p.Falling=false;
    }
}
