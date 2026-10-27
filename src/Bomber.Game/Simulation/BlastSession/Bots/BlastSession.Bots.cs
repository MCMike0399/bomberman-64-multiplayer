using Burbuja.Realtime.Abstractions;
using Bomber.Game.Simulation.Ai;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;

namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    private const float GoalHysteresis=3,BoredHysteresis=10,TransitMargin=.25f,BoredPenalty=12;
    private NavigationGraph _aiGraph=null!;
    private TimedDangerField _aiDanger=null!,_aiPlacementDanger=null!;
    private int[] _aiQueue=null!,_aiSeen=null!,_aiFirst=null!,_aiDepth=null!;
    private int[] _aiPursuit=null!;
    private int _aiPursuitStart;
    private bool _aiNeedsBreach;
    private byte[] _aiItemKind=null!;
    private bool[] _aiCover=null!;
    private float[] _aiNearest=null!,_aiSecondNearest=null!;
    private string?[] _aiNearestOwner=null!,_aiItemDropper=null!;
    private int _aiGeneration,_aiFieldTick=-1;
    private float _aiReaction=.12f,_aiSteeringInterval=4f/30;
    private BotDifficultyProfile _aiProfile=BotDifficultyProfile.Medium;
    private readonly Dictionary<string,int> _aiCommittedGoals=new(StringComparer.Ordinal);
    private readonly List<BlastBomb> _aiCandidateBombs=new(65);
    private readonly BlastBomb _aiCandidate=new() {Id="planning"};
    private readonly Dictionary<string,float> _aiLastBomb=new(StringComparer.Ordinal),_aiLastThreat=new(StringComparer.Ordinal);
    // V follows walking quickly for interception; Heading only builds up along a steady
    // course, so a body jittering beside a bomb is not mistaken for a kicker.
    private sealed class BotSighting {public float X,Z,VX,VZ,HeadingX,HeadingZ,LeadX,LeadZ;public int Tick=-1;}
    private readonly Dictionary<string,BotSighting> _aiSightings=new(StringComparer.Ordinal);
    private readonly List<(string Id,float X,float Y,float Z)> _aiRivalLeads=new(16);
    private void BotInitialize(GameSessionOptions options)
    {
        _aiProfile=BotDifficultyProfile.For(options.Settings.GetValueOrDefault("bot-difficulty"));
        _aiReaction=_aiProfile.ReactionSeconds;
        _aiSteeringInterval=MathF.Ceiling(_aiReaction*30)/30;
        _aiGraph=NavigationGraph.For(_stage);_aiDanger=new(_aiGraph,_stage);_aiPlacementDanger=new(_aiGraph,_stage);
        if(_stage.Id==BlastStages.BlizzardBattle)
        {
            // The seeded gust schedule is public; players see it and the throw arc already uses it.
            Func<float,System.Numerics.Vector2> forecast=ahead=>BlizzardRules.Wind(_weatherTime+ahead,_weatherSeed,_roundNum);
            _aiDanger.Wind=forecast;_aiPlacementDanger.Wind=forecast;
        }
        int count=_aiGraph.Count;_aiQueue=new int[count];_aiSeen=new int[count];_aiFirst=new int[count];_aiDepth=new int[count];_aiItemKind=new byte[count];_aiCover=new bool[count];_aiNearest=new float[count];_aiSecondNearest=new float[count];_aiNearestOwner=new string?[count];_aiItemDropper=new string?[count];
        _aiPursuit=new int[count];
    }
    private void BotReset(){BotResetNavigation();_aiFieldTick=-1;_aiLastBomb.Clear();_aiLastThreat.Clear();_aiCommittedGoals.Clear();_aiSightings.Clear();foreach(var p in _players.Values){p.BotThinkTimer=0;p.BotPlacingBomb=false;p.BotBombTapPending=false;}}
    private void BotRefreshField()
    {
        if(_aiFieldTick==_tickCount)return;
        _aiFieldTick=_tickCount;_aiDanger.Build(_softBlocks,_bombs,_hazards,_meteors);BotAddKickLanes(_aiDanger,_bombs);Array.Clear(_aiItemKind);Array.Clear(_aiItemDropper);
        foreach(var item in _items)
        {
            int cell=_aiGraph.Cell(item.X,item.Z,item.Tier);if(cell>=0){_aiItemDropper[cell]=item.DroppedBy;_aiItemKind[cell]=item.Type switch{"heart"=>1,"remote"=>2,"red"=>3,"fire"=>4,"bounce"=>6,"focus"=>8,"tiny" or "haste"=>9,"big" or "slow"=>10,_=>5};}
        }
        BotObserveRivals();
        // Shared observation grids avoid scanning every opponent/cover cell separately for every bot search.
        for(int cell=0;cell<_aiGraph.Count;cell++)
        {
            _aiCover[cell]=false;_aiNearest[cell]=_aiSecondNearest[cell]=float.PositiveInfinity;_aiNearestOwner[cell]=null;
            if(!_aiGraph.Walkable[cell])continue;
            for(int d=0;d<4;d++){int n=_aiGraph.Edges[cell*4+d];if(n>=0&&_softBlocks.IsAlive(_aiGraph.X(n),_aiGraph.Z(n))){_aiCover[cell]=true;break;}}
            float x=_aiGraph.X(cell)+.5f,z=_aiGraph.Z(cell)+.5f,y=_aiGraph.Height[cell];
            foreach(var enemy in _aiRivalLeads)
            {
                float distance=Math.Abs(enemy.X-x)+Math.Abs(enemy.Z-z)+Math.Abs(enemy.Y-y)*2;
                if(distance<_aiNearest[cell]){_aiSecondNearest[cell]=_aiNearest[cell];_aiNearest[cell]=distance;_aiNearestOwner[cell]=enemy.Id;}
                else if(distance<_aiSecondNearest[cell])_aiSecondNearest[cell]=distance;
            }
        }
    }
    private void BotThink(BlastPlayer bot,float dt)
    {
        BotObserveProgress(bot,dt);
        // Execute the already planned tap/retreat on successive authority ticks.
        // This releases A before hold-to-lift and lets the owner leave the newly placed bomb before reconsidering.
        if(bot.BotBombTapPending)
        {
            bot.BotBombTapPending=false;
            int cell=_aiGraph.Cell(bot.X,bot.Z,bot.Y);
            if(cell>=0)BotSteer(bot,cell,bot.BotEscapeNode);
            else bot.Input=default;
        }
        bot.BotThinkTimer-=dt;if(bot.BotThinkTimer>0)return;
        AiTestThinkCount++;bot.BotThinkTimer=_aiReaction;
        BotRefreshField();
        int start=_aiGraph.Cell(bot.X,bot.Z,bot.Y);if(start<0){bot.Input=default;return;}
        start=BotStableCell(bot,start);BotNoteCell(bot,start);
        if(bot.StunTimer>0||bot.CarriedById is not null)
        {bool effort=(_tickCount/4+bot.Slot)%2==0;bot.Input=new InputState(effort?1:-1,0,effort,false,false);return;}
        // Fair information: current visible entities and timers only; never sample the match RNG or future drops.
        bool urgent=!BotSafe(_aiDanger,start,0,4.05f);
        if(urgent)
        {
            int escape=BotSearch(bot,start,_aiDanger,true,_aiProfile.EscapeDepth);
            if(escape<0)escape=BotLeastRiskStep(bot,start);
            bot.BotPlacingBomb=false;BotSteer(bot,start,escape);return;
        }
        BotBuildPursuit(bot,start);
        bool rejectedPlacement=false;
        if(bot.BotPlacingBomb)
        {
            bot.BotPlacingBomb=false;
            if(BotBombPlan(bot,start,bot.FacingX,bot.FacingZ,0,out int escape))
            {
                _aiLastBomb[bot.Id]=_roundTimer;BotNoteBomb(bot);
                bot.BotEscapeNode=escape;
                bot.BotBombTapPending=true;bot.Input=new InputState(0,0,true,false,false);return;
            }
            // The orienting step changed the footprint/escape route. Resume real
            // navigation before retrying instead of oscillating between tiny turns.
            rejectedPlacement=true;
        }
        if(bot.HasRemote&&!bot.PrevR&&BotRemoteOpportunity(bot))
        {bot.Input=new InputState(0,0,false,false,true);return;}
        int waypoint=BotSearch(bot,start,_aiDanger,false,_aiProfile.TacticalDepth);
        float blastRadius=(bot.FireLevel*.9f+.4f)*(bot.HasRed?1.25f:1);
        bool useful=BotAdjacentCover(start)||BotEnemyInRange(bot.X,bot.Y,bot.Z,blastRadius,bot.Id,bot.HasRed)||BotCutsAnyApproach(bot,bot.X,bot.Y,bot.Z,blastRadius);
        // Bombs normally wait for a centered stance. An overdue attacker with a rival
        // in reach takes the off-center shot: two bots seeking alignment otherwise
        // dance forever. Placement and escape use the real foot position.
        float offset=Math.Abs(bot.X-(_aiGraph.X(start)+.5f))+Math.Abs(bot.Z-(_aiGraph.Z(start)+.5f));
        bool offCenterAttack=offset>=.2f&&offset<.6f&&BotAttackDue(bot)&&BotEnemyInRange(bot.X,bot.Y,bot.Z,blastRadius,bot.Id,bot.HasRed);
        bool centered=offset<.2f||offCenterAttack;
        if(!rejectedPlacement&&(!_practice||_tutorialLesson?.Id=="duel")&&useful&&centered&&!bot.PrevA&&bot.ActiveBombs<bot.BombCapacity&&_aiDanger.BombOwners[start] is null&&
            (!_aiLastBomb.TryGetValue(bot.Id,out var last)||_roundTimer-last>_aiProfile.BombCooldown))
        {
            float best=0,aimX=0,aimZ=0;
            // Evaluate the same near-foot placement and blocked-offset fallback as
            // the real action, including the small movement used to orient the bot.
            for(int direction=0;direction<32;direction++)
            {
                var (nx,nz)=BotPlacementDirections[direction];
                if(!TryBombPlacement(bot,nx,nz,.12f*(PlayerSpeed*bot.StatusSpeed)*_aiSteeringInterval,out float x,out float z))continue;
                // Once engagement is due, follow an open route toward the rival
                // instead of stopping at every incidental crate along that route.
                if(BotAttackDue(bot)&&!_aiNeedsBreach&&waypoint>=0&&waypoint!=start&&!BotEngages(bot,x,z))continue;
                if(offCenterAttack&&!BotEnemyInRange(x,_stage.SurfaceAt(x,z,bot.Y),z,blastRadius,bot.Id,bot.HasRed))continue;
                float score=BotBombUtility(bot,x,z);
                if(score>best){best=score;aimX=nx;aimZ=nz;}
            }
            if(best>0&&BotBombPlan(bot,start,aimX,aimZ,.12f*(PlayerSpeed*bot.StatusSpeed)*_aiSteeringInterval,out _))
            {
                // Orient using ordinary movement, then place on the next reaction.
                bot.BotPlacingBomb=true;bot.Input=new InputState(aimX*.12f,aimZ*.12f,false,false,false);return;
            }
        }
        BotSteer(bot,start,BotFinishStep(bot,start,waypoint));
    }
    /// <summary>Visible displacement since the previous authority tick, smoothed.
    /// Players see each other walk; no input or future decision is read.</summary>
    private void BotObserveRivals()
    {
        _aiRivalLeads.Clear();
        foreach(var p in _players.Values)
        {
            if(!_aiSightings.TryGetValue(p.Id,out var seen))_aiSightings[p.Id]=seen=new BotSighting();
            int elapsed=_tickCount-seen.Tick;
            if(seen.Tick<0||elapsed<=0||elapsed>12||!p.Alive||p.Ghost){seen.VX=seen.VZ=seen.HeadingX=seen.HeadingZ=0;}
            else
            {
                float dt=elapsed/30f,vx=(p.X-seen.X)/dt,vz=(p.Z-seen.Z)/dt;
                // Teleports, knockback and respawns are not walking.
                if(vx*vx+vz*vz>PlayerSpeed*PlayerSpeed*4){vx=0;vz=0;}
                seen.VX+=(vx-seen.VX)*.5f;seen.VZ+=(vz-seen.VZ)*.5f;
                seen.HeadingX+=(vx-seen.HeadingX)*.25f;seen.HeadingZ+=(vz-seen.HeadingZ)*.25f;
            }
            seen.X=p.X;seen.Z=p.Z;seen.Tick=_tickCount;
            // Where the rival will be after the profile's lead time, kept on walkable ground.
            seen.LeadX=p.X;seen.LeadZ=p.Z;
            float lx=p.X+seen.VX*_aiProfile.LeadSeconds,lz=p.Z+seen.VZ*_aiProfile.LeadSeconds;
            int lead=_aiGraph.Cell(lx,lz,p.Y);
            if(lead>=0&&_aiGraph.Walkable[lead]&&!_softBlocks.IsAlive((int)MathF.Floor(lx),(int)MathF.Floor(lz))){seen.LeadX=lx;seen.LeadZ=lz;}
            if(p.Alive&&!p.Ghost)_aiRivalLeads.Add((p.Id,seen.LeadX,p.Y,seen.LeadZ));
        }
    }
    /// <summary>A player walking into a resting bomb kicks it along their heading at
    /// once, too fast to dodge after it starts. Treat the visible lane ahead of a
    /// walker heading for a bomb as an impact threat; standing beside one is not.
    /// CPUs route around bombs and never kick deliberately (their seats are labeled),
    /// so only human walkers create lanes; otherwise every CPU duel looked kickable.</summary>
    private void BotAddKickLanes(TimedDangerField field,List<BlastBomb> bombs)
    {
        foreach(var p in _players.Values)
        {
            if(p.IsBot||!p.Alive||p.Ghost||p.CarriedById is not null||p.Airborne||!_aiSightings.TryGetValue(p.Id,out var seen))continue;
            float speed=MathF.Sqrt(seen.HeadingX*seen.HeadingX+seen.HeadingZ*seen.HeadingZ);
            if(speed<1)continue;
            float hx=seen.HeadingX/speed,hz=seen.HeadingZ/speed;
            foreach(var b in bombs)
            {
                // The owner may walk off a new or planned bomb without kicking it.
                if(b.Exploded||b.State!=BombState.Resting||Math.Abs(p.Y-BombY(b))>.6f||
                    (b.OwnerMayLeave||ReferenceEquals(b,_aiCandidate))&&b.OwnerId==p.Id)continue;
                float dx=b.X-p.X,dz=b.Z-p.Z,ahead=dx*hx+dz*hz;
                if(ahead<=0||ahead>KickLookahead||Math.Abs(dx*hz-dz*hx)>PlayerRadius+BlastBomb.Radius)continue;
                field.AddKickLane(_softBlocks,b,hx,hz);
            }
        }
    }
    private const float KickLookahead=3;
    private (float X,float Z) BotLeadPosition(BlastPlayer p) =>
        _aiSightings.TryGetValue(p.Id,out var seen)&&seen.Tick==_tickCount?(seen.LeadX,seen.LeadZ):(p.X,p.Z);
    private float BotBombUtility(BlastPlayer bot,float x,float z)
    {
        float score=0,y=_stage.SurfaceAt(x,z,bot.Y),radius=(bot.FireLevel*.9f+.4f)*(bot.HasRed?1.25f:1);
        int reach=(int)MathF.Ceiling(radius);
        for(int cx=(int)x-reach;cx<=(int)x+reach;cx++)for(int cz=(int)z-reach;cz<=(int)z+reach;cz++)
            if(_softBlocks.IsAlive(cx,cz)&&InBlast(x,z,y,radius,cx+.5f,cz+.5f,_stage.At(cx,cz).Tier,bot.HasRed))score++;
        if(BotEnemyInRange(x,y,z,radius,bot.Id,bot.HasRed))score+=2+_aiProfile.PressureWeight;
        return score+BotZoneScore(bot,x,y,z,radius);
    }
    /// <summary>A deliberate pressure bomb also limits a nearby opponent's visible routes.
    /// No future movement or hidden drops are predicted.</summary>
    private float BotZoneScore(BlastPlayer bot,float x,float y,float z,float radius)
    {
        float score=0;
        foreach(var enemy in _players.Values)if(enemy.Id!=bot.Id&&enemy.Alive&&!enemy.Ghost)
        {
            float distance=Math.Abs(enemy.X-x)+Math.Abs(enemy.Z-z)+Math.Abs(enemy.Y-y)*2;
            if(distance<=radius||distance>=radius+1.5f)continue;
            if(BotExitCount(_aiGraph.Cell(enemy.X,enemy.Z,enemy.Y))<=2)score+=_aiProfile.PressureWeight;
            else if(BotAttackDue(bot)&&BotCutsApproach(enemy,x,y,z))score+=_aiProfile.PressureWeight*.75f;
        }
        return score;
    }
    /// <summary>A rival lined up just beyond reach and visibly walking toward the bomb
    /// must stop or detour: its next steps enter the cross. A stationary rival is
    /// better approached and hit than fenced from a distance.</summary>
    private bool BotCutsApproach(BlastPlayer enemy,float x,float y,float z) =>
        Math.Abs(enemy.Y-y)<.5f&&(Math.Abs(enemy.X-x)<.5f||Math.Abs(enemy.Z-z)<.5f)&&
        _aiSightings.TryGetValue(enemy.Id,out var seen)&&seen.HeadingX*(x-enemy.X)+seen.HeadingZ*(z-enemy.Z)>.3f*PlayerSpeed;
    /// <summary>Fencing breaks standoffs, such as two CPUs facing each other in a lane.
    /// Only an overdue attacker spends bombs on it; landing hits stays the better move.</summary>
    private bool BotCutsAnyApproach(BlastPlayer bot,float x,float y,float z,float radius)
    {
        if(!BotAttackDue(bot))return false;
        foreach(var enemy in _players.Values)if(enemy.Id!=bot.Id&&enemy.Alive&&!enemy.Ghost)
        {
            float distance=Math.Abs(enemy.X-x)+Math.Abs(enemy.Z-z)+Math.Abs(enemy.Y-y)*2;
            if(distance>radius&&distance<radius+1.5f&&BotCutsApproach(enemy,x,y,z))return true;
        }
        return false;
    }
    /// <summary>While pursuit is due, only bombs that threaten or corner a rival are worth the stop.</summary>
    private bool BotEngages(BlastPlayer bot,float x,float z)
    {
        float y=_stage.SurfaceAt(x,z,bot.Y),radius=(bot.FireLevel*.9f+.4f)*(bot.HasRed?1.25f:1);
        return BotEnemyInRange(x,y,z,radius,bot.Id,bot.HasRed)||BotCutsAnyApproach(bot,x,y,z,radius);
    }
    private bool BotBombPlan(BlastPlayer bot,int start,float aimX,float aimZ,float advance,out int escape)
    {
        escape=-1;
        if(!TryBombPlacement(bot,aimX,aimZ,advance,out float x,out float z)||BotBombUtility(bot,x,z)<=0)return false;
        // Revalidate engagement after the orienting reaction: a farming plan
        // may become overdue before its actual press. Sealed routes still
        // permit demolition; an open pursuit route keeps priority.
        if(BotAttackDue(bot)&&!_aiNeedsBreach&&_aiPursuitStart>=0&&!BotEngages(bot,x,z))return false;
        _aiCandidate.OwnerId=bot.Id;_aiCandidate.X=x;_aiCandidate.Z=z;
        _aiCandidate.CellX=(int)x;_aiCandidate.CellZ=(int)z;_aiCandidate.Tier=(int)MathF.Round(_stage.SurfaceAt(x,z,bot.Y));
        _aiCandidate.LandingY=_stage.SurfaceAt(x,z,bot.Y);
        _aiCandidate.FireLevel=bot.FireLevel;_aiCandidate.Pumps=0;_aiCandidate.Mode=bot.HasRemote?"normal":bot.BombType;
        _aiCandidate.FuseTimer=(_aiCandidate.IsBouncy?3.5f:BlastBomb.FuseSeconds)-.25f;_aiCandidate.State=BombState.Resting;
        _aiCandidateBombs.Clear();_aiCandidateBombs.AddRange(_bombs);_aiCandidateBombs.Add(_aiCandidate);
        _aiPlacementDanger.Build(_softBlocks,_aiCandidateBombs,_hazards,_meteors);BotAddKickLanes(_aiPlacementDanger,_aiCandidateBombs);
        escape=BotSearch(bot,start,_aiPlacementDanger,true,_aiProfile.EscapeDepth);
        return escape>=0&&escape!=start;
    }
    private bool BotSafe(TimedDangerField field,int cell,float enter,float leave)
    {
        if(!field.Safe(cell,enter,leave))return false;
        int x=_aiGraph.X(cell),z=_aiGraph.Z(cell);
        if(WaterUnsafe(x+.5f,z+.5f,leave,_aiGraph.Height[cell]))return false;
        if(_phase==RoundPhase.Hurry&&!_stage.RisingWater&&_stage.Id!=BlastStages.RockGarden)
        {
            int columns=(int)MathF.Floor((_hurryColTimer+leave)/BlastStages.HurryColumnSeconds(_stage.Id));
            if(x<_closedLeft+columns||x>=_stage.Width-_closedRight-columns)return false;
        }
        return true;
    }
    private bool BotOpen(int cell,int start,BlastPlayer bot,TimedDangerField danger)
    {
        int x=_aiGraph.X(cell),z=_aiGraph.Z(cell);
        if(_softBlocks.IsAlive(x,z)&&Math.Abs(_stage.SurfaceAt(x+.5f,z+.5f)-_aiGraph.Height[cell])<1)return false;
        if(cell==start)return true;
        if(danger.BombOwners[cell] is not null||BotAvoidsCell(bot,cell))return false;
        // Terrain edges alone do not describe routes through visible player bodies.
        // Use the same body clearance as authority contacts at the destination.
        float px=x+.5f,pz=z+.5f,y=_aiGraph.Height[cell];
        foreach(var other in _players.Values)
            if(other.Id!=bot.Id&&HasBody(other)&&Math.Abs(other.Y-y)<HeadHeight-.05f&&
                Dist2(px,pz,other.X,other.Z)<(PlayerRadius*2+.04f)*(PlayerRadius*2+.04f))return false;
        return true;
    }
    /// <summary>Reusable finite BFS workspace. Arrival times reject routes that cross a blast before it expires.
    /// Searches do not allocate queues, closures, hash sets or opponent sets per decision.</summary>
    private int BotSearch(BlastPlayer bot,int start,TimedDangerField danger,bool escape,int maxDepth)
    {
        // An escape must outlast the threat it is escaping. A 3.5-second bouncing
        // fuse otherwise looks safe to stand on through Easy's 3.1-second horizon,
        // so every placement was rejected as having nowhere better to go.
        float horizon=escape?Math.Max(_aiProfile.SafetyHorizonSeconds,float.IsFinite(danger.Ends[start])?Math.Min(danger.Ends[start],4.6f):0):.45f;
        // Commitment stabilizes travel, but an already reached goal must not pay
        // the bot to stay there after its bomb's danger has cleared.
        if(!escape&&_aiCommittedGoals.TryGetValue(bot.Id,out int reached)&&reached==start)
            _aiCommittedGoals.Remove(bot.Id);
        if(++_aiGeneration==int.MaxValue){Array.Clear(_aiSeen);_aiGeneration=1;}
        int head=0,tail=0;_aiQueue[tail++]=start;_aiSeen[start]=_aiGeneration;_aiDepth[start]=0;_aiFirst[start]=start;
        float bestScore=float.NegativeInfinity,committedScore=float.NegativeInfinity;int best=-1,bestCell=-1,committedFirst=-1;
        int committed=!escape&&_aiCommittedGoals.TryGetValue(bot.Id,out int held)?held:-1;
        while(head<tail)
        {
            int cell=_aiQueue[head++],depth=_aiDepth[cell];float arrival=depth*(1/(PlayerSpeed*bot.StatusSpeed)+.045f);
            if(BotSafe(danger,cell,arrival,arrival+horizon))
            {
                if(escape)return _aiFirst[cell];
                float score=BotGoalScore(bot,cell,depth);
                if(BotBoredWith(bot,cell))score-=BoredPenalty;
                if(score>bestScore){bestScore=score;best=_aiFirst[cell];bestCell=cell;}
                if(cell==committed){committedScore=score;committedFirst=_aiFirst[cell];}
            }
            if(depth>=maxDepth)continue;
            for(int d=0;d<4;d++)
            {
                int next=_aiGraph.Edges[cell*4+((d+bot.Slot)%4)];
                if(next<0||_aiSeen[next]==_aiGeneration||!BotOpen(next,start,bot,danger))continue;
                float nextArrival=(depth+1)*(1/(PlayerSpeed*bot.StatusSpeed)+.045f);
                // A body overlaps a cell after its center leaves, and turns add
                // alignment time; tight timed transits killed escaping bots.
                if(!BotSafe(danger,next,arrival,nextArrival+TransitMargin))continue;
                // The current tile may already be burning; allow leaving it rather than freezing in place.
                if(cell!=start&&!BotSafe(danger,cell,arrival,nextArrival))continue;
                _aiSeen[next]=_aiGeneration;_aiDepth[next]=depth+1;_aiFirst[next]=cell==start?next:_aiFirst[cell];_aiQueue[tail++]=next;
            }
        }
        // Hysteresis: a still-reachable goal survives small score changes. Nearly
        // equal goals in opposite directions otherwise swap on every jitter of a
        // nearby body, and the bot shuffles one step forward and one back.
        // A bored bot (mirroring a rival across one boundary) holds its fresh goal
        // more firmly, while a clearly better chance, such as a target moving away, still wins.
        if(committedFirst>=0&&committedScore>=bestScore-(BotBored(bot)?BoredHysteresis:GoalHysteresis)){best=committedFirst;bestCell=committed;}
        if(!escape&&bestCell>=0)BotCommitGoal(bot,bestCell);
        return best;
    }
    private void BotCommitGoal(BlastPlayer bot,int cell)
    {
        if(cell>=0)_aiCommittedGoals[bot.Id]=cell;
    }
    private int BotExitCount(int cell)
    {
        if(cell<0)return 0;
        int exits=0;
        for(int d=0;d<4;d++)
        {
            int next=_aiGraph.Edges[cell*4+d];
            if(next>=0&&!_softBlocks.IsAlive(_aiGraph.X(next),_aiGraph.Z(next))&&_aiDanger.BombOwners[next] is null)exits++;
        }
        return exits;
    }
    private float BotGoalScore(BlastPlayer bot,int cell,int depth)
    {
        float score=-depth*.45f;
        bool attackDue=BotAttackDue(bot);
        // Evaluate marginal value, not just pickup presence. Extra capacity and
        // interchangeable equipment must not send a well-equipped CPU farming forever.
        float itemUtility=_aiItemKind[cell] switch {
            9=>bot.StatusEffect=="none"?12:0,10=>-20,
            1=>bot.HasHeart?0:26,
            2=>bot.HasRemote?0:bot.BombType=="normal"?22:5,
            3=>bot.HasRed?0:bot.BombType=="normal"?21:5,
            4=>bot.FireLevel>=bot.MaxFire?0:bot.FireLevel==1?20:14,
            5=>bot.BombCapacity>=bot.MaxBombs?0:bot.BombCapacity<=3?18:bot.BombCapacity==4?8:3,
            6=>bot.BombType=="bounce"?0:bot.BombType=="normal"?21:5,
            8=>bot.Ability=="none"?6:0,_=>0};
        // Rich farming pockets are useful between attacks; they should not reset
        // the engagement clock indefinitely. A missing heart retains survival value.
        if(attackDue&&_aiItemKind[cell]!=1)itemUtility*=.25f;
        score+=(_aiItemDropper[cell]==bot.Id?0:_aiProfile.ItemUtilityScale)*itemUtility;
        // Progress along real directed paths can require moving away from the rival.
        // Use a relative reward so a distant rival cannot swamp nearby survival items.
        if(attackDue&&_aiPursuitStart>=0&&_aiPursuit[cell]>=0)
            score+=(_aiPursuitStart-_aiPursuit[cell])*2.5f*_aiProfile.PressureWeight;
        if(BotAdjacentCover(cell))score+=3;
        float x=_aiGraph.X(cell)+.5f,z=_aiGraph.Z(cell)+.5f,y=_aiGraph.Height[cell];
        float nearest=_aiNearestOwner[cell]==bot.Id?_aiSecondNearest[cell]:_aiNearest[cell];
        if(float.IsFinite(nearest))
        {
            // Easy leaves room to breathe; Hard contests bomb range and closes routes.
            // Farming a crate does not count as engaging an opponent. After a
            // bounded collection interval, favor reachable attack range again.
            float preferred=attackDue?Math.Min(1,_aiProfile.PreferredEnemyDistance):_aiProfile.PreferredEnemyDistance;
            score+=10-Math.Abs(nearest-preferred)*_aiProfile.PressureWeight;
            // Reaching actual visible blast range is worth a detour. Pure geometric
            // distance can otherwise keep a bot below a cliff instead of using its ramp.
            if(preferred<=1&&BotEnemyInRange(x,y,z,(bot.FireLevel*.9f+.4f)*(bot.HasRed?1.25f:1),bot.Id,bot.HasRed))
                score+=(attackDue?11:3)*_aiProfile.PressureWeight;
            if(nearest<5)score-=(4-BotExitCount(cell))*_aiProfile.TrapPenalty;
        }
        if(_aiCommittedGoals.TryGetValue(bot.Id,out int goal))
        {
            int distance=Math.Abs(_aiGraph.X(cell)-_aiGraph.X(goal))+Math.Abs(_aiGraph.Z(cell)-_aiGraph.Z(goal));
            score+=_aiProfile.GoalCommitmentBonus/(1+distance);
        }
        // Once water is close, prefer a reachable higher refuge instead of idling on a doomed low terrace.
        if(WaterLevelAfter(12)>=y)score+=y*3;
        return score;
    }
    private bool BotAdjacentCover(int cell) => cell>=0&&_aiCover[cell];
    private bool BotEnemyInRange(float x,float y,float z,float radius,string owner,bool red)
    {
        foreach(var p in _players.Values)if(p.Id!=owner&&p.Alive&&!p.Ghost&&BlastGeometry.HitsPlayer(_stage,_softBlocks,x,y,z,radius,p.X,p.Y,p.Z,red))return true;
        return false;
    }
    private bool BotRemoteOpportunity(BlastPlayer bot)
    {
        foreach(var b in _bombs)if(!b.Exploded&&b.OwnerId==bot.Id&&b.IsRemote&&b.State!=BombState.Carried)
        {
            // Match authority selection: the oldest eligible remote may be rolling or flying.
            if(BlastGeometry.HitsPlayer(_stage,_softBlocks,b.X,BombY(b),b.Z,b.BlastRadius+.45f,bot.X,bot.Y,bot.Z,b.IsRed))return false;
            if(!BotEnemyInRange(b.X,BombY(b),b.Z,b.BlastRadius,bot.Id,b.IsRed)&&!BotAdjacentCover(_aiGraph.Cell(b.X,b.Z,BombY(b))))return false;
            int cell=_aiGraph.Cell(bot.X,bot.Z,bot.Y);if(cell<0)return false;
            // Triggering the oldest remote can ignite a second bomb beside its owner.
            _aiPlacementDanger.Build(_softBlocks,_bombs,_hazards,_meteors,b);
            return BotSafe(_aiPlacementDanger,cell,0,.3f);
        }
        return false;
    }
    private int BotLeastRiskStep(BlastPlayer bot,int start)
    {
        int best=-1;float value=float.NegativeInfinity;
        for(int d=0;d<4;d++)
        {
            int next=_aiGraph.Edges[start*4+d];if(next<0||!BotOpen(next,start,bot,_aiDanger))continue;
            int x=_aiGraph.X(next),z=_aiGraph.Z(next);if(WaterUnsafe(x+.5f,z+.5f,.5f,_aiGraph.Height[next]))continue;
            float score=float.IsPositiveInfinity(_aiDanger.Begins[next])?100:_aiDanger.Begins[next];
            score+=_aiGraph.Height[next]*.05f;
            if(score>value){value=score;best=next;}
        }
        return best;
    }
    private void BotSteer(BlastPlayer bot,int start,int waypoint)
    {
        waypoint=BotIdleWaypoint(bot,start,waypoint);
        BotTrackWaypoint(bot,waypoint);
        if(waypoint<0){bot.Input=default;return;}
        int cx=_aiGraph.X(start),cz=_aiGraph.Z(start),tx=_aiGraph.X(waypoint),tz=_aiGraph.Z(waypoint);
        float x=tx+.5f,z=tz+.5f;
        if(tx!=cx&&Math.Abs(bot.Z-(cz+.5f))>.07f)x=cx+.5f;
        if(tz!=cz&&Math.Abs(bot.X-(cx+.5f))>.07f)z=cz+.5f;
        float dx=x-bot.X,dz=z-bot.Z,length=MathF.Sqrt(dx*dx+dz*dz),moveSpeed=PlayerSpeed*bot.StatusSpeed;
        float speed=Math.Min(1,length/(moveSpeed*_aiSteeringInterval));
        float vx=length<.02f?0:dx/length*speed,vz=length<.02f?0:dz/length*speed;
        // A gust adds drift to every input. Without leaning into it, the slowing
        // approach settles just outside the alignment tolerance and never turns.
        var wind=BlizzardWind*BlizzardRules.PlayerWindScale;
        if(wind!=System.Numerics.Vector2.Zero)
        {
            vx-=wind.X/moveSpeed;vz-=wind.Y/moveSpeed;
            float magnitude=MathF.Sqrt(vx*vx+vz*vz);
            if(magnitude>1){vx/=magnitude;vz/=magnitude;}
        }
        bot.Input=BotUnblockedInput(bot,start,vx*vx+vz*vz<.0004f?default:new InputState(vx,vz,false,false,false));
    }
}
