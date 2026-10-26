using Bomber.Game.Matches;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;
using static GameFixtures;

/// <summary>Bot routing, fuse safety and refusal of suicidal placements.</summary>
internal static class BotChecks
{
    public static void Run(Action<string, Action> run)
    {
        run("bots follow actual stair route to an elevated opponent", () =>
        {
            var stage = StepArena((6,3));
            stage = new StageLayout { Id=stage.Id, Width=stage.Width, Height=stage.Height, Cells=stage.Cells, Spawns=[(6,3),(1,3)] };
            var match = new LocalMatch(new MatchOptions { HumanPlayers = 1, BotPlayers = 1, Practice = true },stage); Fight(match);
            Step(match,300);
            var bot = Player(match,1);
            Assert(bot.GetProperty("y").GetSingle()==1 && bot.GetProperty("x").GetSingle()>4.5f, "Bot could not climb reachable stairs.");
            Assert(bot.GetProperty("alive").GetBoolean() && !bot.GetProperty("ghost").GetBoolean(), "Bot fell off its path.");
        });
        run("bot reaches safe terrain before an adjacent bomb fuse expires", () =>
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
        run("bot places its own bomb only with a route to safety", () =>
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
        run("trapped bot refuses a suicidal bomb placement", () =>
        {
            var cells=new CellDef[9,9];for(var x=0;x<9;x++)for(var z=0;z<9;z++)cells[x,z]=CellDef.Pillar;
            cells[3,3]=cells[4,3]=CellDef.Floor0;
            var arena=new StageLayout {Id="fixture",Width=9,Height=9,Cells=cells,Spawns=[(3,3),(4,3)],SoftBlockDensity=0};
            var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=1},arena);
            var placed=false;for(var i=0;i<180;i++){match.Tick();placed|=State(match).GetProperty("bombs").GetArrayLength()>0;}
            Assert(!placed,"Bot placed a bomb in a two-cell chamber with no refuge.");
        });
    }
}
