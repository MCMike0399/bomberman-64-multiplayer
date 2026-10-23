using System.Diagnostics;
using System.Text.Json;
using Bomber.Game.Simulation;
using Bomber.Game.Simulation.Ai;

if(args.Contains("--stock-human-pressure")){StockHumanPressureChecks.Run(args);return;}
if(args.Contains("--pressure")){PressureChecks.Run(args);return;}
if(args.Contains("--liveness")){NavigationLivenessChecks.Run();return;}
if(args.Contains("--soak")){GameplaySoakChecks.Run(args);return;}

if(args.Contains("--benchmark"))
{
    var results=new List<object>();
    foreach(var stageId in new[]{"rock-garden","up-and-down","sunken-temple"})
    {
        // Warm the JIT separately. Network encoding and rendering are outside this AI/authority measurement.
        var warm=new Fixture(BlastStages.Get(stageId));warm.Step(120);
        foreach(int seed in new[]{64,123,9876})
        {
            var fixture=new Fixture(BlastStages.Get(stageId),seed:seed);
            var session=fixture.Session;
            var tickTimes=new List<double>(1800);int activeTicks=0,liveBotTicks=0;long allocated=GC.GetAllocatedBytesForCurrentThread();var clock=Stopwatch.StartNew();
            int bombs=0,kills=0,stuck=0,blocked=0,upgrades=0;var known=new HashSet<string>();var previous=new Dictionary<string,(float X,float Z)>();var powers=new Dictionary<string,int>();
            for(int tick=0;tick<1800;tick++)
            {
                bool active=fixture.State!=Burbuja.Realtime.Abstractions.LobbyState.Ended && session.AiTestPhase is RoundPhase.Fight or RoundPhase.Hurry;
                int live=0;if(active)foreach(var player in session.AiTestPlayers)if(player.Alive&&!player.Ghost)live++;
                long begin=Stopwatch.GetTimestamp();fixture.Step();
                if(active){activeTicks++;liveBotTicks+=live;tickTimes.Add(Stopwatch.GetElapsedTime(begin).TotalMicroseconds);}
                foreach(var bomb in session.AiTestBombs)if(known.Add(bomb.Id))bombs++;
                foreach(var p in session.AiTestPlayers)
                {
                    int power=p.FireLevel+p.BombCapacity+(p.HasHeart?1:0)+(p.HasRed?1:0)+(p.HasRemote?1:0);
                    if(powers.TryGetValue(p.Id,out int old)&&power>old)upgrades+=power-old;powers[p.Id]=power;
                }
                if(tick%30==0)
                    foreach(var p in session.AiTestPlayers)
                    {
                        if(previous.TryGetValue(p.Id,out var last)&&p.Alive&&!p.Ghost&&Math.Abs(last.X-p.X)+Math.Abs(last.Z-p.Z)<.1f){stuck++;if(Math.Abs(p.Input.X)+Math.Abs(p.Input.Z)>.1f)blocked++;}
                        previous[p.Id]=(p.X,p.Z);
                    }
            }
            clock.Stop();allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
            kills=session.AiTestPlayers.Count(p=>!p.Alive||p.Ghost);
            tickTimes.Sort();
            results.Add(new{stage=stageId,seed,ticks=1800,bots=8,activeTicks,liveBotTicks,thinkCalls=session.AiTestThinkCount,p95ActiveTickMicroseconds=tickTimes.Count>0?tickTimes[(int)(tickTimes.Count*.95)]:0,maxActiveTickMicroseconds=tickTimes.Count>0?tickTimes[^1]:0,milliseconds=Math.Round(clock.Elapsed.TotalMilliseconds,2),allocatedBytes=allocated,bombs,deadAtEnd=kills,stationarySamples=stuck,blockedMovementSamples=blocked,powerUpTransitions=upgrades});
        }
    }
    var json=JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true});Console.WriteLine(json);
    var output=args.FirstOrDefault(a=>a.StartsWith("--output="));if(output!=null)File.WriteAllText(output[9..],json+"\n");
    return;
}
var checks=FrontPlacementChecks.Run()+NavigationLivenessChecks.Run()+PressureChecks.Run(Array.Empty<string>())+StockHumanPressureChecks.Run(Array.Empty<string>());
void Check(bool passed,string name){if(!passed)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
void Fight(Fixture f){f.Step(62);}

var flat=Fixture.Flat();var graph=NavigationGraph.For(flat);
Check(ReferenceEquals(graph,NavigationGraph.For(flat)),"static walk graph is cached per immutable arena");
var temple=BlastStages.Get("sunken-temple");var templeGraph=NavigationGraph.For(temple);
for(int cell=0;cell<templeGraph.Count;cell++)for(int d=0;d<4;d++)
{
    int next=templeGraph.Edges[cell*4+d];
    if(next>=0&&!temple.CanWalk(cell%temple.Width,cell/temple.Width,next%temple.Width,next/temple.Width))throw new Exception("AI graph invents an uphill cliff traversal");
}
Check(true,"all cached temple edges agree with authoritative ramps and directed cliff drops");
var soft=new SoftBlockGrid(flat.Width,flat.Height);var danger=new TimedDangerField(graph,flat);
var chain=new[]{new BlastBomb {Id="early",X=3.5f,Z=3.5f,CellX=3,CellZ=3,FuseTimer=.45f},new BlastBomb {Id="late",X=4.5f,Z=3.5f,CellX=4,CellZ=3,FuseTimer=2.5f}};
danger.Build(soft,chain,Array.Empty<BlastHazard>());
int target=graph.Cell(5.5f,3.5f);
Check(!danger.Safe(target,.55f,.8f),"a short fuse advances a neighboring bomb's predicted chain time");
Check(danger.Safe(target,1.1f,1.3f),"an expired chain hazard releases its route rather than blocking it forever");
Check(danger.Safe(target,0,.3f),"timed planning allows crossing before a visible fuse instead of treating all bombs as active blasts");
var fringeChain=new[]{new BlastBomb{Id="fringe-early",X=3.5f,Z=3.5f,FuseTimer=.45f},new BlastBomb{Id="fringe-late",X=5.2f,Z=3.5f,FuseTimer=2.5f}};
danger.Build(soft,fringeChain,Array.Empty<BlastHazard>());
Check(danger.Begins[3*flat.Width+6]<.5f,"danger includes the authority's half-cell bomb chain margin");
danger.Build(soft,Array.Empty<BlastBomb>(),new[]{new BlastHazard {Id="blast",X=5.5f,Z=3.5f,Y=0,Radius=1.3f,LifeTimer=.4f}});
Check(!danger.Safe(target,0,.2f)&&danger.Safe(target,.6f,.8f),"existing blast zones remain unsafe for their real remaining lifetime");

var resources=new Fixture(Fixture.Flat(spawns:[(2,2),(10,10)]),bots:2);Fight(resources);resources.Session.AiTestAddItem(1,2,"heart");resources.Step(25);
Check(resources.Session.AiTestPlayers.First(p=>p.Slot==0).HasHeart,"a reachable power-up is deliberately collected before chasing a distant opponent");

var escape=new Fixture(Fixture.Flat(spawns:[(3,5),(10,10)]),bots:2);Fight(escape);
escape.Session.AiTestAddBomb(new BlastBomb {Id="threat",OwnerId="human",X=4.5f,Z=5.5f,CellX=4,CellZ=5,FuseTimer=.9f});
escape.Step(40);
var survivor=escape.Session.AiTestPlayers.First(p=>p.Slot==0);
Check(survivor.Alive&&!survivor.Ghost,"normal bot escapes an approaching visible fuse through reachable safe terrain");

var flood=new Fixture(temple,bots:2,suddenDeath:true);Fight(flood);
flood.Session.AiTestPosition("cpu-0",6.5f,15.5f);flood.Session.AiTestPosition("cpu-1",10.5f,3.5f);
flood.Session.AiTestFightTime(263);flood.Step(100);
var climber=flood.Session.AiTestPlayers.First(p=>p.Slot==0);
Check(climber.Alive&&climber.Y>=1,"bot climbs out of the low court before the forecast water level submerges it");

int Decisions(string level){var f=new Fixture(Fixture.Flat(spawns:[(2,2),(10,10)]),2,difficulty:level);Fight(f);int before=f.Session.AiTestThinkCount;f.Step(30);return f.Session.AiTestThinkCount-before;}
int easy=Decisions("easy"),normal=Decisions("normal"),hard=Decisions("hard");
Check(easy<normal&&normal<hard,$"difficulty changes reaction opportunities fairly (easy={easy},normal={normal},hard={hard})");

// Force exactly one tactical decision from identical visible positions; reaction timing cannot explain it.
float SpacingDecision(string level)
{
    var f=new Fixture(Fixture.Flat(spawns:[(2,2),(10,10)]),2,difficulty:level);Fight(f);
    f.Session.AiTestPosition("cpu-0",5.5f,6.5f);f.Session.AiTestPosition("cpu-1",8.5f,6.5f);
    f.Session.AiTestThink("cpu-0");var input=f.Session.AiTestPlayers.First(p=>p.Slot==0).Input;return input.X-Math.Abs(input.Z);
}
float easySpacing=SpacingDecision("easy"),mediumSpacing=SpacingDecision("medium"),hardSpacing=SpacingDecision("hard");
Check(easySpacing<0&&hardSpacing>0,$"same visible opponent makes Easy give space and Hard contest range in one decision ({easySpacing}, {mediumSpacing}, {hardSpacing})");

float TrapDecision(string level)
{
    var f=new Fixture(Fixture.Flat(spawns:[(5,5),(7,3)]),2,difficulty:level);
    f.Session.AiTestPosition("cpu-0",5.5f,5.5f);f.Session.AiTestPosition("cpu-1",7.5f,3.5f);
    foreach(var (x,z) in new[]{(4,4),(5,3),(6,4)})f.Session.AiTestSetSoft(x,z,true);
    f.Session.AiTestAddItem(5,4,"heart");f.Session.AiTestAddItem(6,5,"heart");
    f.Session.AiTestThink("cpu-0");return f.Session.AiTestPlayers.First(p=>p.Slot==0).Input.X;
}
float easyTrap=TrapDecision("easy"),mediumTrap=TrapDecision("medium"),hardTrap=TrapDecision("hard");
Check(easyTrap==0&&mediumTrap>0&&hardTrap>0,$"equal visible pickups make Medium and Hard favor the open route over a dead end ({easyTrap}, {mediumTrap}, {hardTrap})");

float RiskDecision(string level)
{
    var f=new Fixture(Fixture.Flat(spawns:[(5,5),(7,3)]),2,difficulty:level);
    f.Session.AiTestPosition("cpu-0",5.5f,5.5f);f.Session.AiTestPosition("cpu-1",7.5f,3.5f);
    foreach(var (x,z) in new[]{(4,4),(5,3),(6,4),(2,5)})f.Session.AiTestSetSoft(x,z,true);
    f.Session.AiTestAddItem(5,4,"heart");f.Session.AiTestAddItem(3,5,"heart");
    f.Session.AiTestThink("cpu-0");return f.Session.AiTestPlayers.First(p=>p.Slot==0).Input.X;
}
float mediumRisk=RiskDecision("medium"),hardRisk=RiskDecision("hard");
Check(mediumRisk==0&&hardRisk<0,$"Hard trades a shorter contested pickup for a longer escape-friendly route ({mediumRisk}, {hardRisk})");

foreach(var state in new[]{BombState.Rolling,BombState.Flying})
{
    var f=new Fixture(Fixture.Flat(spawns:[(3,5),(9,10)]),2);
    f.Session.AiTestPosition("cpu-0",3.5f,5.5f);f.Session.AiTestPosition("cpu-1",9.5f,10.5f);
    var oldest=new BlastBomb {Id="oldest-moving",OwnerId="cpu-0",IsRemote=true,State=state,X=4.5f,Z=5.5f,CellX=4,CellZ=5};
    f.Session.AiTestAddBomb(oldest);
    f.Session.AiTestAddBomb(new BlastBomb {Id="second-resting",OwnerId="cpu-0",IsRemote=true,X=9.5f,Z=9.5f,CellX=9,CellZ=9});
    Check(!f.Session.AiTestRemoteOpportunity("cpu-0"),$"{state} oldest remote threatening its owner prevents detonating a safe second remote");
    oldest.X=9.5f;oldest.Z=9.5f;oldest.CellX=9;oldest.CellZ=9;
    Check(f.Session.AiTestRemoteOpportunity("cpu-0"),$"{state} oldest remote threatening only an opponent remains eligible for detonation");
}

foreach(string stage in new[]{"rock-garden","up-and-down","sunken-temple"})
{
    var a=new Fixture(BlastStages.Get(stage),seed:9876);var b=new Fixture(BlastStages.Get(stage),seed:9876);
    for(int tick=0;tick<900;tick++)
    {
        a.Step();b.Step();
        var pa=a.Session.AiTestPlayers.ToArray();var pb=b.Session.AiTestPlayers.ToArray();
        for(int i=0;i<pa.Length;i++)if(pa[i].X!=pb[i].X||pa[i].Z!=pb[i].Z||pa[i].Alive!=pb[i].Alive||pa[i].Input!=pb[i].Input)throw new Exception("AI replay divergence at "+stage+" tick "+tick);
    }
    Check(true,"eight-bot fixed-seed replay is deterministic on "+stage);
}
foreach(string level in new[]{"easy","medium","hard"})
{
    var arena=Fixture.Flat(spawns:[(3,6),(9,6)]);var f=new Fixture(arena,2,difficulty:level);
    f.Session.AiTestAddHuman("cpu-1",1);f.Session.AiTestPosition("cpu-1",9.5f,6.5f);
    var opponent=f.Session.AiTestPlayers.First(p=>p.Slot==1);
    var attackIds=new HashSet<string>();bool exploded=false;
    for(int tick=0;tick<900;tick++)
    {
        f.Step();
        foreach(var bomb in f.Session.AiTestBombs)
            if(bomb.OwnerId=="cpu-0"&&BlastGeometry.Contains(arena,new SoftBlockGrid(13,13),bomb.X,0,bomb.Z,bomb.BlastRadius,opponent.X,opponent.Y,opponent.Z,bomb.IsRed))attackIds.Add(bomb.Id);
        if(attackIds.Count>0&&f.Session.AiTestHazards.Any(h=>BlastGeometry.Contains(arena,new SoftBlockGrid(13,13),h.X,h.Y,h.Z,h.Radius,opponent.X,opponent.Y,opponent.Z,h.IsRed))){exploded=true;break;}
    }
    Check(attackIds.Count>0&&exploded,$"{level} approaches a stationary visible opponent and explodes a real pressure bomb within 30 seconds");
}
var combat=new Fixture(Fixture.Flat(),8);var fresh=new HashSet<string>();int pressure=0;
for(int tick=0;tick<360;tick++)
{
    combat.Step();
    foreach(var bomb in combat.Session.AiTestBombs)if(fresh.Add(bomb.Id))
        foreach(var opponent in combat.Session.AiTestPlayers)
            if(opponent.Id!=bomb.OwnerId&&opponent.Alive&&!opponent.Ghost&&BlastGeometry.Contains(Fixture.Flat(),new SoftBlockGrid(13,13),bomb.X,0,bomb.Z,bomb.BlastRadius,opponent.X,opponent.Y,opponent.Z,bomb.IsRed)){pressure++;break;}
}
Check(pressure>=2,"bots create repeated bomb pressure against reachable opponents, not only stationary cover");
var quiet=new Fixture(Fixture.Flat(spawns:[(2,2),(10,10)]),2);Fight(quiet);
for(int i=0;i<200;i++)quiet.Session.AiTestThink("cpu-0");
long beforeAlloc=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)quiet.Session.AiTestThink("cpu-0");long allocation=GC.GetAllocatedBytesForCurrentThread()-beforeAlloc;
Check(allocation<4096,"1000 steady-state path decisions reuse search memory ("+allocation+" bytes)");
Console.WriteLine($"PASS: {checks} AI behavior, timing, determinism and allocation checks");
