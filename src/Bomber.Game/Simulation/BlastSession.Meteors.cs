// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using Burbuja.Realtime.Abstractions;
using Bomber.Game.Simulation.Ai;
namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    private void TickGardenMeteors(ISessionContext context,float dt)
    {
        for(int i=_meteors.Count-1;i>=0;i--)
        {
            var meteor=_meteors[i];meteor.Remaining-=dt;
            if(meteor.Remaining>0)continue;
            _meteors.RemoveAt(i);
            // Ordinary spherical blast rules resolve shielding, cover, hearts,
            // chains and loot. A fireball has no player owner or inventory cost.
            ExplodeBomb(context,new BlastBomb{Id=meteor.Id,X=meteor.X,Z=meteor.Z,
                CellX=(int)meteor.X,CellZ=(int)meteor.Z,FireLevel=1});
        }
        _meteorTimer-=dt;if(_meteorTimer>0)return;
        float hurryAge=Math.Max(0,_roundTimer-(_roundSeconds-60));
        _meteorTimer=Math.Max(.35f,.9f-hurryAge*.01f);
        var living=_players.Values.Where(p=>p.Alive&&!p.Ghost).OrderBy(p=>p.Slot).ToArray();
        if(living.Length==0)return;
        float x,z;
        if(_meteorSequence%3==0)
        {
            // Aim at an observed position, never track the player after warning.
            var target=living[(_meteorSequence/3)%living.Length];x=target.X;z=target.Z;
        }
        else
        {
            int cx,cz;
            do {cx=context.Random.Next(1,_stage.Width-1);cz=context.Random.Next(1,_stage.Height-1);}
            while(!_stage.At(cx,cz).IsPassable);
            x=cx+.5f;z=cz+.5f;
        }
        _meteorSequence++;
        if(!_stage.At((int)x,(int)z).IsPassable)return;
        _meteors.Add(new BlastMeteor{Id=NextBombId(),X=x,Z=z});
    }

}
