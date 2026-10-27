using System.Text.Json;
using Bomber.Client.Controls;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;

static class AimInputChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var walking=new PlayerInput(.6f,.8f,B:true);
        var aimHeld=walking with {Aim=true};
        check(InputPolicy.Resolve(default,aimHeld).Aim&&InputPolicy.Resolve(default,walking)==new PlayerInput(.6f,.8f,B:true),
            "Left-stick precision aim and ordinary movement resolve without a right-stick rate path");
        PointerChecks(check);
        foreach(bool aiming in new[]{false,true})
        {
            using var packet=JsonDocument.Parse(NetworkSession.EncodeInput(new PlayerInput(.25f,-.5f,B:true,Aim:aiming),17));
            var data=packet.RootElement.GetProperty("d");
            check(data.GetProperty("aim").GetBoolean()==aiming&&data.GetProperty("b").GetBoolean(),"Network input preserves explicit aim "+aiming+" independently of carry");
            check(data.GetProperty("tx").ValueKind==JsonValueKind.Null&&data.GetProperty("tz").ValueKind==JsonValueKind.Null,"Network input without a pointer sends no landing target");
        }
        using(var pointed=JsonDocument.Parse(NetworkSession.EncodeInput(new PlayerInput(0,0,B:true,AimTarget:new(4.5f,7.25f)),18)))
        {
            var data=pointed.RootElement.GetProperty("d");
            check(data.GetProperty("tx").GetSingle()==4.5f&&data.GetProperty("tz").GetSingle()==7.25f&&!data.GetProperty("tr").GetBoolean(),"Network input carries the absolute mouse landing target");
        }
        using(var relative=JsonDocument.Parse(NetworkSession.EncodeInput(new PlayerInput(0,0,B:true,AimTarget:new(-2,3),AimRelative:true),20)))
        {
            check(relative.RootElement.GetProperty("d").GetProperty("tr").GetBoolean(),"Network input marks right-stick offsets as carrier-relative");
        }
        using(var invalid=JsonDocument.Parse(NetworkSession.EncodeInput(new PlayerInput(0,0,AimTarget:new(float.NaN,1)),19)))
            check(invalid.RootElement.GetProperty("d").GetProperty("tx").ValueKind==JsonValueKind.Null,"Nonfinite pointer targets are never sent");

        await using(var local=await Carrying(2,check))
        {
            var before=Latest(local);
            // Both transitions occur before authority advances. Keeping B held isolates
            // aim edge delivery from pickup/throw, without inspecting adapter internals.
            local.SendInput(0,new PlayerInput(0,0,B:true,Aim:true));
            local.SendInput(0,new PlayerInput(0,0,B:true));
            Step(local,2);
            var state=Latest(local);var first=Player(state,0);var other=Player(state,1);
            check(first.GetProperty("aimSet").GetBoolean()&&!first.GetProperty("aiming").GetBoolean(),"Subframe aim hold and release both reach authority while carry stays held");
            check(!other.GetProperty("aimSet").GetBoolean()&&other.GetProperty("carrying").GetString()=="bomb","Queued first-seat aim does not leak into second-seat carry");
            check(SamePosition(Player(before,0),first)&&SamePosition(Player(before,1),other),"Subframe aim transitions leave both stationary carriers planted");
            local.SendInput(0,default);local.SendInput(0,new PlayerInput(0,0,B:true));
            Step(local,2);state=Latest(local);
            check(Player(state,0).GetProperty("carrying").ValueKind==JsonValueKind.Null&&!Player(state,0).GetProperty("aimSet").GetBoolean(),"Subframe carry release commits retained aim despite a newer held-B sample");
            check(Player(state,1).GetProperty("carrying").GetString()=="bomb","First-seat queued throw cannot release another local player's bomb");
            check(state.GetProperty("events").EnumerateArray().Count(e=>e.GetProperty("kind").GetString()=="throw"&&e.GetProperty("playerId").GetString()=="local-0")==1,"Queued release creates exactly one authoritative throw");
        }

        // Four production adapter seats receive equivalent device-resolved commands.
        // Compare changes rather than absolute headings so spawn facing is irrelevant.
        await using(var local=await Carrying(4,check))
        {
            var before=Latest(local);
            var keyboard=InputPolicy.Resolve(new PlayerInput(1,-1,B:true,Aim:true));
            var stick=InputPolicy.Resolve(default,new PlayerInput(MathF.Sqrt(.5f),-MathF.Sqrt(.5f),B:true,Aim:true));
            var dpad=InputPolicy.Resolve(default,default,new PlayerInput(1,-1,B:true,Aim:true));
            var arrows=InputPolicy.Resolve(new PlayerInput(1,-1,B:true,Aim:true));
            local.SendInput(0,keyboard);local.SendInput(1,stick);local.SendInput(2,dpad);local.SendInput(3,arrows);Step(local,12);
            var after=Latest(local);
            for(int seat=0;seat<4;seat++)
                check(SamePosition(Player(before,seat),Player(after,seat))&&Player(after,seat).GetProperty("aiming").GetBoolean(),"Device-resolved aim plants actual LocalSession seat "+seat);
            var changes=Enumerable.Range(0,4).Select(seat=>Change(Player(before,seat),Player(after,seat))).ToArray();
            check(changes[0].X>0&&changes[0].Z<0,"Deliberate input moves the authoritative cursor right and up");
            check(changes.Skip(1).All(change=>Near(change.X,changes[0].X)&&Near(change.Z,changes[0].Z)),"Keyboard diagonals, full-stick diagonal and D-pad share cursor rates through LocalSession");
        }

        // A pointer seat walks while aiming; a second seat's rate cursor is unaffected.
        await using(var local=await Carrying(2,check))
        {
            var before=Player(Latest(local),0);
            var target=new System.Numerics.Vector2(N(before,"x")+2,N(before,"z")-3);
            local.SendInput(0,new PlayerInput(-1,0,B:true,AimTarget:target));local.SendInput(1,new PlayerInput(0,0,B:true));Step(local,6);
            var state=Latest(local);var pointer=Player(state,0);
            check(N(pointer,"x")<N(before,"x")-.5f&&!pointer.GetProperty("aiming").GetBoolean(),"Pointer aim leaves the carrier walking through LocalSession");
            var landing=new System.Numerics.Vector2(N(pointer,"x"),N(pointer,"z"))+new System.Numerics.Vector2(N(pointer,"aimX"),N(pointer,"aimZ"))*N(pointer,"aimRange");
            check(System.Numerics.Vector2.Distance(landing,target)<.02f,"LocalSession delivers the pointer target to authority");
            check(!Player(state,1).GetProperty("aimSet").GetBoolean(),"One seat's pointer does not leak into another carrier");
        }

        await using(var local=await Carrying(2,check))
        {
            var before=Latest(local);
            local.SendInput(0,InputPolicy.Resolve(default,new PlayerInput(.5f,0,B:true,Aim:true)));
            local.SendInput(1,InputPolicy.Resolve(default,new PlayerInput(1,0,B:true,Aim:true)));Step(local,8);
            var after=Latest(local);var fine=Change(Player(before,0),Player(after,0));var full=Change(Player(before,1),Player(after,1));
            check(fine.X>0&&fine.X<full.X,"Partial stick permits a finer authoritative cursor nudge without exceeding full-stick rate");
            check(SamePosition(Player(before,0),Player(after,0))&&SamePosition(Player(before,1),Player(after,1)),"Partial and full stick aim both preserve planted carrier position");
            local.SendInput(0,InputPolicy.Resolve(default,new PlayerInput(0,.5f,B:true,Aim:true)));
            local.SendInput(1,InputPolicy.Resolve(default,new PlayerInput(0,1,B:true,Aim:true)));Step(local,6);
            var ranged=Latest(local);
            float fineRange=Cursor(Player(ranged,0)).Y-Cursor(Player(after,0)).Y;
            float fullRange=Cursor(Player(ranged,1)).Y-Cursor(Player(after,1)).Y;
            check(fineRange>0&&fineRange<fullRange,"Partial stick permits finer range adjustment through the same command pipeline");
        }
    }

    private static void PointerChecks(Action<bool,string> check)
    {
        var stick=new AimStickPointer();const float frame=1f/60;
        check(stick.Step(.1f,.1f,frame)==null,"Right-stick drift never points a throw");
        var full=stick.Step(0,-1,frame);
        check(full is {} up&&Math.Abs(up.X)<.0001f&&Math.Abs(up.Y+Bomber.Game.Simulation.Rules.BombThrowRules.MaxAimRange)<.0001f,"Full right-stick tilt points up to the maximum range");
        check(stick.Step(0,-.3f,frame)==full,"A spring-back sweep keeps the chosen target");
        check(stick.Step(0,0,frame)==null,"A released right stick stops reporting and authority keeps the last target");
        stick.Reset();
        System.Numerics.Vector2? eased=null;
        for(int i=0;i<=20;i++){float tilt=1-.44f*i/20;eased=stick.Step(tilt,0,frame);}
        float expected=AimStickPointer.MinRange+(.56f-InputPolicy.StickDeadzone)/(AimStickPointer.OuterDeflection-InputPolicy.StickDeadzone)*(Bomber.Game.Simulation.Rules.BombThrowRules.MaxAimRange-AimStickPointer.MinRange);
        check(eased is {} slow&&Math.Abs(slow.X-expected)<.01f&&slow.Y==0,"A deliberate slow pull-back shortens the throw");
        check(stick.Step(-.1f,0,frame) is null&&stick.Step(-.5f,-.5f,frame) is {} turned&&turned.X<0&&turned.Y<0,"The right stick can point straight behind the carrier");

        var gate=new MouseAimGate();
        check(!gate.Active(true,10),"An untouched mouse never steers a keyboard throw");
        gate.Active(false,10);gate.MouseMoved(3,10);
        check(!gate.Active(true,10.5),"A brushed trackpad below the motion threshold does not take over");
        gate.Active(false,10.6);for(int i=0;i<6;i++)gate.MouseMoved(1,10.6+i);
        check(!gate.Active(true,15.7),"Sparse sensor jitter never accumulates into pointing");
        gate.Active(false,11);gate.MouseMoved(10,11);
        check(gate.Active(true,12),"Recent mouse motion points a newly carried bomb");
        gate.Active(false,13);
        check(!gate.Active(true,20),"Old mouse motion does not point a later carry");
        gate.MouseMoved(10,21);
        check(gate.Active(true,21.5),"Moving the mouse during a carry starts pointing");
        gate.KeyboardAimed(22);
        check(!gate.Active(true,22.5),"Keyboard aiming takes control back from the mouse");
        gate.MouseMoved(10,23);
        check(gate.Active(true,23.1),"Moving the mouse again resumes pointing");
        check(!gate.Active(false,24),"An empty-handed player is never pointing");
    }
    private static async Task<LocalSession> Carrying(int seats,Action<bool,string> check)
    {
        var local=new LocalSession(new SessionSettings(Bots:0,Stage:"rock-garden"),seats,practice:true);
        Step(local,64);local.DrainMessages();
        for(int seat=0;seat<seats;seat++)local.SendInput(seat,new PlayerInput(0,0,A:true,B:true));
        Step(local,2);
        for(int seat=0;seat<seats;seat++)local.SendInput(seat,new PlayerInput(0,0,B:true));
        Step(local,2);var state=Latest(local);
        for(int seat=0;seat<seats;seat++)check(Player(state,seat).GetProperty("carrying").GetString()=="bomb","Ordinary adapter commands create a held bomb for aim seat "+seat);
        // Leave a fresh readable snapshot for callers after the setup assertions.
        Step(local,2);
        await Task.CompletedTask;return local;
    }
    private static void Step(LocalSession local,int ticks){for(int i=0;i<ticks;i++)local.Advance(1d/30);}
    private static JsonElement Latest(LocalSession local)=>local.DrainMessages().Last(m=>m.GetProperty("t").GetString()=="state").GetProperty("d");
    private static JsonElement Player(JsonElement state,int seat)=>state.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("slot").GetInt32()==seat);
    private static float N(JsonElement value,string key)=>value.GetProperty(key).GetSingle();
    private static bool Near(float a,float b)=>Math.Abs(a-b)<.0001f;
    private static bool SamePosition(JsonElement a,JsonElement b)=>Near(N(a,"x"),N(b,"x"))&&Near(N(a,"z"),N(b,"z"));
    private static System.Numerics.Vector2 Cursor(JsonElement p)=>new(N(p,"aimX")*N(p,"aimRange"),N(p,"aimZ")*N(p,"aimRange"));
    private static (float X,float Z) Change(JsonElement before,JsonElement after)
    {
        var delta=Cursor(after)-new System.Numerics.Vector2(N(before,"fx"),N(before,"fz"))*N(before,"aimRange");
        return (delta.X,delta.Y);
    }
}
