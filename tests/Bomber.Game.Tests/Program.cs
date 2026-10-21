using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;

var passed = BombPlacementChecks.Run() + PowerUpModeChecks.Run() + BombTravelChecks.Run() + KickTimingChecks.Run() + BodyContactChecks.Run() + BombImpactChecks.Run() + TraversalAndGhostChecks.Run() + BombActionsChecks.Run() + GrappleChecks.Run();
Run("participant bounds and stage validation", () =>
{
    foreach (var options in new[] {
        new MatchOptions { HumanPlayers = 0 }, new MatchOptions { HumanPlayers = 8, BotPlayers = 1 },
        new MatchOptions { HumanPlayers = 1, BotPlayers = 0 }, new MatchOptions { Stage = "missing" },
        new MatchOptions { Wins = 0 }, new MatchOptions { RoundSeconds = 59 } })
    {
        var threw = false;
        try { _ = new LocalMatch(options); } catch (ArgumentException) { threw = true; }
        Assert(threw, "Invalid options must be rejected.");
    }
});
Run("eight humans have unique seats and spawns", () =>
{
    var match = new LocalMatch(new MatchOptions { HumanPlayers = 8, BotPlayers = 0 });
    var players = State(match).GetProperty("players").EnumerateArray().ToArray();
    Assert(players.Length == 8, "Expected eight humans.");
    Assert(players.All(p => !p.GetProperty("bot").GetBoolean()), "Human seats became bots.");
    Assert(players.Select(p => p.GetProperty("slot").GetInt32()).Distinct().Count() == 8, "Duplicate seat.");
    Assert(players.Select(p => (p.GetProperty("x").GetSingle(), p.GetProperty("z").GetSingle())).Distinct().Count() == 8, "Duplicate spawn.");
});
Run("stage payload preserves raised terrain and complete soft grid", () =>
{
    var match = new LocalMatch(new MatchOptions { Stage = "up-and-down" });
    var stage = Data(match.StageJson!);
    var width = stage.GetProperty("width").GetInt32();
    var height = stage.GetProperty("height").GetInt32();
    var cells = stage.GetProperty("cells").EnumerateArray().ToArray();
    Assert(cells.Length == width * height, "Incomplete stage grid.");
    Assert(cells.Any(c => c.GetProperty("t").GetInt32() > 0), "No raised terrain.");
    Assert(cells.Any(c => c.GetProperty("k").GetInt32() == 4), "No ramp.");
    Assert(Convert.FromBase64String(State(match).GetProperty("softBlocks").GetString()!).Length > 0, "Empty soft grid.");
});
Run("seeded bots and human inputs replay deterministically", () =>
{
    var options = new MatchOptions { BotPlayers = 7, Seed = 9876 };
    var a = new LocalMatch(options); var b = new LocalMatch(options);
    for (var i = 0; i < 1800; i++)
    {
        var input = new PlayerInput(i % 90 < 45 ? .3f : -.3f, 0, i % 45 == 0);
        a.SetInput(0, input); b.SetInput(0, input); a.Tick(); b.Tick();
        Assert(a.StateJson == b.StateJson, $"Replay diverged at tick {i}.");
        Assert(a.IsEnded == b.IsEnded, "End state diverged.");
    }
});
Run("bomb press is edge triggered and explodes after fuse", () =>
{
    var match = TwoHumans();
    Fight(match);
    match.SetInput(0, new PlayerInput(0, 0, true)); Step(match, 2);
    Assert(State(match).GetProperty("bombs").GetArrayLength() == 1, "Bomb was not placed.");
    match.SetInput(0,default);Step(match,20);
    Assert(State(match).GetProperty("bombs").GetArrayLength() == 1, "One A tap placed extra bombs.");
    var sawHazard = false;
    match.Message += json => { if (Data(json).TryGetProperty("hazards", out var hazards) && hazards.GetArrayLength() > 0) sawHazard = true; };
    Step(match, 70);
    Assert(sawHazard, "Bomb never generated a spherical explosion.");
    Assert(State(match).GetProperty("bombs").GetArrayLength() == 0, "Bomb survived its fuse.");
    Assert(State(match).GetProperty("players")[0].GetProperty("ghost").GetBoolean(), "Explosion did not turn its victim into a ghost.");
});
Run("nonfinite input is safe and movement acknowledges input", () =>
{
    var match = TwoHumans(); Fight(match);
    var before = State(match).GetProperty("players")[0].GetProperty("x").GetSingle();
    match.SetInput(0, new PlayerInput(float.NaN, float.PositiveInfinity)); Step(match, 2);
    var player = State(match).GetProperty("players")[0];
    Assert(player.GetProperty("x").GetSingle() == before, "Nonfinite input moved player.");
    match.SetInput(0, new PlayerInput(1, 0)); Step(match, 4);
    player = State(match).GetProperty("players")[0];
    Assert(player.GetProperty("x").GetSingle() > before, "Valid movement did not move player.");
    Assert(player.GetProperty("ack").GetInt64() > 0, "Input not acknowledged.");
});
Run("round timeout draws and starts a fresh round", () =>
{
    var match = new LocalMatch(new MatchOptions { HumanPlayers = 2, BotPlayers = 0, RoundSeconds = 60, SuddenDeath = false });
    Step(match, 2000);
    var state = State(match);
    Assert(state.GetProperty("round").GetInt32() == 2, "Draw did not advance round.");
    Assert(state.GetProperty("players").EnumerateArray().All(p => p.GetProperty("wins").GetInt32() == 0), "Draw awarded a win.");
});
Run("vertical terrace walls cannot be walked through", () =>
{
    var arena = StepArena((3, 2));
    var match = new LocalMatch(new MatchOptions { HumanPlayers = 1, BotPlayers = 0, Practice = true }, arena);
    Fight(match); match.SetInput(0, new PlayerInput(1, 0)); Step(match, 15);
    var player = State(match).GetProperty("players")[0];
    Assert(player.GetProperty("x").GetSingle() <= 3.61f, "Walked through a terrace wall without stairs.");
    Assert(player.GetProperty("y").GetSingle() == 0, "Climbed a vertical cliff.");
});
Run("actual ramp ascent and descent publish continuous authority height", () =>
{
    var match = new LocalMatch(new MatchOptions { HumanPlayers = 1, BotPlayers = 0, Practice = true }, StepArena((2, 3)));
    Fight(match); match.SetInput(0, new PlayerInput(1, 0));
    var sawSlope = false;
    for (var i = 0; i < 16; i++)
    {
        match.Tick(); var y = State(match).GetProperty("players")[0].GetProperty("y").GetSingle();
        sawSlope |= y > .05f && y < .95f;
    }
    Assert(sawSlope, "Ramp height snapped between integer tiers.");
    Assert(State(match).GetProperty("players")[0].GetProperty("y").GetSingle() == 1, "Ramp ascent failed.");
    match.SetInput(0, new PlayerInput(-1, 0)); Step(match, 16);
    var player = State(match).GetProperty("players")[0];
    Assert(player.GetProperty("x").GetSingle() < 3 && player.GetProperty("y").GetSingle() == 0, "Ramp descent failed.");
});
Run("temple topology connects all eight spawns with sparse clear stairs", () =>
{
    var stage = BlastStages.Get("sunken-temple");
    Assert(stage.Width == 21 && stage.Height == 23, "Temple dimensions changed.");
    Assert(stage.Spawns.Distinct().Count() == 8, "Temple must support eight distinct spawns.");
    var seen = new HashSet<(int X, int Z)> { stage.Spawns[0] }; var queue = new Queue<(int X, int Z)>(); queue.Enqueue(stage.Spawns[0]);
    while (queue.TryDequeue(out var cell)) foreach (var (dx, dz) in new[] { (1,0),(-1,0),(0,1),(0,-1) })
    {
        var next = (cell.X + dx, cell.Z + dz);
        if (stage.CanCross(cell.X, cell.Z, next.Item1, next.Item2) && seen.Add(next)) queue.Enqueue(next);
    }
    Assert(stage.Spawns.All(seen.Contains), "A spawn is cut off from the shared arena.");
    Assert(seen.Contains((10,3)) && seen.Contains((10,18)), "Crown or sunken court disconnected.");
    for (var seed = 0; seed < 30; seed++)
    {
        var match = new LocalMatch(new MatchOptions { Seed = seed });
        var bits = Convert.FromBase64String(State(match).GetProperty("softBlocks").GetString()!);
        Assert(bits.Sum(b => System.Numerics.BitOperations.PopCount((uint)b)) <= 30, "Temple became a crate maze.");
        for (var z = 3; z <= 13; z++) for (var x = 9; x <= 11; x++)
            Assert((bits[(z*21+x)/8] & 1 << ((z*21+x)%8)) == 0, "Central stair route blocked by cover.");
    }
});
Run("temple actual authority inputs traverse summit and sunken court round trip", () =>
{
    var match = new LocalMatch(new MatchOptions { HumanPlayers = 1, BotPlayers = 0, Practice = true }); Fight(match);
    Walk(match, 10.5f, 3.5f);
    Assert(Player(match).GetProperty("y").GetSingle() == 4, "Did not reach crown through all three stairs.");
    Walk(match, 10.5f, 12.5f);
    Assert(Player(match).GetProperty("y").GetSingle() == 1, "Did not descend three stairs.");
    Walk(match, 6.5f, 12.5f); Walk(match, 6.5f, 17.5f);
    Assert(Player(match).GetProperty("y").GetSingle() == 0, "Did not descend into courtyard.");
    Walk(match, 6.5f, 12.5f); Walk(match, 10.5f, 12.5f);
    Assert(Player(match).GetProperty("y").GetSingle() == 1, "Did not ascend from courtyard.");
    Step(match, 10000);
    Assert(!match.IsEnded && State(match).GetProperty("round").GetInt32() == 1, "Practice ended or reset while exploring.");
});
Run("blast volumes respect altitude and stone even with piercing power", () =>
{
    var stage = FlatArena([(1,1)]); var cover = new SoftBlockGrid(stage.Width, stage.Height);
    Assert(!BlastGeometry.Contains(stage, cover, 2.5f, 0, 3.5f, 1.3f, 3.5f, 2, 3.5f), "Blast ignored vertical separation.");
    Assert(BlastGeometry.Contains(stage, cover, 2.5f, 0, 3.5f, 3, 3.5f, 2, 3.5f), "Large spherical blast did not include high target.");
    stage.Cells[3,3] = CellDef.Pillar;
    Assert(!BlastGeometry.Contains(stage, cover, 2.5f, 0, 3.5f, 4, 4.5f, 0, 3.5f), "Stone failed to occlude blast.");
    Assert(!BlastGeometry.Contains(stage, cover, 2.5f, 0, 3.5f, 4, 4.5f, 0, 3.5f, true), "Piercing blast passed hard stone.");
    stage.Cells[3,3] = CellDef.Floor2;
    Assert(!BlastGeometry.Contains(stage, cover, 2.5f, 0, 3.5f, 4, 4.5f, 0, 3.5f, true), "Blast passed raised terrain.");
    stage.Cells[3,3] = CellDef.Floor0; cover.Set(3,3,true);
    Assert(!BlastGeometry.Contains(stage, cover, 2.5f, 0, 3.5f, 4, 4.5f, 0, 3.5f), "Soft cover failed to shield target.");
    Assert(BlastGeometry.Contains(stage, cover, 2.5f, 0, 3.5f, 4, 4.5f, 0, 3.5f,true), "Piercing blast did not pass soft cover.");
    Assert(BlastGeometry.Contains(stage, cover, 2.5f, 3, 3.5f, 4, 4.5f, 3, 3.5f), "Low cover blocked a blast well above it.");
});
Run("detonation does not hit a nearby player two levels overhead", () =>
{
    var stage = FlatArena([(2,3),(3,3)]); stage.Cells[3,3] = CellDef.Floor2;
    var match = new LocalMatch(new MatchOptions { HumanPlayers = 2, BotPlayers = 0, Practice = true }, stage); Fight(match);
    match.SetInput(0,new PlayerInput(0,0,true)); Step(match,2);match.SetInput(0,default);Step(match,88);
    var high = Player(match,1);
    Assert(high.GetProperty("alive").GetBoolean() && !high.GetProperty("ghost").GetBoolean(), "Ground-level bomb killed elevated player outside its sphere.");
});
Run("bots follow actual stair route to an elevated opponent", () =>
{
    var stage = StepArena((6,3));
    stage = new StageLayout { Id=stage.Id, Width=stage.Width, Height=stage.Height, Cells=stage.Cells, Spawns=[(6,3),(1,3)] };
    var match = new LocalMatch(new MatchOptions { HumanPlayers = 1, BotPlayers = 1, Practice = true },stage); Fight(match);
    Step(match,300);
    var bot = Player(match,1);
    Assert(bot.GetProperty("y").GetSingle()==1 && bot.GetProperty("x").GetSingle()>4.5f, "Bot could not climb reachable stairs.");
    Assert(bot.GetProperty("alive").GetBoolean() && !bot.GetProperty("ghost").GetBoolean(), "Bot fell off its path.");
});
Run("bot reaches safe terrain before an adjacent bomb fuse expires", () =>
{
    var stage=FlatArena([(2,3),(3,3)]);
    var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=1,Practice=true},stage); Fight(match);
    match.SetInput(0,new PlayerInput(0,0,true));
    var reachedRefuge=false;
    for(var i=0;i<90;i++)
    {
        match.Tick(); if(i==1)match.SetInput(0,default);var state=State(match);var live=Player(match,1);
        foreach(var bomb in state.GetProperty("bombs").EnumerateArray())
            if(!BlastGeometry.Contains(stage,new SoftBlockGrid(9,9),bomb.GetProperty("x").GetSingle(),bomb.GetProperty("y").GetSingle(),bomb.GetProperty("z").GetSingle(),1.7f,
                live.GetProperty("x").GetSingle(),live.GetProperty("y").GetSingle(),live.GetProperty("z").GetSingle())) reachedRefuge=true;
    }
    var bot=Player(match,1);
    Assert(bot.GetProperty("alive").GetBoolean()&&!bot.GetProperty("ghost").GetBoolean(),"Bot failed to escape a reachable bomb threat.");
    Assert(reachedRefuge,"Bot never reached a refuge before fuse expiry: "+bot);
});
Run("bot places its own bomb only with a route to safety", () =>
{
    var arena=FlatArena([(2,3),(3,3)]);
    var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=1,Wins=5},arena);
    var sawBomb=false; var sawSafety=false;
    for(var i=0;i<160;i++)
    {
        match.Tick();var state=State(match);var bot=Player(match,1);
        foreach(var bomb in state.GetProperty("bombs").EnumerateArray())
        {
            sawBomb=true;
            if(!BlastGeometry.Contains(arena,new SoftBlockGrid(9,9),bomb.GetProperty("x").GetSingle(),bomb.GetProperty("y").GetSingle(),bomb.GetProperty("z").GetSingle(),1.7f,
                bot.GetProperty("x").GetSingle(),bot.GetProperty("y").GetSingle(),bot.GetProperty("z").GetSingle())) sawSafety=true;
        }
    }
    Assert(sawBomb,"Bot never placed a bomb near an enemy.");
    Assert(sawSafety,"Bot did not reach safety from its own bomb.");
    Assert(!Player(match,1).GetProperty("ghost").GetBoolean(),"Bot died to its own planned placement.");
});
Run("trapped bot refuses a suicidal bomb placement", () =>
{
    var cells=new CellDef[9,9];for(var x=0;x<9;x++)for(var z=0;z<9;z++)cells[x,z]=CellDef.Pillar;
    cells[3,3]=cells[4,3]=CellDef.Floor0;
    var arena=new StageLayout {Id="fixture",Width=9,Height=9,Cells=cells,Spawns=[(3,3),(4,3)],SoftBlockDensity=0};
    var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=1},arena);
    var placed=false;for(var i=0;i<180;i++){match.Tick();placed|=State(match).GetProperty("bombs").GetArrayLength()>0;}
    Assert(!placed,"Bot placed a bomb in a two-cell chamber with no refuge.");
});
Run("practice self-bomb respawns a living player who can place another bomb", () =>
{
    var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true}); Fight(match);
    match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,90);
    Assert(Player(match).GetProperty("alive").GetBoolean()&&!Player(match).GetProperty("ghost").GetBoolean(),"Practice death left the player as an unusable ghost.");
    match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);
    Assert(State(match).GetProperty("bombs").GetArrayLength()>0,"Respawned practice player cannot place another bomb.");
    Assert(!match.IsEnded&&State(match).GetProperty("round").GetInt32()==1,"Practice respawn ended or reset the match.");
});
Run("practice water fall drops held bomb and respawns with working controls", () =>
{
    var arena=FlatArena([(1,3)]);for(var z=0;z<9;z++)arena.Cells[0,z]=CellDef.Water;
    var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},arena);Fight(match);
    match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);
    match.SetInput(0,new PlayerInput(0,0,false,true));Step(match,2);
    Assert(Player(match).GetProperty("carrying").GetString()=="bomb","Fixture did not lift the bomb before falling.");
    match.SetInput(0,new PlayerInput(-1,0,false,true));Step(match,20);match.SetInput(0,default);Step(match,2);
    var player=Player(match);
    Assert(player.GetProperty("alive").GetBoolean()&&!player.GetProperty("ghost").GetBoolean(),"Water left practice player dead/ghost.");
    Assert(player.GetProperty("x").GetSingle()>=1&&player.GetProperty("carrying").ValueKind==JsonValueKind.Null,"Practice spawn remained in water or kept a carried entity.");
    Walk(match,2.5f,3.5f);match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);
    Assert(State(match).GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("x").GetSingle()>2),"Practice actions failed after water respawn.");
});
Run("bot match win retains the final scoreboard in the end envelope", () =>
{
    var arena=FlatArena([(1,3),(7,3)]);for(var z=0;z<9;z++)arena.Cells[0,z]=CellDef.Water;
    var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=1,Wins=1},arena);Fight(match);
    string? ended=null;match.Message+=json=>{using var doc=JsonDocument.Parse(json);if(doc.RootElement.GetProperty("t").GetString()=="sys.ended")ended=json;};
    match.SetInput(0,new PlayerInput(-1,0));Step(match,20);
    Assert(match.IsEnded&&match.Result?.Reason=="bot_won","Water elimination did not produce the bot match win.");
    Assert(match.Result!.Winners.Count==0,"Bot victory invented a human winner.");
    Assert(ended is not null,"No match end envelope was emitted.");
    var data=Data(ended!);var details=data.GetProperty("details");
    Assert(details.ValueKind==JsonValueKind.Object,"Bot win discarded final scoreboard details.");
    var rows=details.GetProperty("scoreboard").EnumerateArray().ToArray();
    var bot=rows.Single(r=>r.GetProperty("bot").GetBoolean());
    Assert(bot.GetProperty("wins").GetInt32()==1&&bot.GetProperty("id").GetString()=="bot-0","Final bot point missing from scoreboard.");
    Assert(rows.Length==2&&rows.Single(r=>!r.GetProperty("bot").GetBoolean()).GetProperty("wins").GetInt32()==0,"Final human score corrupted.");
});
Run("flood thresholds use actual elevation and temple hurry does not close columns", () =>
{
    Assert(FloodRules.Level(59,120)==-1,"Water rose before one-minute warning.");
    Assert(FloodRules.Level(60,120)==-.75f,"Hurry water start changed.");
    Assert(!FloodRules.Submerged(0,FloodRules.Level(83.99f,120)),"Floor0 drowned too early.");
    Assert(FloodRules.Submerged(0,FloodRules.Level(84,120)),"Exact floor0 flood threshold did not drown.");
    Assert(!FloodRules.Submerged(1,FloodRules.Level(95.99f,120))&&FloodRules.Submerged(1,FloodRules.Level(96,120)),"Floor1 threshold incorrect.");
    var cells=new CellDef[9,9];for(var x=0;x<9;x++)for(var z=0;z<9;z++)cells[x,z]=CellDef.Floor0;
    cells[4,3]=CellDef.Floor1;cells[7,3]=new CellDef {Kind=CellKind.Floor,Tier=4};
    var arena=new StageLayout {Id="sunken-temple",Width=9,Height=9,Cells=cells,Spawns=[(1,3),(4,3),(7,3)],SoftBlockDensity=0};
    var match=new LocalMatch(new MatchOptions {HumanPlayers=3,BotPlayers=0,RoundSeconds=60,Wins=5},arena);
    while(State(match).GetProperty("waterLevel").GetSingle()<0)Step(match,2);
    Assert(!Player(match).GetProperty("alive").GetBoolean()&&!Player(match).GetProperty("ghost").GetBoolean(),"Submerged court player survived or became ghost.");
    Assert(Player(match,1).GetProperty("alive").GetBoolean()&&Player(match,2).GetProperty("alive").GetBoolean(),"Higher tiers drowned at court threshold.");
    Assert(State(match).GetProperty("closedLeft").GetInt32()==0&&State(match).GetProperty("closedRight").GetInt32()==0,"Temple used closing columns.");
    while(State(match).GetProperty("waterLevel").GetSingle()<1)Step(match,2);
    Assert(!Player(match,1).GetProperty("alive").GetBoolean()&&Player(match,2).GetProperty("alive").GetBoolean(),"Water did not independently eliminate next tier.");
    Assert(State(match).GetProperty("phase").GetInt32()==3&&Player(match,2).GetProperty("wins").GetInt32()==1,"Last high survivor did not win flooded round.");
    Assert(Events(match).Count(e=>Kind(e)=="waterfall")==1,"Flood rise did not emit exactly one waterfall event.");
    Assert(Events(match).Where(e=>Kind(e)=="death").All(e=>e.GetProperty("reason").GetString()=="water"),"Flood deaths lost their water cause.");
});
Run("power-up limits weighted choices and round inventory reset", () =>
{
    var p=new BlastPlayer();for(var i=0;i<20;i++){PowerUpRules.Apply(p,"fire");PowerUpRules.Apply(p,"bomb");PowerUpRules.Apply(p,"heart");}
    Assert(p.FireLevel==3&&p.BombCapacity==8&&p.HasHeart,"Power-up caps broken.");
    var seedA=new Random(7);var seedB=new Random(7);var types=new Dictionary<string,int>();
    for(var i=0;i<10000;i++){var a=PowerUpRules.ChooseDrop(seedA);Assert(a==PowerUpRules.ChooseDrop(seedB),"Drop draw not deterministic.");types[a]=types.GetValueOrDefault(a)+1;}
    Assert(types.Count==5&&types["fire"]>types["remote"]&&types["bomb"]>types["heart"],"Weighted distribution missing/common items not common.");
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
Run("holding lift then bomb creates held bomb and pumps cap at six effective levels", () =>
{
    var arena=FlatArena([(3,3)]);
    arena=new StageLayout {Id=arena.Id,Width=9,Height=9,Cells=arena.Cells,Spawns=arena.Spawns,SoftBlockDensity=0,InitialItems=[(3,3,"fire"),(3,3,"fire")]};
    var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},arena);Fight(match);
    match.SetInput(0,new PlayerInput(0,0,false,true));Step(match,2);
    match.SetInput(0,new PlayerInput(0,0,true,true));Step(match,2);
    Assert(Player(match).GetProperty("carrying").GetString()=="bomb","Holding lift before bomb did not create held bomb.");
    for(var i=0;i<8;i++){match.SetInput(0,new PlayerInput(0,0,false,true));Step(match,2);match.SetInput(0,new PlayerInput(0,0,true,true));Step(match,2);}
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
Run("separate remote and red bomb chain reactions observe cover and piercing", () =>
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
Run("actual destructible drops use seeded weighted item selection and can be collected", () =>
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
Run("heart pickup absorbs exactly one actual blast without stacking", () =>
{
    var arena=new StageLayout {Id="fixture",Width=9,Height=9,Cells=FlatArena([(1,3)]).Cells,Spawns=[(1,3),(2,3),(7,7)],SoftBlockDensity=0,
        InitialItems=[(2,3,"heart"),(2,3,"heart")]};
    var match=new LocalMatch(new MatchOptions {HumanPlayers=3,BotPlayers=0,Wins=5},arena);Fight(match);
    Assert(Player(match,1).GetProperty("heart").GetBoolean(),"Heart pickups not collected.");
    match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,90);
    var guarded=Player(match,1);Assert(guarded.GetProperty("alive").GetBoolean()&&!guarded.GetProperty("ghost").GetBoolean()&&!guarded.GetProperty("heart").GetBoolean(),"Heart did not absorb the first blast and disappear.");
    Step(match,70);match.SetInput(1,new PlayerInput(0,0,true));Step(match,2);match.SetInput(1,default);Step(match,90);
    Assert(Player(match,1).GetProperty("ghost").GetBoolean(),"A second heart stacked or a second blast failed to eliminate player.");
});
Run("legacy arenas retain closing columns while practice disables every hurry hazard", () =>
{
    var legacy=new LocalMatch(new MatchOptions {Stage="rock-garden",HumanPlayers=2,BotPlayers=0,RoundSeconds=60});Step(legacy,110);
    Assert(State(legacy).GetProperty("hurryMode").GetString()=="columns"&&State(legacy).GetProperty("closedLeft").GetInt32()>0,"Legacy sudden death lost closing columns.");
    var practice=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,RoundSeconds=60,Practice=true});Step(practice,4000);
    Assert(State(practice).GetProperty("hurryMode").GetString()=="none"&&State(practice).GetProperty("waterLevel").GetSingle()==-1,"Practice flooded during indefinite training.");
});
Run("local appearances follow compact active roster for humans and CPU slots", () =>
{
    var appearances=new[]{5,7,2};
    var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=2,PlayerAppearances=appearances});
    appearances[0]=0;Step(match,2);
    Assert(State(match).GetProperty("players").EnumerateArray().Select(p=>p.GetProperty("appearance").GetInt32()).SequenceEqual(new[]{5,7,2}),"Selected appearances not assigned or caller mutated authority configuration.");
    foreach(var bad in new[]{new[]{1},new[]{0,1,8},new[]{0,-1,2}})
    {var rejected=false;try{_=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=2,PlayerAppearances=bad});}catch(ArgumentException){rejected=true;}Assert(rejected,"Malformed local appearance roster accepted.");}
    match.SetAppearance(0,4);Step(match,2);
    Assert(Player(match).GetProperty("appearance").GetInt32()==4&&Player(match,1).GetProperty("appearance").GetInt32()==7,"Local cosmetic change affected another participant.");
});
Run("appearance messages reject malformed choices and cannot target another player", () =>
{
    var f=new AppearanceFixture();f.Message(0,"{\"index\":6}");f.Step(2);
    foreach(var malformed in new[]{"{}","null","[]","{\"index\":-1}","{\"index\":8}","{\"index\":1.5}","{\"index\":1e100}","{\"index\":\"2\"}","{\"index\":null}","{\"index\":true}","{\"index\":[2]}"})
    {
        f.Message(0,malformed);f.Step(2);
        Assert(f.Snapshot.GetProperty("players")[0].GetProperty("appearance").GetInt32()==6,"Malformed cosmetic message changed selection: "+malformed);
    }
    f.Message(0,"{\"index\":7,\"slot\":1,\"playerId\":\"fixture-1\"}");f.Step(2);
    var players=f.Snapshot.GetProperty("players");
    Assert(players[0].GetProperty("appearance").GetInt32()==7&&players[1].GetProperty("appearance").GetInt32()==1,"Client targeted another player's appearance.");
    using var data=JsonDocument.Parse("{\"index\":3}");
    f.Session.OnMessage(f,new Burbuja.Realtime.Abstractions.RealtimePlayer("unseated","Unknown",0,new Dictionary<string,string>()),new Burbuja.Realtime.Abstractions.InboundMessage("appearance",data.RootElement.Clone(),null,f.Now));f.Step(2);
    Assert(f.Snapshot.GetProperty("players")[0].GetProperty("appearance").GetInt32()==7,"Unseated sender changed appearance.");
});
Run("appearance survives connection restoration and round equipment reset", () =>
{
    var f=new AppearanceFixture();f.Message(0,"{\"index\":5}");
    f.Session.OnPlayerConnectionChanged(f,f.Players[0],false);f.Session.OnPlayerConnectionChanged(f,f.Players[0],true);f.Step(62);
    Assert(f.Snapshot.GetProperty("players")[0].GetProperty("appearance").GetInt32()==5,"Reconnect lost selected appearance.");
    f.Message(0,"{\"x\":-1,\"z\":0}","input");f.Step(20);f.Message(0,"{\"x\":0,\"z\":0}","input");f.Step(94);
    Assert(f.Snapshot.GetProperty("round").GetInt32()==2&&f.Snapshot.GetProperty("players")[0].GetProperty("appearance").GetInt32()==5,"Round reset lost selected appearance.");
});
Run("different appearances produce identical gameplay simulation", () =>
{
    var a=new LocalMatch(new MatchOptions {HumanPlayers=2,BotPlayers=0,PlayerAppearances=[1,3]});
    var b=new LocalMatch(new MatchOptions {HumanPlayers=2,BotPlayers=0,PlayerAppearances=[7,6]});
    for(var tick=0;tick<120;tick++){var input=new PlayerInput(tick%40<20?.1f:-.1f,0,tick%60==0);a.SetInput(0,input);b.SetInput(0,input);a.Tick();b.Tick();}
    var left=System.Text.Json.Nodes.JsonNode.Parse(a.StateJson!)!;var right=System.Text.Json.Nodes.JsonNode.Parse(b.StateJson!)!;
    foreach(var root in new[]{left,right})foreach(var player in root["d"]!["players"]!.AsArray())player!.AsObject().Remove("appearance");
    Assert(System.Text.Json.Nodes.JsonNode.DeepEquals(left,right),"Cosmetic selection changed game outcomes or physics.");
});
Run("late lobby joins and reconnects receive existing authoritative appearances", () =>
{
    var f=new AppearanceFixture(startMatch:false);f.Message(0,"{\"index\":6}");f.Sent.Clear();
    var late=new Burbuja.Realtime.Abstractions.RealtimePlayer("late-joiner","Late",2,new Dictionary<string,string>());
    f.Session.OnPlayerJoined(f,late);
    Assert(f.Sent.Any(m=>m.Recipient==late.PlayerId&&m.Type=="appearance"&&m.Data.GetProperty("playerId").GetString()=="fixture-0"&&m.Data.GetProperty("index").GetInt32()==6),"Late joiner cannot see host selection before match snapshots.");
    f.Sent.Clear();f.Session.OnPlayerConnectionChanged(f,f.Players[1],true);
    Assert(f.Sent.Any(m=>m.Recipient=="fixture-1"&&m.Type=="appearance"&&m.Data.GetProperty("playerId").GetString()=="fixture-0"&&m.Data.GetProperty("index").GetInt32()==6),"Reconnecting lobby player did not recover appearance roster.");
});
Run("presentation events record only accepted held actions and one authoritative blast", () =>
{
    var match = new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},FlatArena([(3,3)])); Fight(match);
    match.SetInput(0,new PlayerInput(0,0,false,true));Step(match,2);
    match.SetInput(0,new PlayerInput(0,0,true,true));Step(match,2);
    for(var i=0;i<8;i++){match.SetInput(0,new PlayerInput(0,0,false,true));Step(match,2);match.SetInput(0,new PlayerInput(0,0,true,true));Step(match,2);}
    var events=Events(match);
    Assert(events.Count(e=>Kind(e)=="bomb-carry")==1&&!events.Any(e=>Kind(e)=="bomb-drop"),"Held creation reported a ground placement.");
    Assert(events.Count(e=>Kind(e)=="pump")==3,"Rejected capped pumps emitted events.");
    match.SetInput(0,default);Step(match,2);
    var thrown=Events(match).Single(e=>Kind(e)=="throw");
    Assert(thrown.GetProperty("playerId").GetString()=="local-0"&&thrown.GetProperty("radius").GetSingle()>3,"Throw lost actor or effective radius.");
    Step(match,100);events=Events(match);
    Assert(events.Count(e=>Kind(e)=="throw")==1&&events.Count(e=>Kind(e)=="blast")==1,"One action emitted duplicate throw/blast events.");
    Assert(events.Single(e=>Kind(e)=="blast").GetProperty("entityId").GetString()==thrown.GetProperty("entityId").GetString(),"Blast lost bomb identity.");
});
Run("walking back into a bomb kicks across the blocked final movement step", () =>
{
    var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},FlatArena([(3,3)]));Fight(match);
    match.SetInput(0,new PlayerInput(-.2f,0));Step(match,2);
    match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,2);
    var bombX=State(match).GetProperty("bombs")[0].GetProperty("x").GetSingle();
    match.SetInput(0,new PlayerInput(.95f,0));Step(match,14);
    Assert(Player(match).GetProperty("x").GetSingle()>bombX+1.8f,"Repro did not move away from front bomb.");
    Assert(!Events(match).Any(e=>Kind(e)=="kick"),"Walking away kicked the bomb behind the player.");
    match.SetInput(0,new PlayerInput(-1,0));Step(match,2);
    Assert(!Events(match).Any(e=>Kind(e)=="kick"),"Bomb was kicked before this tick could reach contact.");
    for(int i=0;i<16&&!Events(match).Any(e=>Kind(e)=="kick");i++)match.Tick();
    Assert(Events(match).Any(e=>Kind(e)=="kick"),"Movement collision stopped the player outside the fixed kick distance.");
    Assert(State(match).GetProperty("bombs")[0].GetProperty("x").GetSingle()<bombX-.05f,"Accepted kick did not roll the actual bomb.");
});
Run("presentation ground placement lift and repeated blocked contact are distinct", () =>
{
    var arena=FlatArena([(3,3)]);
    var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},arena);Fight(match);
    match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,2);
    match.SetInput(0,new PlayerInput(0,0,false,true));Step(match,2);
    Assert(Events(match).Count(e=>Kind(e)=="bomb-drop")==1&&Events(match).Count(e=>Kind(e)=="lift")==1,"Accepted ground placement/lift missing.");
    arena=FlatArena([(3,3)]);arena.Cells[3,6]=CellDef.Pillar;
    match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},arena);Fight(match);
    match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,2);
    match.SetInput(0,new PlayerInput(0,.4f,A:true));
    for(int tick=0;tick<50&&Events(match).Count(e=>Kind(e)=="kick")<2;tick++)match.Tick();
    var kicks=Events(match).Count(e=>Kind(e)=="kick");
    Assert(kicks==2,"Expected initial roll and a fresh contact after the fast bomb rolled away. "+State(match));
    Step(match,12);
    Assert(Events(match).Count(e=>Kind(e)=="kick")==kicks,"Continuous blocked contact emitted duplicate kicks.");
});
Run("presentation deaths retain explosion water and fall reasons through practice recovery", () =>
{
    var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},FlatArena([(3,3)]));Fight(match);
    match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,88);
    var death=Events(match).Single(e=>Kind(e)=="death");
    Assert(death.GetProperty("reason").GetString()=="explosion"&&death.GetProperty("causedBy").GetString()=="local-0","Explosion death reason/owner lost.");
    Assert(Events(match).Single(e=>Kind(e)=="respawn").GetProperty("reason").GetString()=="explosion","Practice recovery lost original cause.");
    foreach(var kind in new[]{CellKind.Water,CellKind.Void})
    {
        var stage=FlatArena([(1,3)]);for(var z=0;z<9;z++)stage.Cells[0,z]=kind==CellKind.Water?CellDef.Water:CellDef.Void;
        match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},stage);Fight(match);
        match.SetInput(0,new PlayerInput(-1,0));Step(match,6);
        // Crossing the edge begins a real fall; wait for contact below the floor.
        match.SetInput(0,default);Step(match,20);
        death=Events(match).First(e=>Kind(e)=="death");
        Assert(death.GetProperty("reason").GetString()==(kind==CellKind.Water?"water":"fall"),"Terrain death has incorrect cause.");
    }
});
Run("presentation history survives round reset reconnect and remains bounded with consecutive ids", () =>
{
    var f=new AppearanceFixture();f.Step(62);var before=f.Snapshot.GetProperty("presentationSeq").GetInt64();
    f.Session.OnPlayerConnectionChanged(f,f.Players[0],false);f.Session.OnPlayerConnectionChanged(f,f.Players[0],true);f.Step(2);
    Assert(f.Snapshot.GetProperty("presentationSeq").GetInt64()==before,"Reconnect reset or invented events.");
    f.Step(3800);
    var events=f.Snapshot.GetProperty("events").EnumerateArray().ToArray();
    Assert(events.Any(e=>Kind(e)=="round-ready"&&e.GetProperty("round").GetInt32()==2)&&events.Any(e=>e.GetProperty("round").GetInt32()==1),"Round reset cleared presentation history.");
    var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},FlatArena([(3,3)]));Fight(match);
    for(var i=0;i<50;i++){match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,88);match.SetInput(0,default);Step(match,2);}
    events=Events(match);
    Assert(events.Length==128&&events[0].GetProperty("seq").GetInt64()>1,"Presentation history grew without a cap.");
    for(var i=1;i<events.Length;i++)Assert(events[i].GetProperty("seq").GetInt64()==events[i-1].GetProperty("seq").GetInt64()+1,"Event sequence has gaps/duplicates.");
    Assert(events[^1].GetProperty("seq").GetInt64()==State(match).GetProperty("presentationSeq").GetInt64(),"Snapshot cursor disagrees with retained tail.");
});
Console.WriteLine($"Passed {passed} game integration checks.");

