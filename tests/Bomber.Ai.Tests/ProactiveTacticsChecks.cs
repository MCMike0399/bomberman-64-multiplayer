using Bomber.Game.Simulation.Ai;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;

// Declared authority fixtures isolate tactics; movement, placements and blasts use real ticks.
internal static class ProactiveTacticsChecks
{
    public static int Run()
    {
        int checks=0;var failures=new List<string>();
        foreach(string id in new[]{"rock-garden","sunken-temple","up-and-down","top-rules"})
        {
            var graph=NavigationGraph.For(BlastStages.Get(id));
            int expected=0;
            for(int node=0;node<graph.Count;node++)
            {
                for(int d=0;d<4;d++)
                {
                    int next=graph.Edges[node*4+d];if(next<0)continue;
                    expected++;
                    var incoming=graph.IncomingEdges.AsSpan(graph.IncomingStarts[next],graph.IncomingStarts[next+1]-graph.IncomingStarts[next]);
                    if(!incoming.Contains(node))throw new Exception("Reverse pursuit lost a directed walk edge on "+id);
                }
                for(int edge=graph.IncomingStarts[node];edge<graph.IncomingStarts[node+1];edge++)
                    if(!graph.Edges.AsSpan(graph.IncomingEdges[edge]*4,4).Contains(node))
                        throw new Exception("Reverse pursuit invented an ascent or changed a navigation layer on "+id);
            }
            if(expected!=graph.IncomingEdges.Length)throw new Exception("Reverse pursuit duplicated walk edges on "+id);
            Console.WriteLine("PASS reverse pursuit preserves every directed edge and navigation layer on "+id);checks++;
        }
        foreach(string difficulty in new[]{"easy","medium","hard"})
        {
            var stage=Fixture.Flat(23,17,[(8,3),(12,3)]);
            for(int z=1;z<13;z++)stage.Cells[10,z]=CellDef.Pillar;
            var f=new Fixture(stage,2,difficulty:difficulty);
            f.Session.AiTestAddHuman("cpu-1",1);f.Session.AiTestPosition("cpu-1",12.5f,3.5f);f.Step(61);
            var bot=f.Session.AiTestPlayers.First(p=>p.Id=="cpu-0");
            var target=f.Session.AiTestPlayers.First(p=>p.Id=="cpu-1");
            bool crossed=false,pressure=false;
            var soft=new SoftBlockGrid(stage.Width,stage.Height);
            for(int tick=0;tick<750;tick++)
            {
                target.InvincibleTimer=2;f.Step();crossed|=bot.X>11;
                pressure|=f.Session.AiTestBombs.Any(b=>b.OwnerId==bot.Id&&BlastGeometry.Contains(stage,soft,b.X,b.Tier,b.Z,b.BlastRadius,target.X,target.Y,target.Z,b.IsRed));
                if(pressure)break;
            }
            Console.WriteLine($"detour {difficulty}: crossed={crossed} pressure={pressure} alive={bot.Alive} end=({bot.X:F2},{bot.Z:F2})");
            if(!crossed||!pressure||!bot.Alive)failures.Add(difficulty+" CPU must take a long wall detour and threaten its visible rival within 25 seconds");
            checks++;
        }
        foreach(string difficulty in new[]{"easy","medium","hard"})
        {
            var stage=Fixture.Flat(19,13,[(2,5),(15,5)]);
            var f=new Fixture(stage,2,difficulty:difficulty);
            f.Session.AiTestAddHuman("cpu-1",1);f.Session.AiTestPosition("cpu-1",15.5f,5.5f);f.Step(61);
            for(int z=1;z<stage.Height-1;z++)f.Session.AiTestSetSoft(9,z,true);
            var bot=f.Session.AiTestPlayers.First(p=>p.Id=="cpu-0");
            var target=f.Session.AiTestPlayers.First(p=>p.Id=="cpu-1");
            bool crossed=false;
            for(int tick=0;tick<900;tick++)
            {
                target.InvincibleTimer=2;f.Step();crossed|=bot.X>10;
                if(crossed)break;
            }
            Console.WriteLine($"breach {difficulty}: crossed={crossed} alive={bot.Alive} end=({bot.X:F2},{bot.Z:F2})");
            if(!crossed||!bot.Alive)failures.Add(difficulty+" CPU must clear blocking cover and advance instead of circling it after its engagement deadline");
            checks++;
        }
        var chain=new Fixture(Fixture.Flat(spawns:[(3,5),(7,5)]),2);
        chain.Session.AiTestPosition("cpu-0",3.5f,5.5f);chain.Session.AiTestPosition("cpu-1",7.5f,5.5f);
        chain.Session.AiTestAddBomb(new BlastBomb{Id="remote",OwnerId="cpu-0",IsRemote=true,X=6.5f,Z=5.5f,CellX=6,CellZ=5});
        chain.Session.AiTestAddBomb(new BlastBomb{Id="chain",OwnerId="cpu-1",X=4.8f,Z=5.5f,CellX=4,CellZ=5,FuseTimer=2.5f});
        if(chain.Session.AiTestRemoteOpportunity("cpu-0"))failures.Add("CPU must refuse a remote attack whose visible chain reaches its owner");
        chain.Session.AiTestPosition("cpu-0",2.5f,5.5f);
        if(!chain.Session.AiTestRemoteOpportunity("cpu-0"))failures.Add("CPU must retain a useful remote chain attack after its owner reaches safety");
        if(failures.Count>0)throw new Exception(string.Join("; ",failures));
        Console.WriteLine("PASS remote trigger rejects a lethal visible chain outside its direct blast");checks++;
        checks+=RemoteCoverChain();
        Console.WriteLine($"PASS {checks} proactive tactics checks");return checks;
    }

