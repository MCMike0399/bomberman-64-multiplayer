using Burbuja.Realtime.Abstractions;
using Bomber.Game.Simulation.Ai;

namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    private NavigationGraph _aiGraph=null!;
    private TimedDangerField _aiDanger=null!,_aiPlacementDanger=null!;
    private int[] _aiQueue=null!,_aiSeen=null!,_aiFirst=null!,_aiDepth=null!;
    private byte[] _aiItemKind=null!;
    private bool[] _aiCover=null!;
    private float[] _aiNearest=null!,_aiSecondNearest=null!;
    private string?[] _aiNearestOwner=null!;
    private int _aiGeneration,_aiFieldTick=-1;
    private float _aiReaction=.12f,_aiSteeringInterval=4f/30;
    private readonly List<BlastBomb> _aiCandidateBombs=new(65);
    private readonly BlastBomb _aiCandidate=new() {Id="planning"};
    private readonly Dictionary<string,float> _aiLastBomb=new(StringComparer.Ordinal);
    private void BotInitialize(GameSessionOptions options)
    {
        _aiReaction=options.Settings.TryGetValue("bot-difficulty",out var difficulty)?difficulty switch{"easy"=>.24f,"hard"=>.08f,_=>.12f}:.12f;
        _aiSteeringInterval=MathF.Ceiling(_aiReaction*30)/30;
        _aiGraph=NavigationGraph.For(_stage);_aiDanger=new(_aiGraph,_stage);_aiPlacementDanger=new(_aiGraph,_stage);
        int count=_aiGraph.Count;_aiQueue=new int[count];_aiSeen=new int[count];_aiFirst=new int[count];_aiDepth=new int[count];_aiItemKind=new byte[count];_aiCover=new bool[count];_aiNearest=new float[count];_aiSecondNearest=new float[count];_aiNearestOwner=new string?[count];
    }
    private void BotReset(){_aiFieldTick=-1;_aiLastBomb.Clear();foreach(var p in _players.Values)p.BotThinkTimer=0;}
    private void BotRefreshField()
    {
        if(_aiFieldTick==_tickCount)return;
        _aiFieldTick=_tickCount;_aiDanger.Build(_softBlocks,_bombs,_hazards);Array.Clear(_aiItemKind);
        foreach(var item in _items)
        {
            int cell=_aiGraph.Cell(item.X,item.Z);if(cell>=0)_aiItemKind[cell]=item.Type switch{"heart"=>1,"remote"=>2,"red"=>3,"fire"=>4,_=>5};
        }
        // Shared observation grids avoid scanning every opponent/cover cell separately for every bot search.
        for(int cell=0;cell<_aiGraph.Count;cell++)
        {
            _aiCover[cell]=false;_aiNearest[cell]=_aiSecondNearest[cell]=float.PositiveInfinity;_aiNearestOwner[cell]=null;
            if(!_aiGraph.Walkable[cell])continue;
            for(int d=0;d<4;d++){int n=_aiGraph.Edges[cell*4+d];if(n>=0&&_softBlocks.IsAlive(n%_stage.Width,n/_stage.Width)){_aiCover[cell]=true;break;}}
            float x=cell%_stage.Width+.5f,z=cell/_stage.Width+.5f,y=_aiGraph.Height[cell];
            foreach(var enemy in _players.Values)if(enemy.Alive&&!enemy.Ghost)
            {
                float distance=Math.Abs(enemy.X-x)+Math.Abs(enemy.Z-z)+Math.Abs(enemy.Y-y)*2;
                if(distance<_aiNearest[cell]){_aiSecondNearest[cell]=_aiNearest[cell];_aiNearest[cell]=distance;_aiNearestOwner[cell]=enemy.Id;}
                else if(distance<_aiSecondNearest[cell])_aiSecondNearest[cell]=distance;
            }
        }
    }
    private void BotThink(BlastPlayer bot,float dt)
    {
        bot.BotThinkTimer-=dt;if(bot.BotThinkTimer>0)return;
        AiTestThinkCount++;bot.BotThinkTimer=_aiReaction;
        BotRefreshField();
        int start=_aiGraph.Cell(bot.X,bot.Z);if(start<0){bot.Input=default;return;}
        if(bot.StunTimer>0||bot.CarriedById is not null)
        {bool effort=(_tickCount/4+bot.Slot)%2==0;bot.Input=new InputState(effort?1:-1,0,effort,false,false);return;}
        // Fair information: current visible entities and timers only; never sample the match RNG or future drops.
        bool urgent=!BotSafe(_aiDanger,start,0,3.05f);
        if(urgent)
        {
            int escape=BotSearch(bot,start,_aiDanger,true,12);
            if(escape<0)escape=BotLeastRiskStep(bot,start);
            BotSteer(bot,start,escape);return;
        }
        if(bot.HasRemote&&!bot.PrevR&&BotRemoteOpportunity(bot))
        {bot.Input=new InputState(0,0,false,false,true);return;}
        int waypoint=BotSearch(bot,start,_aiDanger,false,_aiGraph.Count);
        bool useful=BotAdjacentCover(start)||BotEnemyInRange(bot.X,bot.Y,bot.Z,bot.FireLevel*.9f+.4f,bot.Id,bot.HasRed);
        bool centered=Math.Abs(bot.X-(start%_stage.Width+.5f))+Math.Abs(bot.Z-(start/_stage.Width+.5f))<.2f;
        if(!_practice&&useful&&centered&&!bot.PrevA&&bot.ActiveBombs<bot.BombCapacity&&_aiDanger.BombOwners[start] is null&&
            (!_aiLastBomb.TryGetValue(bot.Id,out var last)||_roundTimer-last>.65f))
        {
            _aiCandidate.OwnerId=bot.Id;_aiCandidate.X=start%_stage.Width+.5f;_aiCandidate.Z=start/_stage.Width+.5f;
            _aiCandidate.CellX=start%_stage.Width;_aiCandidate.CellZ=start/_stage.Width;_aiCandidate.Tier=bot.Tier;
            _aiCandidate.FireLevel=bot.FireLevel;_aiCandidate.Pumps=0;_aiCandidate.IsRed=bot.HasRed;_aiCandidate.IsRemote=false;
            // Reserve reaction/turning margin. Placement uses the real 2.5-second fuse in the authority.
            _aiCandidate.FuseTimer=BlastBomb.FuseSeconds-.25f;_aiCandidate.State=BombState.Resting;
            _aiCandidateBombs.Clear();_aiCandidateBombs.AddRange(_bombs);_aiCandidateBombs.Add(_aiCandidate);
            _aiPlacementDanger.Build(_softBlocks,_aiCandidateBombs,_hazards);
            int escape=BotSearch(bot,start,_aiPlacementDanger,true,8);
            if(escape>=0&&escape!=start)
            {
                _aiLastBomb[bot.Id]=_roundTimer;
                // Move immediately while placing; a bot obeys the same kick/collision rules as a human.
                BotSteer(bot,start,escape);bot.Input=bot.Input with {A=true};return;
            }
        }
        BotSteer(bot,start,waypoint);
    }
    private bool BotSafe(TimedDangerField field,int cell,float enter,float leave)
    {
        if(!field.Safe(cell,enter,leave))return false;
        int x=cell%_stage.Width,z=cell/_stage.Width;
        if(WaterUnsafe(x+.5f,z+.5f,leave))return false;
        if(_phase==RoundPhase.Hurry&&_stage.Id!=BlastStages.SunkenTemple)
        {
            int columns=(int)MathF.Floor(_hurryColTimer+leave);
            if(x<_closedLeft+columns||x>=_stage.Width-_closedRight-columns)return false;
        }
        return true;
    }
    private bool BotOpen(int cell,int start,BlastPlayer bot,TimedDangerField danger)
    {
        int x=cell%_stage.Width,z=cell/_stage.Width;
        if(_softBlocks.IsAlive(x,z))return false;
        return cell==start||danger.BombOwners[cell] is null;
    }
    /// <summary>Reusable finite BFS workspace. Arrival times reject routes that cross a blast before it expires.
    /// Searches do not allocate queues, closures, hash sets or opponent sets per decision.</summary>
    private int BotSearch(BlastPlayer bot,int start,TimedDangerField danger,bool escape,int maxDepth)
    {
        if(++_aiGeneration==int.MaxValue){Array.Clear(_aiSeen);_aiGeneration=1;}
        int head=0,tail=0;_aiQueue[tail++]=start;_aiSeen[start]=_aiGeneration;_aiDepth[start]=0;_aiFirst[start]=start;
        float bestScore=float.NegativeInfinity;int best=-1;
        while(head<tail)
        {
            int cell=_aiQueue[head++],depth=_aiDepth[cell];float arrival=depth*(1/PlayerSpeed+.045f);
            if(BotSafe(danger,cell,arrival,arrival+(escape?3.1f:.45f)))
            {
                if(escape)return _aiFirst[cell];
                float score=BotGoalScore(bot,cell,depth);
                if(score>bestScore){bestScore=score;best=_aiFirst[cell];}
            }
            if(depth>=maxDepth)continue;
            for(int d=0;d<4;d++)
            {
                int next=_aiGraph.Edges[cell*4+((d+bot.Slot)%4)];
                if(next<0||_aiSeen[next]==_aiGeneration||!BotOpen(next,start,bot,danger))continue;
                float nextArrival=(depth+1)*(1/PlayerSpeed+.045f);
                if(!BotSafe(danger,next,arrival,nextArrival+.08f))continue;
                // The current tile may already be burning; allow leaving it rather than freezing in place.
                if(cell!=start&&!BotSafe(danger,cell,arrival,nextArrival))continue;
                _aiSeen[next]=_aiGeneration;_aiDepth[next]=depth+1;_aiFirst[next]=cell==start?next:_aiFirst[cell];_aiQueue[tail++]=next;
            }
        }
        return best;
    }
    private float BotGoalScore(BlastPlayer bot,int cell,int depth)
    {
        float score=-depth*.45f;
        score+=_aiItemKind[cell] switch {1=>bot.HasHeart?2:26,2=>bot.HasRemote?2:22,3=>bot.HasRed?2:21,4=>bot.FireLevel>=3?2:20,5=>bot.BombCapacity>=8?2:18,_=>0};
        if(BotAdjacentCover(cell))score+=4;
        float x=cell%_stage.Width+.5f,z=cell/_stage.Width+.5f,y=_aiGraph.Height[cell];
        float nearest=_aiNearestOwner[cell]==bot.Id?_aiSecondNearest[cell]:_aiNearest[cell];
        if(float.IsFinite(nearest))score+=10-nearest*.8f;
        // Once water is close, prefer a reachable higher refuge instead of idling on a doomed low terrace.
        if(WaterLevelAfter(12)>=y)score+=y*3;
        return score;
    }
    private bool BotAdjacentCover(int cell) => cell>=0&&_aiCover[cell];
    private bool BotEnemyInRange(float x,float y,float z,float radius,string owner,bool red)
    {
        foreach(var p in _players.Values)if(p.Id!=owner&&p.Alive&&!p.Ghost&&InBlast(x,z,y,radius,p.X,p.Z,p.Y,red))return true;
        return false;
    }
    private bool BotRemoteOpportunity(BlastPlayer bot)
    {
        foreach(var b in _bombs)if(!b.Exploded&&b.OwnerId==bot.Id&&b.IsRemote&&b.State==BombState.Resting)
        {
            // The authority detonates the oldest resting remote. Do not evaluate a different bomb.
            if(InBlast(b.X,b.Z,BombY(b),b.BlastRadius+.45f,bot.X,bot.Z,bot.Y,b.IsRed))return false;
            return BotEnemyInRange(b.X,BombY(b),b.Z,b.BlastRadius,bot.Id,b.IsRed)||BotAdjacentCover(_aiGraph.Cell(b.X,b.Z));
        }
        return false;
    }
    private int BotLeastRiskStep(BlastPlayer bot,int start)
    {
        int best=-1;float value=float.NegativeInfinity;
        for(int d=0;d<4;d++)
        {
            int next=_aiGraph.Edges[start*4+d];if(next<0||!BotOpen(next,start,bot,_aiDanger))continue;
            int x=next%_stage.Width,z=next/_stage.Width;if(WaterUnsafe(x+.5f,z+.5f,.5f))continue;
            float score=float.IsPositiveInfinity(_aiDanger.Begins[next])?100:_aiDanger.Begins[next];
            score+=_aiGraph.Height[next]*.05f;
            if(score>value){value=score;best=next;}
        }
        return best;
    }
    private void BotSteer(BlastPlayer bot,int start,int waypoint)
    {
        if(waypoint<0){bot.Input=default;return;}
        int cx=start%_stage.Width,cz=start/_stage.Width,tx=waypoint%_stage.Width,tz=waypoint/_stage.Width;
        float x=tx+.5f,z=tz+.5f;
        if(tx!=cx&&Math.Abs(bot.Z-(cz+.5f))>.07f)x=cx+.5f;
        if(tz!=cz&&Math.Abs(bot.X-(cx+.5f))>.07f)z=cz+.5f;
        float dx=x-bot.X,dz=z-bot.Z,length=MathF.Sqrt(dx*dx+dz*dz);
        float speed=Math.Min(1,length/(PlayerSpeed*_aiSteeringInterval));
        bot.Input=length<.02f?default:new InputState(dx/length*speed,dz/length*speed,false,false,false);
    }
}
