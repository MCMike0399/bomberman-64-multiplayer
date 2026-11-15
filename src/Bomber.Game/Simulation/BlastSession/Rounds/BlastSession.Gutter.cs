// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using System.Numerics;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;

namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    private float GutterCurrent=>_stage.Id==BlastStages.InTheGutter?GutterRules.Current(_weatherTime,_weatherSeed,_roundNum):0;
    private bool InShallowCurrent(float x,float y,float z)=>_stage.Id==BlastStages.InTheGutter&&
        y<=.05f&&_stage.At((int)MathF.Floor(x),(int)MathF.Floor(z)).Kind==CellKind.Floor;
    private void DriftGutterPlayers(float dt)
    {
        float flow=GutterCurrent*GutterRules.PlayerScale;
        if(flow==0)return;
        foreach(var p in _players.Values)
            if(p.Alive&&!p.Ghost&&p.CarriedById is null&&!p.Airborne&&!p.PendingRoundStart&&InShallowCurrent(p.X,p.Y,p.Z))
                ApplyMoveDelta(p,flow*dt,0,dt);
    }
    private void DriftGutterBomb(BlastBomb bomb,float dt)
    {
        if(InShallowCurrent(bomb.X,BombY(bomb),bomb.Z))DriftGroundBomb(bomb,dt,new Vector2(GutterCurrent,0));
    }
}
