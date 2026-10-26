// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using Bomber.Game.Simulation.State;

namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    private bool BotAttackDue(BlastPlayer bot) =>
        _roundTimer-_aiLastThreat.GetValueOrDefault(bot.Id)>_aiProfile.EngagementSeconds;

    private bool BotCoverAt(int cell) => _softBlocks.IsAlive(_aiGraph.X(cell),_aiGraph.Z(cell))&&
        Math.Abs(_stage.SurfaceAt(_aiGraph.X(cell)+.5f,_aiGraph.Z(cell)+.5f)-_aiGraph.Height[cell])<1;

    private void BotBuildPursuit(BlastPlayer bot,int start)
    {
        _aiPursuitStart=-1;_aiNeedsBreach=false;
        if(!BotAttackDue(bot))return;
        BotPursuitDistances(bot,false);
        _aiNeedsBreach=_aiPursuit[start]<0;
        // A sealed route needs demolition before pursuit. Ignoring crates here
        // identifies a frontier; only the timed tactical search may move the bot.
        if(_aiNeedsBreach)BotPursuitDistances(bot,true);
        _aiPursuitStart=_aiPursuit[start];
    }

    private void BotPursuitDistances(BlastPlayer bot,bool throughCover)
    {
        Array.Fill(_aiPursuit,-1);
        int head=0,tail=0;
        foreach(var enemy in _players.Values)
        {
            if(enemy.Id==bot.Id||!enemy.Alive||enemy.Ghost)continue;
            int cell=_aiGraph.Cell(enemy.X,enemy.Z,enemy.Y);
            if(cell<0||!_aiGraph.Walkable[cell]||_aiPursuit[cell]>=0)continue;
            _aiPursuit[cell]=0;_aiQueue[tail++]=cell;
        }
        while(head<tail)
        {
            int cell=_aiQueue[head++];
            for(int edge=_aiGraph.IncomingStarts[cell];edge<_aiGraph.IncomingStarts[cell+1];edge++)
            {
                int previous=_aiGraph.IncomingEdges[edge];
                if(_aiPursuit[previous]>=0||(!throughCover&&BotCoverAt(previous)))continue;
                _aiPursuit[previous]=_aiPursuit[cell]+1;_aiQueue[tail++]=previous;
            }
        }
    }
}