    private static int RemoteCoverChain()
    {
        foreach(bool hardCover in new[]{false,true})
        {
            var stage=Fixture.Flat();if(hardCover)stage.Cells[6,6]=CellDef.Pillar;
            var f=new Fixture(stage,2);
            f.Session.AiTestAddHuman("cpu-0",0);f.Session.AiTestAddHuman("cpu-1",1);f.Step(61);
            f.Session.AiTestPosition("cpu-0",6.5f,7.5f);f.Session.AiTestPosition("cpu-1",4,5.5f);
            var owner=f.Session.AiTestPlayers.Single(p=>p.Id=="cpu-0");
            owner.HasRemote=true;owner.InvincibleTimer=0;
            if(!hardCover)f.Session.AiTestSetSoft(6,6,true);
            var remote=new BlastBomb{Id="cover-remote",OwnerId=owner.Id,IsRemote=true,X=5,Z=5.5f,CellX=5,CellZ=5,FireLevel=2};
            var chained=new BlastBomb{Id="cover-chain",OwnerId="cpu-1",X=6.5f,Z=5.5f,CellX=6,CellZ=5,FireLevel=2,FuseTimer=2.5f};
            f.Session.AiTestAddBomb(remote);f.Session.AiTestAddBomb(chained);
            bool approved=f.Session.AiTestRemoteOpportunity(owner.Id);
            // Declared authority fixture: execute the candidate remote command even
            // when rejected, to prove that the cover really disappears before the chain.
            owner.Input=new InputState(0,0,false,false,true);f.Step();
            Console.WriteLine($"remote cover chain: hard={hardCover}, approved={approved}, remote={remote.Exploded}, chain={chained.Exploded}, ownerAlive={owner.Alive}");
            if(!remote.Exploded||!chained.Exploded||owner.Alive!=hardCover)
                throw new Exception("Remote cover fixture must kill through destroyed cover and retain protection behind permanent terrain");
            if(approved!=hardCover)throw new Exception("CPU must reject a remote chain through destroyed soft cover while retaining safe attacks behind permanent terrain");
        }
        Console.WriteLine("PASS remote forecast handles destroyed cover and permanent terrain in actual chains");return 2;
    }
}
