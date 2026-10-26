using System.Text.Json;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Rules;
using static BlastCircuit.Rendering.ArtGeometry;
using BlastCircuit.Arena.Effects;
using BlastCircuit.Audio;
using BlastCircuit.Rendering;
using BlastCircuit.Arena.Stages;

namespace BlastCircuit.Arena;

/// <summary>Advisory throw arcs for local carriers, sharing BombThrowRules with authority.</summary>
public partial class ArenaView
{
    private void ClearThrowPreviews()
    {
        foreach(var view in _throwPreviews.Values){view.HidePreview();view.QueueFree();}
        _throwPreviews.Clear();ThrowPreviews=[];
    }
    private void UpdateThrowPreviews(JsonElement data,byte[] cover)
    {
        if(!_battleActive||data.GetProperty("phase").GetInt32() is not (1 or 2))
        {ClearThrowPreviews();return;}
        if(_terrain==null)return;
        var states=new List<ThrowPreviewState>();var seen=new HashSet<string>();
        foreach(var player in data.GetProperty("players").EnumerateArray())
        {
            var id=player.GetProperty("id").GetString()!;
            if(!_localInputs.TryGetValue(id,out var input)||player.GetProperty("bot").GetBoolean()||!player.GetProperty("alive").GetBoolean()||
                player.GetProperty("ghost").GetBoolean()||player.GetProperty("stunned").GetBoolean()||player.GetProperty("carrying").GetString()!="bomb")continue;
            var bomb=data.GetProperty("bombs").EnumerateArray().FirstOrDefault(b=>b.TryGetProperty("carrierId",out var carrier)&&carrier.GetString()==id);
            if(bomb.ValueKind==JsonValueKind.Undefined)continue;
            int pumps=bomb.GetProperty("pumps").GetInt32();
            var origin=new System.Numerics.Vector3(F(player,"x"),F(player,"y")+BombThrowRules.LaunchHeight,F(player,"z"));
            bool aimSet=player.TryGetProperty("aimSet",out var set)&&set.GetBoolean();
            bool aiming=player.TryGetProperty("aiming",out var active)&&active.GetBoolean();
            float range=aimSet?F(player,"aimRange"):0;
            bool bounce=bomb.TryGetProperty("mode",out var mode)&&mode.GetString()=="bounce";
            var velocity=aimSet?BombThrowRules.AimedVelocity(F(player,"aimX"),F(player,"aimZ"),range,pumps):
                BombThrowRules.Velocity(F(player,"fx"),F(player,"fz"),MathF.Sqrt(input.X*input.X+input.Z*input.Z),pumps);
            var projection=BombThrowRules.Project(origin,velocity,_terrain.AimSurface,_=>false,
                bomb.GetProperty("remote").GetBoolean()?5:F(bomb,"fuse"),_terrain.AimCellKind,
                (x,z)=>_terrain.AimCellKind(x+.5f,z+.5f),(x,z)=>_terrain.AimHasCover(x,z,cover),
                waterLevel:data.GetProperty("hurryMode").GetString()=="water"&&data.GetProperty("phase").GetInt32()==2?F(data,"waterLevel"):null,
                bounce:bounce,gravityScale:BombThrowRules.Weight(pumps),pumps:pumps,stage:_terrain.Layout,windAt:data.TryGetProperty("blizzard",out var weather)&&weather.ValueKind==JsonValueKind.Object?
                    seconds=>BlizzardRules.Wind(weather.GetProperty("time").GetSingle()+seconds+BombThrowRules.SimulationStep,weather.GetProperty("seed").GetInt32(),weather.GetProperty("round").GetInt32()):null);
            if(!_throwPreviews.TryGetValue(id,out var view))
            {view=new BombThrowPreview();_effects.AddChild(view);_throwPreviews[id]=view;}
            view.Update(projection,pumps,player.GetProperty("slot").GetInt32(),aimSet,aiming,range);seen.Add(id);
            view.UpdateScreen(_camera);
            states.Add(new(id,pumps,projection.Last.X,projection.Last.Y,projection.Last.Z,projection.End.ToString(),projection.Points.Count,view.LandingMarkerVisible,view.EndpointLabel,aimSet,aiming,range,view.EndpointMarkerVisible,view.EndpointScreenPosition.X,view.EndpointScreenPosition.Y,view.EndpointEdgeClamped));
        }
        foreach(var id in _throwPreviews.Keys.Where(id=>!seen.Contains(id)).ToArray())
        {_throwPreviews[id].HidePreview();_throwPreviews[id].QueueFree();_throwPreviews.Remove(id);}
        ThrowPreviews=states;
    }
}