void Run(string name, Action check) { check(); passed++; Console.WriteLine($"PASS {name}"); }
static void Assert(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
static JsonElement Data(string json) { using var doc = JsonDocument.Parse(json); return doc.RootElement.GetProperty("d").Clone(); }
static JsonElement State(LocalMatch match) => Data(match.StateJson!);
static LocalMatch TwoHumans() => new(new MatchOptions { HumanPlayers = 2, BotPlayers = 0 });
static void Step(LocalMatch match, int ticks) { for (var i = 0; i < ticks; i++) match.Tick(); }
static void Fight(LocalMatch match) { Step(match, 62); Assert(State(match).GetProperty("phase").GetInt32() == 1, "Fight did not begin."); }

static StageLayout StepArena((int X, int Z) spawn)
{
    var cells = new CellDef[9, 9];
    for (var x = 0; x < 9; x++) for (var z = 0; z < 9; z++) cells[x, z] = x >= 4 ? CellDef.Floor1 : CellDef.Floor0;
    cells[3, 3] = CellDef.Ramp(0, 1, 1, 0);
    return new StageLayout { Id = "fixture", Width = 9, Height = 9, Cells = cells, Spawns = [spawn] };
}

static JsonElement Player(LocalMatch match, int index=0)=>State(match).GetProperty("players")[index];
static void Walk(LocalMatch match,float x,float z)
{
    for(var tick=0;tick<600;tick++)
    {
        var p=Player(match);var dx=x-p.GetProperty("x").GetSingle();var dz=z-p.GetProperty("z").GetSingle();
        if(Math.Abs(dx)<.12f&&Math.Abs(dz)<.12f){match.SetInput(0,default);Step(match,2);return;}
        match.SetInput(0,new PlayerInput(Math.Clamp(dx/.3f,-1,1),Math.Clamp(dz/.3f,-1,1)));match.Tick();
    }
    var last=Player(match);throw new InvalidOperationException($"Could not walk to {x},{z}; stuck at {last.GetProperty("x")},{last.GetProperty("y")},{last.GetProperty("z")}");
}
static StageLayout FlatArena((int X,int Z)[] spawns)
{
    var cells=new CellDef[9,9];for(var x=0;x<9;x++)for(var z=0;z<9;z++)cells[x,z]=CellDef.Floor0;
    return new StageLayout {Id="fixture",Width=9,Height=9,Cells=cells,Spawns=spawns,SoftBlockDensity=0};
}

static JsonElement[] Events(LocalMatch match)=>State(match).GetProperty("events").EnumerateArray().ToArray();
static string? Kind(JsonElement e)=>e.GetProperty("kind").GetString();
