using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.State;

// Regressions for the 2026-10-26 agency work. Each assertion failed on the prior
// policy; AgencyDiagnostics owns the measurements and their trace mode.
static class AgencyChecks
{
    public static int Run()
    {
        int checks=0;
        void Check(bool passed,string name){if(!passed)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}

        // Up & Down Easy seed 64 shuffled 84 consecutive single-cell reversals beside
        // the idle player and bombed once in a minute: its Bouncing bomb's 3.5-second
        // fuse outlasted Easy's 3.1-second escape horizon, so standing still looked safe.
        var shuffle=AgencyDiagnostics.IdleHuman("up-and-down","easy",64);
        Check(shuffle.LongestPingPongStreak<=12&&shuffle.HumanPressureBombs>=4,
            $"Easy CPU holding a Bouncing bomb attacks an idle player instead of shuffling (run={shuffle.LongestPingPongStreak}, hits={shuffle.HumanPressureBombs})");

        var bounce=new Fixture(Fixture.Flat(spawns:[(3,6),(6,6)]),2,64,"easy");
        bounce.Session.AiTestAddHuman("cpu-1",1);bounce.Session.AiTestPosition("cpu-1",6.5f,6.5f);bounce.Step(61);
        var bouncer=bounce.Session.AiTestPlayers.Single(p=>p.Id=="cpu-0");var target=bounce.Session.AiTestPlayers.Single(p=>p.Id=="cpu-1");
        bouncer.BombType="bounce";int bounced=0;var seenBombs=new HashSet<string>();
        for(int tick=0;tick<450;tick++)
        {
            target.InvincibleTimer=2;target.Input=default;bounce.Step();
            foreach(var bomb in bounce.Session.AiTestBombs)if(bomb.OwnerId=="cpu-0"&&seenBombs.Add(bomb.Id))bounced++;
        }
        Check(bounced>=2&&bouncer.Alive,$"Easy escape planning covers a longer Bouncing fuse ({bounced} bombs in 15 seconds)");

        // Easy/Medium steering slowed while aligning to a turn and settled just outside
        // the alignment tolerance against a gust: 10-24% of gust time moving, versus
        // 60-90% in calm. Leaning into the visible wind restores ordinary turns.
        foreach(string level in new[]{"easy","medium"})foreach(int seed in new[]{64,9876})
        {
            var gust=AgencyDiagnostics.IdleHuman("blizzard-battle",level,seed);
            float share=gust.GustTicks>0?gust.GustMovingTicks/(float)gust.GustTicks:0;
            Check(gust.GustTicks>150&&share>=.7f&&gust.GustBombs>0,$"{level} CPU keeps moving and bombing through Blizzard gusts (seed {seed}: {share:P0} moving, {gust.GustBombs} gust bombs)");
        }

        // Four Hard CPUs on Top Rules and Greedy Trap placed no bomb at all in 20 seconds:
        // pairs met in one-wide lanes two cells apart, neither could reach the other with
        // fire 1, and routes flickered with each other's bodies so both mirrored forever.
        foreach(string stage in new[]{"top-rules","greedy-trap"})foreach(int seed in new[]{64,9876})
        {
            int placed=AgencyDiagnostics.AllBotBombs(stage,"hard",seed,4,600);
            Check(placed>=8,$"four Hard CPUs break lane standoffs on {stage} seed {seed} ({placed} bombs in 20 seconds; prior policy 0)");
        }

        // Preserve the original algorithm-comparison loadout; current defaults are
        // exercised independently by PressureChecks, StockHumanPressureChecks and the soak.
        var matrix=AgencyDiagnostics.Matrix(startingBombs:3);
        int worstRun=matrix.Max(r=>r.LongestPingPongStreak);
        int worstIdleGap=matrix.Where(r=>r.Scenario=="idle-human").Max(r=>r.LongestPressureGapTicks);
        int idleDeaths=matrix.Where(r=>r.Scenario=="idle-human").Sum(r=>r.BotDeaths);
        Check(worstRun<=20,$"no sustained single-cell shuffle across 72 arena runs with the three-bomb reference loadout (longest {worstRun} reversals; prior policy 99)");
        Check(worstIdleGap<=20*30,$"an idle player is threatened at least every 20 seconds on every stock arena (longest {worstIdleGap/30f:F1}s; prior policy 60s)");
        Check(idleDeaths<=2,$"bots rarely defeat themselves while attacking an idle player ({idleDeaths} in 36 minutes; prior policy 3)");

        // A walking target was trailed from one cell behind, just outside fire-1 reach;
        // leading its visible movement intercepts it. Prior Medium: p90 12.8s, max 26.1s.
        foreach(string level in new[]{"medium","hard"})
        {
            var sweep=AgencyDiagnostics.PressureSweep(level,true,startingBombs:3);
            Check(sweep.Deaths==0&&sweep.MedianGapSeconds<=6&&sweep.P90GapSeconds<=10&&sweep.MaxGapSeconds<=15&&sweep.MeanHits>=8,
                $"{level} intercepts a patrolling target across 24 starts/phases with the three-bomb reference loadout (median {sweep.MedianGapSeconds:F1}s, p90 {sweep.P90GapSeconds:F1}s, max {sweep.MaxGapSeconds:F1}s, {sweep.MeanHits:F1} hits)");
        }

        // A rival walking at a resting bomb kicks it along their heading at 12 cells/s.
        // Standing in that lane gets the bot knocked down; it must leave sideways first.
        foreach(string level in new[]{"medium","hard"})
        {
            var arena=Fixture.Flat(spawns:[(6,4),(6,9)]);var f=new Fixture(arena,2,64,level);
            f.Session.AiTestAddHuman("cpu-1",1);f.Step(61);
            f.Session.AiTestPosition("cpu-0",6.5f,4.5f);f.Session.AiTestPosition("cpu-1",6.5f,9.5f);
            f.Session.AiTestAddBomb(new BlastBomb{Id="kick-lane",OwnerId="cpu-0",X=6.5f,Z=6.5f,CellX=6,CellZ=6,FuseTimer=2.4f});
            var bot=f.Session.AiTestPlayers.Single(p=>p.Id=="cpu-0");var kicker=f.Session.AiTestPlayers.Single(p=>p.Id=="cpu-1");
            bool knocked=false,kicked=false;
            for(int tick=0;tick<45;tick++)
            {
                kicker.InvincibleTimer=2;kicker.Input=new(0,-1,false,false,false);f.Step();
                knocked|=bot.StunTimer>0;kicked|=f.Session.AiTestBombs.Any(b=>b.Id=="kick-lane"&&b.State==BombState.Rolling);
            }
            Check(kicked&&!knocked&&bot.Alive,$"{level} CPU sidesteps the lane of a bomb a rival is walking into (kicked={kicked}, knocked={knocked}, x={bot.X:F2})");
        }
        return checks;
    }
}
