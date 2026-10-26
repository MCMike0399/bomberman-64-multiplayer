using System.Text.Json;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;

static class PresentationChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var cursor=new PresentationCursor();
        check(cursor.Read(State(5,1,3,5)).Count==0&&cursor.Sequence==5,"Presentation join establishes a silent history baseline");
        check(cursor.Read(State(5,1,3,5)).Count==0,"Repeated first snapshot never replays historical cues");
        var fresh=cursor.Read(State(8,8,5,6,7));
        check(fresh.Select(Seq).SequenceEqual(new long[]{6,7,8})&&cursor.Sequence==8,"Fresh accepted effects are returned in sequence order");
        check(cursor.Read(State(8,5,6,7,8)).Count==0,"Repeated authoritative snapshot cannot duplicate played effects");
        check(cursor.Read(State(7,6,7)).Count==0&&cursor.Sequence==8,"Reordered older snapshot cannot rewind the presentation cursor");
        var tail=cursor.Read(State(150,148,149,150));
        check(tail.Select(Seq).SequenceEqual(new long[]{148,149,150})&&cursor.Sequence==150,"A history gap consumes the available authoritative tail without inventing missing actions");
        check(cursor.Read(State(150,148,149,150)).Count==0,"Recovered tail is consumed only once");
        cursor.Reset();
        check(cursor.Sequence==0&&cursor.Read(State(153,150,151,152,153)).Count==0&&cursor.Sequence==153,"Reconnect reset silently baselines newly received history");
        check(cursor.Read(State(154,151,152,153,154)).Select(Seq).SequenceEqual(new long[]{154}),"After reconnect only newly accepted actions become cues");
        // Read must own the returned elements: transport/message documents may be disposed immediately.
        IReadOnlyList<JsonElement> retained;
        using(var doc=JsonDocument.Parse("""{"presentationSeq":155,"events":[{"seq":155,"kind":"pump"}]}"""))retained=cursor.Read(doc.RootElement);
        check(retained.Single().GetProperty("kind").GetString()=="pump","Presentation event payload survives transport document disposal");

        await using var local=new LocalSession(new SessionSettings(Bots:0),1,practice:true);
        var initial=Latest(local);cursor.Reset();cursor.Read(initial);
        for(var i=0;i<64;i++)local.Advance(1d/30);
        cursor.Read(Latest(local));
        local.SendInput(0,new PlayerInput(0,0,true));local.SendInput(0,default);
        // Simulate a slow renderer: many 15 Hz states coalesce, but accepted transient actions remain.
        for(var i=0;i<96;i++)local.Advance(1d/30);
        var snapshot=Latest(local);var actions=cursor.Read(snapshot);
        foreach(var kind in new[]{"bomb-drop","blast","death","respawn"})
            check(actions.Count(e=>e.GetProperty("kind").GetString()==kind)==1,"Coalesced LocalSession state preserves exactly one "+kind+" action");
        var blast=actions.Single(e=>e.GetProperty("kind").GetString()=="blast");
        var keys=new[]{"seq","tick","round","kind","entityId","playerId","x","y","z","magnitude","radius","reason","causedBy"};
        check(keys.All(key=>blast.TryGetProperty(key,out _))&&!blast.TryGetProperty("EntityId",out _),"Local event serialization preserves the complete camelCase presentation contract");
        check(blast.GetProperty("playerId").GetString()=="local-0"&&blast.GetProperty("entityId").GetString()!.StartsWith("b")&&blast.GetProperty("radius").GetSingle()>1,"Local presentation blast carries the authoritative owner identity and radius");
        var death=actions.Single(e=>e.GetProperty("kind").GetString()=="death");
        check(death.GetProperty("reason").GetString()=="explosion"&&death.GetProperty("causedBy").GetString()=="local-0","Client receives the actual authoritative elimination reason and attacker");
        check(cursor.Read(snapshot).Count==0,"Local coalesced state replay cannot duplicate audio");
        cursor.Reset();
        check(cursor.Read(snapshot).Count==0,"Reconnecting to actual local state does not replay its prior blast or death");
    }
    private static long Seq(JsonElement e)=>e.GetProperty("seq").GetInt64();
    private static JsonElement State(long sequence,params long[] events)=>JsonSerializer.SerializeToElement(new
    {
        presentationSeq=sequence,events=events.Select(seq=>new {seq,kind="kick"})
    });
    private static JsonElement Latest(LocalSession session)=>session.DrainMessages().Last(m=>m.GetProperty("t").GetString()=="state").GetProperty("d");
}
