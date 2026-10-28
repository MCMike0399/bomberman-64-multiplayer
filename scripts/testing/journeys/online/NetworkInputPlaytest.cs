using System.Diagnostics;
using System.Text.Json;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;
using Godot;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    // Diagnostic leaf: real server input, independent of render cadence.
    private async Task NetworkInputPulse()
    {
        await _root.StartOnlineAsync(new Uri(_server),"Pulse host",new SessionSettings(Bots:0,Ghosts:false){Hosting="server"},NetworkMode.CreatePrivate);
        var host=(NetworkSession)_root.ActiveSession!;
        await using var guest=await NetworkSession.ConnectAsync(new Uri(_server),"Pulse guest",new SessionSettings(Bots:0),NetworkMode.JoinCode,host.JoinCode);
        using var live=KeepFixtureLive(guest);
        try
        {
            var ready=Stopwatch.StartNew();
            while(host.Party?.CanStart!=true&&ready.Elapsed.TotalSeconds<20){await Frames(1);await Task.Delay(16);}
            Check(host.Party?.CanStart==true,"input pulse fixture has two connected humans");
            host.RequestStart();
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"input pulse fixture reaches fight",600);
            long fightAt=State.GetProperty("serverTime").GetInt64();
            await WaitState(s=>s.GetProperty("serverTime").GetInt64()>fightAt+2000,"input pulse starts after the ready/fight transition settles",600);
            var player=State.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("id").GetString()==host.PlayerId);
            var before=new Vector2(player.GetProperty("x").GetSingle(),player.GetProperty("z").GetSingle());
            var direction=SafeNetworkNudge(player);
            long at=State.GetProperty("serverTime").GetInt64();
            var clock=Stopwatch.StartNew();
            long pulseSequence=await NetworkPulse(host,new PlayerInput(direction.X,direction.Y));
            var wait=Stopwatch.StartNew();
            while(State.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("id").GetString()==host.PlayerId).GetProperty("ack").GetInt64()<=pulseSequence&&wait.Elapsed.TotalSeconds<15){await Frames(1);await Task.Delay(16);}
            player=State.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("id").GetString()==host.PlayerId);
            Check(player.GetProperty("ack").GetInt64()>pulseSequence,"movement observation acknowledges input after the pulse release");
            float distance=before.DistanceTo(new(player.GetProperty("x").GetSingle(),player.GetProperty("z").GetSingle()));
            Event("network-input-movement",new{distance,elapsedMs=clock.Elapsed.TotalMilliseconds,alive=HealthyNetworkActor(player),events=host.ConnectionEvents});
            Check(HealthyNetworkActor(player),"bounded input leaves the actor alive on its supported floor");
            Check(distance>.12f&&distance<1,"one short movement pulse is accepted without a frame-length hold");
            await NetworkPulse(host,new PlayerInput(0,0,A:true,B:true),new PlayerInput(0,0,B:true));
            at=State.GetProperty("serverTime").GetInt64();wait.Restart();
            while(State.GetProperty("serverTime").GetInt64()<at+4000&&wait.Elapsed.TotalSeconds<15){await Frames(1);await Task.Delay(16);}
            player=State.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("id").GetString()==host.PlayerId);
            Check(HealthyNetworkActor(player)&&player.GetProperty("carrying").GetString()=="bomb"&&State.GetProperty("bombs").EnumerateArray().Any(b=>
                b.GetProperty("ownerId").GetString()==host.PlayerId&&b.GetProperty("carrierId").GetString()==host.PlayerId),
                "bounded primary preserves carry through more than a full ordinary fuse");
        }
        finally{Input(0,default);_root.Back();await Frames(2);}
    }

    private static bool HealthyNetworkActor(JsonElement player)=>player.GetProperty("alive").GetBoolean()&&
        !player.GetProperty("ghost").GetBoolean()&&!player.GetProperty("falling").GetBoolean()&&!player.GetProperty("airborne").GetBoolean();

    private Vector2 SafeNetworkNudge(JsonElement player)
    {
        Check(HealthyNetworkActor(player)&&player.GetProperty("statusEffect").GetString()=="none","nudge starts from a healthy actor at its ordinary speed");
        var stage=BlastStages.Get(_root.LatestStage!.Value.GetProperty("id").GetString()!);
        var origin=new Vector2(player.GetProperty("x").GetSingle(),player.GetProperty("z").GetSingle());
        float height=player.GetProperty("y").GetSingle();
        var cover=Convert.FromBase64String(State.GetProperty("softBlocks").GetString()!);
        bool Clear(Vector2 point)
        {
            // Include the actual .4 body radius and a .05 safety margin.
            foreach(float dx in new[]{-.45f,.45f})foreach(float dz in new[]{-.45f,.45f})
            {
                float x=point.X+dx,z=point.Y+dz;int cx=(int)MathF.Floor(x),cz=(int)MathF.Floor(z);
                if(!stage.AtHeight(cx,cz,height).IsPassable||Math.Abs(stage.SurfaceAt(x,z,height)-height)>.01f)return false;
                int index=cz*stage.Width+cx;
                if((uint)cx>=(uint)stage.Width||(uint)cz>=(uint)stage.Height||(cover[index>>3]&(1<<(index&7)))!=0)return false;
            }
            return true;
        }
        var inward=new Vector2(stage.Width*.5f,stage.Height*.5f)-origin;
        foreach(var direction in new[]{Vector2.Left,Vector2.Right,Vector2.Up,Vector2.Down}.OrderByDescending(d=>d.Dot(inward)))
            if(Enumerable.Range(0,26).All(i=>Clear(origin+direction*(i*.05f))))
            {Event("network-input-path",new{id=player.GetProperty("id").GetString(),x=origin.X,z=origin.Y,dx=direction.X,dz=direction.Y,supportedDistance=1.25f});return direction;}
        throw new InvalidOperationException("No short supported clear nudge from the actual actor position");
    }
}
