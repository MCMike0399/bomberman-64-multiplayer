using System.Text.Json;
using Bomber.Game.Simulation;

namespace Bomber.Client;

/// <summary>Bounds and shape checks before player-hosted data reaches engine allocation and rendering.</summary>
public static class PeerFrameRules
{
    public static bool Valid(JsonElement frame)
    {
        try
        {
            if (frame.ValueKind != JsonValueKind.Object || !frame.TryGetProperty("d", out var d) || d.ValueKind != JsonValueKind.Object) return false;
            switch (frame.GetProperty("t").GetString())
            {
                case "party.result":
                    return Fields(d,"s:reason") && Array(d,"winners",8,w=>w.ValueKind==JsonValueKind.String&&w.GetString()!.Length<=128) &&
                        (!d.TryGetProperty("details",out var details)||details.ValueKind==JsonValueKind.Null||
                        (details.ValueKind==JsonValueKind.Object&&(!details.TryGetProperty("scoreboard",out _)||
                        Array(details,"scoreboard",8,p=>Fields(p,"s:id i:slot i:appearance s:name i:wins b:bot")&&p.GetProperty("slot").GetInt32() is >=0 and <=7&&p.GetProperty("wins").GetInt32() is >=0 and <=5))));
                case "appearance": return Fields(d,"s:playerId i:index") && d.GetProperty("index").GetInt32() is >=0 and <=7;
                case "stage":
                    if (!Fields(d,"s:id s:name i:width i:height i:round i:winsTarget i:roundSeconds")) return false;
                    int width=d.GetProperty("width").GetInt32(),height=d.GetProperty("height").GetInt32();
                    return width is >=3 and <=32 && height is >=3 and <=32 &&
                        d.GetProperty("id").GetString() is "sunken-temple" or "rock-garden" or "up-and-down" or "top-rules" or "blizzard-battle" or "greedy-trap" &&
                        Array(d,"cells",1024,c=>Cell(c,d.GetProperty("id").GetString() is "top-rules" or "greedy-trap")) &&
                        d.GetProperty("cells").GetArrayLength()==width*height && CoverHeight(d) && Underpasses(d,width*height) && Array(d,"spawns",8,p=>p.ValueKind==JsonValueKind.Array&&p.GetArrayLength()==2&&p.EnumerateArray().All(v=>v.TryGetInt32(out int n)&&n>=0&&n<32));
                case "state":
                    if (!Fields(d,"i:round l:serverTime s:stageLighting l:presentationSeq i:phase n:timeLeft i:closedLeft i:closedRight s:hurryMode n:waterLevel n:waterStartsIn n:waterRiseRate n:hurryStartsIn s:softBlocks")) return false;
                    if (d.GetProperty("phase").GetInt32() is <0 or >3 || Convert.FromBase64String(d.GetProperty("softBlocks").GetString()!).Length>128) return false;
                    return Array(d,"players",8,p=>Fields(p,
                        "i:slot i:appearance s:id s:name b:bot n:x n:y n:z n:fx n:fz b:alive b:ghost ?s:latchedTo b:carryFromPrimary b:aiming b:aimSet n:aimX n:aimZ n:aimRange n:primaryHoldProgress n:kickWindup n:kickRecovery b:stunned ?s:knockdownReason n:recoveryRemaining n:recoveryTotal ?s:carriedBy ?s:carryingPlayerId b:airborne b:falling ?s:carrying b:heart i:fire i:bombs i:activeBombs i:pickupSerial ?s:lastPickup i:pumpSerial s:bombType s:ability s:statusEffect n:statusRemaining n:statusScale i:maxFire i:maxBombs b:red b:remote i:wins ?l:ack")&&p.GetProperty("slot").GetInt32() is >=0 and <=7&&p.GetProperty("statusScale").GetSingle() is >0 and <=2) &&
                        Array(d,"bombs",64,b=>Fields(b,"s:id s:ownerId b:ownerCanExit ?s:carrierId i:effectiveLevel n:x n:y n:z i:level i:pumps s:mode i:bounces n:motionRate n:gravityScale n:charge b:charging b:red b:remote n:fuse i:state")) &&
                        (!d.TryGetProperty("meteors",out _)||Array(d,"meteors",8,m=>Fields(m,"s:id n:x n:z n:remaining")&&
                            m.GetProperty("x").GetSingle() is >=0 and <32&&m.GetProperty("z").GetSingle() is >=0 and <32&&
                            m.GetProperty("remaining").GetSingle() is >=0 and <=BlastStages.MeteorWarningSeconds)) &&
                        (!d.TryGetProperty("blizzard",out var weather)||weather.ValueKind==JsonValueKind.Null||
                            Fields(weather,"n:time i:round n:x n:z")&&weather.GetProperty("time").GetSingle()>=0&&
                            weather.TryGetProperty("seed",out var seed)&&seed.TryGetInt32(out _)&&
                            Math.Abs(weather.GetProperty("x").GetSingle())<=BlizzardRules.WindSpeed&&
                            Math.Abs(weather.GetProperty("z").GetSingle())<=BlizzardRules.WindSpeed) &&
                        Array(d,"hazards",1024,h=>Fields(h,"s:id n:x n:z n:y n:r n:age n:life b:red")) &&
                        Array(d,"items",512,i=>Fields(i,"s:id i:cx i:cz s:type n:life b:dropped n:y")) &&
                        Array(d,"events",128,e=>Fields(e,"l:seq i:tick i:round s:kind ?s:entityId ?s:playerId n:x n:y n:z n:magnitude n:radius ?s:reason ?s:causedBy"));
                default: return false;
            }
        }
        catch(Exception ex) when(ex is InvalidOperationException or KeyNotFoundException or FormatException or OverflowException){return false;}
    }
    private static bool CoverHeight(JsonElement stage)
    {
        if(!stage.TryGetProperty("coverHeight",out var value))return stage.GetProperty("id").GetString()!="blizzard-battle";
        return value.ValueKind==JsonValueKind.Number&&value.TryGetSingle(out float height)&&float.IsFinite(height)&&height is >=.15f and <=3;
    }
    private static bool Cell(JsonElement cell,bool authoredWalls,bool allowBelow=true)
    {
        if(!Fields(cell,"i:k i:t i:rx i:rz n:rf n:rt"))return false;
        int kind=cell.GetProperty("k").GetInt32(),tier=cell.GetProperty("t").GetInt32();
        if(kind is <0 or >4 || tier is <0 or >8 ||
            cell.GetProperty("rx").GetInt32() is <-1 or >1 || cell.GetProperty("rz").GetInt32() is <-1 or >1 ||
            cell.GetProperty("rf").GetSingle() is <0 or >8 || cell.GetProperty("rt").GetSingle() is <0 or >8)return false;
        if(!cell.TryGetProperty("oh",out var height)||height.ValueKind==JsonValueKind.Null)
        {if(authoredWalls&&kind==1)return false;}
        else if(height.ValueKind!=JsonValueKind.Number||!height.TryGetSingle(out float value)||!float.IsFinite(value)||value is <.15f or >8)return false;
        if(!cell.TryGetProperty("below",out var below)||below.ValueKind==JsonValueKind.Null)return true;
        if(!allowBelow||kind!=0||!Cell(below,false,false)||below.GetProperty("k").GetInt32()==1)return false;
        float lowerTop=below.GetProperty("k").GetInt32()==4?Math.Max(below.GetProperty("rf").GetSingle(),below.GetProperty("rt").GetSingle()):below.GetProperty("t").GetSingle();
        return lowerTop<tier;
    }
    private static bool Underpasses(JsonElement stage,int count)
    {
        if(!stage.TryGetProperty("underpasses",out _))return stage.GetProperty("id").GetString()!="top-rules";
        return Array(stage,"underpasses",count,index=>
        {
            if(index.ValueKind!=JsonValueKind.Number||!index.TryGetInt32(out int value)||value<0||value>=count)return false;
            var cell=stage.GetProperty("cells")[value];
            return cell.GetProperty("k").GetInt32()==0&&cell.GetProperty("t").GetInt32()>0;
        });
    }
    private static bool Array(JsonElement d,string name,int maximum,Func<JsonElement,bool> valid) =>
        d.TryGetProperty(name,out var array)&&array.ValueKind==JsonValueKind.Array&&array.GetArrayLength()<=maximum&&array.EnumerateArray().All(valid);
    private static bool Fields(JsonElement d,string schema)
    {
        if(d.ValueKind!=JsonValueKind.Object)return false;
        foreach(var field in schema.Split(' '))
        {
            int colon=field.IndexOf(':');string type=field[..colon];
            if(!d.TryGetProperty(field[(colon+1)..],out var v))return false;
            if(type[0]=='?'){if(v.ValueKind==JsonValueKind.Null)continue;type=type[1..];}
            bool valid=type switch
            {
                "s"=>v.ValueKind==JsonValueKind.String&&v.GetString()!.Length<=256,
                "b"=>v.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "i"=>v.ValueKind==JsonValueKind.Number&&v.TryGetInt32(out int i)&&Math.Abs((long)i)<=1000000,
                "l"=>v.ValueKind==JsonValueKind.Number&&v.TryGetInt64(out _),
                "n"=>v.ValueKind==JsonValueKind.Number&&v.TryGetSingle(out float n)&&float.IsFinite(n)&&Math.Abs(n)<=10000,
                _=>false
            };
            if(!valid)return false;
        }
        return true;
    }
}
