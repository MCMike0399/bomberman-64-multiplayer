using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;
using static GameFixtures;

/// <summary>Flood and Hurry hazards, power-up caps, bomb modes, drops and hearts.</summary>
internal static class ItemsAndHazardsChecks
{
    public static void Run(Action<string, Action> run)
    {
        run("flood thresholds use actual elevation and temple hurry does not close columns", () =>
        {
            Assert(FloodRules.Level(59,120)==-1,"Water rose before one-minute warning.");
            Assert(FloodRules.Level(60,120)==-.75f,"Hurry water start changed.");
            Assert(!FloodRules.Submerged(0,FloodRules.Level(68.99f,120)),"Floor0 drowned too early.");
            Assert(FloodRules.Submerged(0,FloodRules.Level(69,120)),"Exact floor0 flood threshold did not drown.");
            Assert(!FloodRules.Submerged(1,FloodRules.Level(80.99f,120))&&FloodRules.Submerged(1,FloodRules.Level(81,120)),"Floor1 threshold incorrect.");
            var cells=new CellDef[9,9];for(var x=0;x<9;x++)for(var z=0;z<9;z++)cells[x,z]=CellDef.Floor0;
            cells[4,3]=CellDef.Floor1;cells[7,3]=new CellDef {Kind=CellKind.Floor,Tier=4};
            var arena=new StageLayout {Id="sunken-temple",RisingWater=true,Width=9,Height=9,Cells=cells,Spawns=[(1,3),(4,3),(7,3)],SoftBlockDensity=0};
            var match=new LocalMatch(new MatchOptions {HumanPlayers=3,BotPlayers=0,RoundSeconds=60,Wins=5},arena);
            var waterfallEvents=new HashSet<long>();
            match.Message+=json=>{var data=Data(json);if(data.TryGetProperty("events",out var events))foreach(var e in events.EnumerateArray())if(Kind(e)=="waterfall")waterfallEvents.Add(e.GetProperty("seq").GetInt64());};
            while(State(match).GetProperty("waterLevel").GetSingle()<0)Step(match,2);
            Assert(!Player(match).GetProperty("alive").GetBoolean()&&!Player(match).GetProperty("ghost").GetBoolean(),"Submerged court player survived or became ghost.");
            Assert(Player(match,1).GetProperty("alive").GetBoolean()&&Player(match,2).GetProperty("alive").GetBoolean(),"Higher tiers drowned at court threshold.");
            Assert(State(match).GetProperty("closedLeft").GetInt32()==0&&State(match).GetProperty("closedRight").GetInt32()==0,"Temple used closing columns.");
            while(State(match).GetProperty("waterLevel").GetSingle()<1)Step(match,2);
            Assert(!Player(match,1).GetProperty("alive").GetBoolean()&&Player(match,2).GetProperty("alive").GetBoolean(),"Water did not independently eliminate next tier.");
            Assert(State(match).GetProperty("phase").GetInt32()==3&&Player(match,2).GetProperty("wins").GetInt32()==1,"Last high survivor did not win flooded round.");
            Assert(waterfallEvents.Count==1,"Flood rise did not emit exactly one waterfall event.");
            Assert(Events(match).Where(e=>Kind(e)=="death").All(e=>e.GetProperty("reason").GetString()=="water"),"Flood deaths lost their water cause.");
        });
        run("power-up limits weighted choices and round inventory reset", () =>
        {
            var p=new BlastPlayer();for(var i=0;i<20;i++){PowerUpRules.Apply(p,"fire");PowerUpRules.Apply(p,"bomb");PowerUpRules.Apply(p,"heart");}
            Assert(p.FireLevel==5&&p.BombCapacity==5&&p.HasHeart,"Power-up caps broken.");
            var seedA=new Random(7);var seedB=new Random(7);var types=new Dictionary<string,int>();
            for(var i=0;i<10000;i++){var a=PowerUpRules.ChooseDrop(seedA);Assert(a==PowerUpRules.ChooseDrop(seedB),"Drop draw not deterministic.");types[a]=types.GetValueOrDefault(a)+1;}
            Assert(types.Count==12&&types["fire"]>types["remote"]&&types["bomb"]>types["heart"],"Weighted distribution missing/common items not common.");
            var arena=FlatArena([(1,3),(7,3)]);
            arena=new StageLayout {Id=arena.Id,Width=9,Height=9,Cells=arena.Cells,Spawns=arena.Spawns,SoftBlockDensity=0,
                InitialItems=[(1,3,"fire"),(1,3,"bomb"),(1,3,"remote"),(1,3,"red"),(1,3,"heart")]};
            for(var z=0;z<9;z++)arena.Cells[0,z]=CellDef.Water;
            var match=new LocalMatch(new MatchOptions {HumanPlayers=2,BotPlayers=0,Wins=5},arena);Fight(match);
            Assert(Player(match).GetProperty("fire").GetInt32()==2&&Player(match).GetProperty("bombs").GetInt32()==4&&Player(match).GetProperty("remote").GetBoolean(),"Visible pickups did not upgrade actual player.");
            Assert(Player(match).GetProperty("pickupSerial").GetInt64()==5,"Pickup animation sequence missing.");
            match.SetInput(0,new PlayerInput(-1,0));Step(match,20);match.SetInput(0,default);Step(match,94);
            Assert(State(match).GetProperty("round").GetInt32()==2,"Test did not advance round.");
            var reset=Player(match);Assert(reset.GetProperty("fire").GetInt32()==1&&reset.GetProperty("bombs").GetInt32()==3&&!reset.GetProperty("remote").GetBoolean()&&!reset.GetProperty("red").GetBoolean()&&!reset.GetProperty("heart").GetBoolean(),"Equipment leaked into next round.");
        });
        run("holding lift then bomb creates held bomb and pumps cap at six effective levels", () =>
        {
            var arena=FlatArena([(3,3)],15);
            arena=new StageLayout {Id=arena.Id,Width=arena.Width,Height=arena.Height,Cells=arena.Cells,Spawns=arena.Spawns,SoftBlockDensity=0,InitialItems=[(3,3,"fire"),(3,3,"fire")]};
            var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},arena);Fight(match);
            match.SetInput(0,new PlayerInput(0,0,false,true));Step(match,2);
            match.SetInput(0,new PlayerInput(0,0,true,true));Step(match,2);
            Assert(Player(match).GetProperty("carrying").GetString()=="bomb","Holding lift before bomb did not create held bomb.");
            for(var i=0;i<8;i++){match.SetInput(0,new PlayerInput(0,0,false,true));Step(match,2);match.SetInput(0,new PlayerInput(0,0,true,true));Step(match,2);}
            Step(match,45);
            var bomb=State(match).GetProperty("bombs")[0];var fuse=bomb.GetProperty("fuse").GetSingle();
            Assert(bomb.GetProperty("pumps").GetInt32()==3&&bomb.GetProperty("effectiveLevel").GetInt32()==6,"Mega bomb pump/range cap broken.");
            Assert(Player(match).GetProperty("pumpSerial").GetInt64()==3,"Pump animation signaled ineffective pumps.");
            Assert(bomb.GetProperty("carrierId").GetString()=="local-0"&&bomb.GetProperty("ownerId").GetString()=="local-0","Held bomb ownership missing.");
            Step(match,100);Assert(State(match).GetProperty("bombs")[0].GetProperty("fuse").GetSingle()==fuse,"Carried bomb fuse advanced.");
            match.SetInput(0,new PlayerInput(0,0));Step(match,2);
            bomb=State(match).GetProperty("bombs")[0];Assert(bomb.GetProperty("state").GetInt32()==3&&bomb.GetProperty("carrierId").ValueKind==JsonValueKind.Null,"Release did not throw and detach carrier.");
            Assert(bomb.GetProperty("fuse").GetSingle()<fuse,"Thrown bomb fuse did not resume.");
            var exploded=false;match.Message+=json=>{var d=Data(json);if(d.TryGetProperty("hazards",out var h)&&h.GetArrayLength()>0)exploded=true;};
            Step(match,100);Assert(exploded,"Pumped thrown bomb never exploded.");
        });
        run("separate remote and red bomb chain reactions observe cover and piercing", () =>
        {
            foreach(var shield in new[]{false,true})foreach(var red in new[]{false,true})
            {
                var cells=FlatArena([(1,4)]).Cells;
                var gear=new List<(int,int,string)> {(1,4,red?"red":"remote"),(1,4,"fire"),(1,4,"fire")};
                var arena=new StageLayout {Id="fixture",Width=9,Height=9,Cells=cells,Spawns=[(1,4)],SoftBlockDensity=0,InitialItems=gear,
                    FixedSoftBlocks=shield?new HashSet<(int,int)> {(2,4)}:new HashSet<(int,int)>()};
                var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},arena);Fight(match);
                match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,2);
                var firstBombZ=State(match).GetProperty("bombs")[0].GetProperty("z").GetSingle();
                Walk(match,1.5f,2.5f);
                Assert(Math.Abs(State(match).GetProperty("bombs")[0].GetProperty("z").GetSingle()-firstBombZ)<.01f,"Walking away accidentally kicked the bomb behind the player.");
                Walk(match,3.5f,2.5f);Walk(match,3.5f,4.5f);
                match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,4);
                var placed=State(match).GetProperty("bombs").EnumerateArray().ToArray();
                Assert(placed.Length==2,"Bombs expired before the chain-reaction fixture was ready.");
                Assert(placed.All(b=>b.GetProperty("red").GetBoolean()==red&&b.GetProperty("remote").GetBoolean()!=red),"Fixture combined special modes or selected the wrong bomb type.");
                if(red)
                {
                    // Red retains its normal fuse; it no longer borrows remote control to
                    // make this fixture convenient. Inspect the first explosion so the
                    // second bomb cannot pass merely by reaching its own fuse later.
                    for(var ticks=0;ticks<300&&State(match).GetProperty("hazards").GetArrayLength()==0;ticks++)Step(match,1);
                }
                else {match.SetInput(0,new PlayerInput(0,0,false,false,true));Step(match,2);}
                var hazards=State(match).GetProperty("hazards").GetArrayLength();
                Assert(hazards==(shield&&!red?1:2),"Chain reaction ignored visibility or red cover piercing: shield="+shield+" red="+red+" hazards="+hazards+" state="+State(match));
                if(shield)
                {
                    var bits=Convert.FromBase64String(State(match).GetProperty("softBlocks").GetString()!);
                    Assert((bits[(4*9+2)/8]&(1<<((4*9+2)%8)))==0,"Explosion failed to remove destructible cover.");
                }
            }
        });
        run("actual destructible drops use seeded weighted item selection and can be collected", () =>
        {
            var sawDrop=false;var sawEmpty=false;
            for(var seed=0;seed<20;seed++)
            {
                var prediction=new Random(seed);var expected=prediction.NextDouble()<PowerUpRules.DropChance?PowerUpRules.ChooseDrop(prediction):null;
                var arena=new StageLayout {Id="fixture",Width=9,Height=9,Cells=FlatArena([(1,3)]).Cells,Spawns=[(1,3)],SoftBlockDensity=0,FixedSoftBlocks=new HashSet<(int,int)>{(2,3)}};
                var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true,Seed=seed},arena);Fight(match);
                match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,90);
                var drops=State(match).GetProperty("items").EnumerateArray().ToArray();
                Assert(drops.Length==(expected is null?0:1),"Seeded destructible item-drop probability diverged.");
                if(expected is null){sawEmpty=true;continue;}sawDrop=true;
                Assert(drops[0].GetProperty("type").GetString()==expected,"Actual drop used wrong weighted item type.");
                Walk(match,2.5f,3.5f);
                Assert(Player(match).GetProperty("lastPickup").GetString()==expected&&State(match).GetProperty("items").GetArrayLength()==0,"Ordinary movement did not collect destructible's item.");
            }
            Assert(sawDrop&&sawEmpty,"Seed coverage missed drop/no-drop cases.");
        });
        run("heart pickup absorbs exactly one actual blast", () =>
        {
            var arena=new StageLayout {Id="fixture",Width=9,Height=9,Cells=FlatArena([(1,3)]).Cells,Spawns=[(1,3),(2,3),(7,7)],SoftBlockDensity=0,
                InitialItems=[(2,3,"heart")]};
            var match=new LocalMatch(new MatchOptions {HumanPlayers=3,BotPlayers=0,Wins=5},arena);Fight(match);
            Assert(Player(match,1).GetProperty("heart").GetBoolean(),"Heart pickups not collected.");
            match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,90);
            var guarded=Player(match,1);Assert(guarded.GetProperty("alive").GetBoolean()&&!guarded.GetProperty("ghost").GetBoolean()&&!guarded.GetProperty("heart").GetBoolean(),"Heart did not absorb the first blast and disappear.");
            Step(match,70);match.SetInput(1,new PlayerInput(0,0,true));Step(match,2);match.SetInput(1,default);Step(match,90);
            Assert(Player(match,1).GetProperty("ghost").GetBoolean(),"A second heart stacked or a second blast failed to eliminate player.");
        });
        run("legacy arenas retain closing columns while practice disables every hurry hazard", () =>
        {
            var legacy=new LocalMatch(new MatchOptions {Stage="rock-garden",HumanPlayers=2,BotPlayers=0,RoundSeconds=60});Step(legacy,110);
            Assert(State(legacy).GetProperty("hurryMode").GetString()=="columns"&&State(legacy).GetProperty("closedLeft").GetInt32()>0,"Legacy sudden death lost closing columns.");
            var practice=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,RoundSeconds=60,Practice=true});Step(practice,4000);
            Assert(State(practice).GetProperty("hurryMode").GetString()=="none"&&State(practice).GetProperty("waterLevel").GetSingle()==-1,"Practice flooded during indefinite training.");
        });
    }
}
