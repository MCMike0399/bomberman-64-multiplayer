using System.Text.Json;
using Bomber.Client;
using Bomber.Game;

static class AimInputChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        foreach(bool aiming in new[]{false,true})
        {
            using var packet=JsonDocument.Parse(NetworkSession.EncodeInput(new PlayerInput(.25f,-.5f,B:true,Aim:aiming),17));
            var data=packet.RootElement.GetProperty("d");
            check(data.GetProperty("aim").GetBoolean()==aiming&&data.GetProperty("b").GetBoolean(),"Network input preserves explicit aim "+aiming+" independently of carry");
        }

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

        // Three production adapter seats receive equivalent device-resolved commands.
        // Compare changes rather than absolute headings so spawn facing is irrelevant.
        await using(var local=await Carrying(3,check))
        {
            var before=Latest(local);
            var keyboard=InputPolicy.Resolve(new PlayerInput(1,-1,B:true,Aim:true));
            var stick=InputPolicy.Resolve(default,new PlayerInput(MathF.Sqrt(.5f),-MathF.Sqrt(.5f),B:true,Aim:true));
            var dpad=InputPolicy.Resolve(default,default,new PlayerInput(1,-1,B:true,Aim:true));
            local.SendInput(0,keyboard);local.SendInput(1,stick);local.SendInput(2,dpad);Step(local,12);
            var after=Latest(local);
            for(int seat=0;seat<3;seat++)
                check(SamePosition(Player(before,seat),Player(after,seat))&&Player(after,seat).GetProperty("aiming").GetBoolean(),"Device-resolved aim plants actual LocalSession seat "+seat);
            var changes=Enumerable.Range(0,3).Select(seat=>Change(Player(before,seat),Player(after,seat))).ToArray();
            check(changes[0].Angle>0&&changes[0].Range>0,"Deliberate input actually rotates and extends authoritative landing aim");
            check(Near(changes[0].Angle,changes[1].Angle)&&Near(changes[0].Angle,changes[2].Angle)&&Near(changes[0].Range,changes[1].Range)&&Near(changes[0].Range,changes[2].Range),"Keyboard diagonal, full-stick diagonal and D-pad share angular and range rates through LocalSession");
        }

        await using(var local=await Carrying(2,check))
        {
            var before=Latest(local);
            local.SendInput(0,InputPolicy.Resolve(default,new PlayerInput(.5f,0,B:true,Aim:true)));
            local.SendInput(1,InputPolicy.Resolve(default,new PlayerInput(1,0,B:true,Aim:true)));Step(local,8);
            var after=Latest(local);var fine=Change(Player(before,0),Player(after,0));var full=Change(Player(before,1),Player(after,1));
            check(fine.Angle>0&&fine.Angle<full.Angle,"Partial stick permits a finer authoritative turn without exceeding full-stick rate");
            check(SamePosition(Player(before,0),Player(after,0))&&SamePosition(Player(before,1),Player(after,1)),"Partial and full stick aim both preserve planted carrier position");
            local.SendInput(0,InputPolicy.Resolve(default,new PlayerInput(0,.5f,B:true,Aim:true)));
            local.SendInput(1,InputPolicy.Resolve(default,new PlayerInput(0,1,B:true,Aim:true)));Step(local,6);
            var ranged=Latest(local);
            float fineRange=N(Player(after,0),"aimRange")-N(Player(ranged,0),"aimRange");
            float fullRange=N(Player(after,1),"aimRange")-N(Player(ranged,1),"aimRange");
            check(fineRange>0&&fineRange<fullRange,"Partial stick permits finer range adjustment through the same command pipeline");
        }
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
    private static (float Angle,float Range) Change(JsonElement before,JsonElement after)
    {
        float initial=MathF.Atan2(N(before,"fx"),N(before,"fz"));
        float final=MathF.Atan2(N(after,"aimX"),N(after,"aimZ"));
        return (MathF.Abs(MathF.IEEERemainder(final-initial,2*MathF.PI)),N(after,"aimRange")-N(before,"aimRange"));
    }
}
