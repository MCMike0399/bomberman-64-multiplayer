using System.Text.Json;
using BlastCircuit.Testing;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Rules;

internal static class FeedbackSteeringChecks
{
    internal static int Run()
    {
        bool Ring(bool bounded)
        {
            var match=new LocalMatch(new MatchOptions{Stage="greedy-trap",HumanPlayers=2,BotPlayers=0,
                RoundSeconds=120,Seed=64,Advanced=new AdvancedRules{BlockPercent=0}});
            JsonElement Player()=>match.StateMessage!.Value.GetProperty("d").GetProperty("players")[0];
            for(int i=0;i<64;i++)match.Tick();
            foreach(var (x,z)in new[]{(9.5f,2.5f),(9.5f,5.5f)})
            {
                bool delayed=z==5.5f;
                var observed=Player().Clone();
                for(int frame=0;frame<240;frame++)
                {
                    if(!delayed)observed=Player().Clone();
                    float dx=x-observed.GetProperty("x").GetSingle(),dz=z-observed.GetProperty("z").GetSingle();
                    if(MathF.Sqrt(dx*dx+dz*dz)<.35f)break;
                    // Establish the same upper landing with fresh feedback first.
                    // Then delay the stair leg by one observation and use the
                    // peer's existing five-tick catchup; authority is unchanged.
                    observed=Player().Clone();match.SetInput(0,FeedbackSteering.Toward(dx,dz,bounded));
                    for(int tick=0;tick<(delayed?5:1);tick++)match.Tick();
                }
                match.SetInput(0,default);for(int tick=0;tick<6;tick++)match.Tick();
                var settled=Player();
                Console.WriteLine($"QA_FEEDBACK_WITNESS bounded={bounded} target={x},{z} actor={settled.GetProperty("x")},{settled.GetProperty("y")},{settled.GetProperty("z")} tick={match.TickNumber}");
                if(z==2.5f&&(settled.GetProperty("y").GetSingle()!=2||Math.Abs(settled.GetProperty("x").GetSingle()-x)>=.5f))
                    throw new Exception("Feedback control did not establish the retained upper landing precondition");
            }
            var p=Player();return p.GetProperty("alive").GetBoolean()&&p.GetProperty("y").GetSingle()==1&&Math.Abs(p.GetProperty("z").GetSingle()-5.5f)<.5f;
        }
        if(Ring(false))throw new Exception("Old high-gain feedback must reproduce the missed middle landing under delayed snapshots");
        if(!Ring(true))throw new Exception("Delayed peer feedback must settle on the real Greedy middle landing without falling into the basin");
        Console.WriteLine("PASS delayed peer feedback preserves the narrow Greedy landing; old controller is red");return 2;
    }
}
