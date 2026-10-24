using Godot;
using Bomber.Client;
using Bomber.Game;
using Bomber.Game.Simulation;
using BlastCircuit.Arena.Models;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task LootOpening()
    {
        Event("loot-opening-input-policy",new {stage="stock Sunken Temple",seed=64,humans=2,bots=0,practice=false,stateMutation=false,input="ordinary authoritative movement and two-frame primary tap"});
        Input(0,default);Input(1,default);
        _root.StartLocal(new SessionSettings(Stage:BlastStages.SunkenTemple,Bots:0){Advanced=new AdvancedRules{DropPercent=100,DisabledItems="remote,red,bounce,fast,heart,focus,tiny,big,haste,slow"}},2);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"normal two-human temple reaches fight for opening loot",150);
        Check(State.GetProperty("items").GetArrayLength()==0,"normal battle starts without a training pickup cache");
        var stage=BlastStages.Get(BlastStages.SunkenTemple);
        var bits=Convert.FromBase64String(State.GetProperty("softBlocks").GetString()!);
        bool Covered((int X,int Z) cell){int index=cell.Z*stage.Width+cell.X;return (bits[index/8]&(1<<(index%8)))!=0;}
        var start=((int)GrappleValue(GrapplePlayer(0),"x"),(int)GrappleValue(GrapplePlayer(0),"z"));
        var reachable=LootReachable(stage,start,Covered,6);
        var opening=reachable.SelectMany(from=>new[]{(1,0),(-1,0),(0,1),(0,-1)}.Select(direction=>
            (From:from.Key,Cover:(X:from.Key.X+direction.Item1,Z:from.Key.Z+direction.Item2),Distance:from.Value)))
            .Where(candidate=>stage.At(candidate.Cover.X,candidate.Cover.Z).Kind==CellKind.Floor&&Covered(candidate.Cover)&&
                stage.CanCross(candidate.From.X,candidate.From.Z,candidate.Cover.X,candidate.Cover.Z)&&
                stage.At(candidate.From.X,candidate.From.Z).Tier==stage.At(candidate.Cover.X,candidate.Cover.Z).Tier)
            .OrderBy(candidate=>candidate.Distance).First();
        var retreat=LootReachable(stage,opening.From,Covered,4).Where(candidate=>
            (candidate.Key.X-opening.From.X)*(candidate.Key.X-opening.From.X)+(candidate.Key.Z-opening.From.Z)*(candidate.Key.Z-opening.From.Z)>=9)
            .OrderBy(candidate=>candidate.Value).First().Key;
        Event("loot-opening-route",new {approach=new {opening.From.X,opening.From.Z},cover=new {opening.Cover.X,opening.Cover.Z},retreat=new {retreat.X,retreat.Z},walkDistance=opening.Distance});
        await GrappleWalk(0,opening.From.X,opening.From.Z);
        int fireBefore=Player.GetProperty("fire").GetInt32(),bombsBefore=Player.GetProperty("bombs").GetInt32();
        long pickupBefore=Player.GetProperty("pickupSerial").GetInt64();
        Input(0,new PlayerInput(opening.Cover.X-opening.From.X,opening.Cover.Z-opening.From.Z));await Frames(1);
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(2);
        Check(State.GetProperty("bombs").GetArrayLength()==1,"ordinary primary tap plants one near-foot opening bomb");
        var bomb=State.GetProperty("bombs")[0];float bx=bomb.GetProperty("x").GetSingle(),bz=bomb.GetProperty("z").GetSingle();
        await GrappleWalk(0,retreat.X,retreat.Z);
        Check(Mathf.Pow(Player.GetProperty("x").GetSingle()-bx,2)+Mathf.Pow(Player.GetProperty("z").GetSingle()-bz,2)>4,
            "ordinary movement retreats safely from the planted bomb before its fuse expires");
        bool sawHazard=false;
        await WaitState(s=>{sawHazard|=s.GetProperty("hazards").GetArrayLength()>0;return s.GetProperty("bombs").GetArrayLength()==0&&s.GetProperty("hazards").GetArrayLength()==0;},
            "opening bomb completes its full fuse and blast hazard lifetime",150);
        Check(sawHazard,"normal fuse produces an actual authoritative explosion hazard");
        Check(Player.GetProperty("alive").GetBoolean()&&!Player.GetProperty("ghost").GetBoolean(),"player survives the ordinary opening bomb and its complete blast");
        bits=Convert.FromBase64String(State.GetProperty("softBlocks").GetString()!);
        Check(!Covered(opening.Cover),"ordinary planted bomb destroys the near-spawn cover");
        var reward=State.GetProperty("items").EnumerateArray().Single(item=>item.GetProperty("cx").GetInt32()==opening.Cover.X&&item.GetProperty("cz").GetInt32()==opening.Cover.Z);
        string kind=reward.GetProperty("type").GetString()!;
        Check(kind is "fire" or "bomb","useful opening reward survives the entire blast instead of disappearing with its crate");
        await Frames(6);
        var model=_root.FindChildren("*","",true,false).OfType<PowerupModel>().Single(node=>node.Kind==kind&&
            Math.Abs(node.GlobalPosition.X-opening.Cover.X-.5f)<.01f&&Math.Abs(node.GlobalPosition.Z-opening.Cover.Z-.5f)<.01f);
        Check(model.Visible&&model.GetNode<Node3D>("Icon").Scale.IsEqualApprox(Vector3.One*1.30f),"surviving reward renders the actual enlarged icon above its unchanged plate");
        var battleCamera=_root.GetViewport().GetCamera3D();Camera3D? inspection=null;
        if(_scenario=="loot-opening")
        {
            inspection=new Camera3D {Fov=48,Near=.05f,Far=100};_root.AddChild(inspection);
            var target=model.GlobalPosition+Vector3.Up*.2f;inspection.Position=target+new Vector3(3,4.5f,7);inspection.LookAt(target);inspection.MakeCurrent();await Frames(4);
        }
        await Capture("loot-opening-surviving-icon");
        await GrappleWalk(0,opening.Cover.X,opening.Cover.Z);
        Check(Player.GetProperty("pickupSerial").GetInt64()==pickupBefore+1&&Player.GetProperty("lastPickup").GetString()==kind,
            "ordinary walking collects exactly the opening reward through the real pickup counter");
        Check(Player.GetProperty("fire").GetInt32()==fireBefore+(kind=="fire"?1:0)&&Player.GetProperty("bombs").GetInt32()==bombsBefore+(kind=="bomb"?1:0),
            "collected opening reward increases the matching actual inventory by one");
        Check(!State.GetProperty("items").EnumerateArray().Any(item=>item.GetProperty("id").GetString()==reward.GetProperty("id").GetString()),
            "collected opening reward leaves the authoritative item list");
        await Frames(4);Check(!GodotObject.IsInstanceValid(model),"collected opening reward removes its real rendered pickup model");
        await Capture("loot-opening-collected-upgrade");
        if(inspection!=null){battleCamera?.MakeCurrent();inspection.QueueFree();}
        _root.Back();await Frames(3);
    }
    private static Dictionary<(int X,int Z),int> LootReachable(StageLayout stage,(int X,int Z) start,Func<(int X,int Z),bool> covered,int limit)
    {
        var found=new Dictionary<(int X,int Z),int>{{start,0}};var queue=new Queue<(int X,int Z)>();queue.Enqueue(start);
        while(queue.TryDequeue(out var cell))
        {
            if(found[cell]>=limit)continue;
            foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
            {
                var next=(X:cell.X+dx,Z:cell.Z+dz);
                if(found.ContainsKey(next)||!stage.CanCross(cell.X,cell.Z,next.X,next.Z)||covered(next))continue;
                found[next]=found[cell]+1;queue.Enqueue(next);
            }
        }
        return found;
    }
}
