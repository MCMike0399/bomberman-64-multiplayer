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
    private readonly Dictionary<string,System.Numerics.Vector3> _localCarriers=[];
    /// <summary>Arena position of a local player currently previewing a carried-bomb throw.</summary>
    public bool LocalCarrier(string playerId,out System.Numerics.Vector3 position)=>_localCarriers.TryGetValue(playerId,out position);

    /// <summary>The arena X/Z under a viewport position: the first terrain surface along the camera
    /// ray, so a raised tier is picked where it is drawn. Off the stage, the floor plane is used.</summary>
    public System.Numerics.Vector2? ArenaPoint(Godot.Vector2 screen)
    {
        if(_terrain==null||!_camera.IsInsideTree())return null;
        var origin=_camera.ProjectRayOrigin(screen);var direction=_camera.ProjectRayNormal(screen);
        if(direction.Y>-.01f)return null;
        const float top=8*TempleStageView.HeightScale,bottom=-TempleStageView.HeightScale,step=.05f;
        float Height(float t){var p=origin+direction*t;return p.Y-_terrain.AimSurface(p.X,p.Z)*TempleStageView.HeightScale;}
        float start=Math.Max(0,(origin.Y-top)/-direction.Y),end=(origin.Y-bottom)/-direction.Y;
        float previous=start;
        for(float t=start+step;t<=end;t+=step)
        {
            if(Height(t)>0){previous=t;continue;}
            float low=previous,high=t;
            for(int i=0;i<12;i++){float mid=(low+high)*.5f;if(Height(mid)>0)low=mid;else high=mid;}
            var hit=origin+direction*high;
            return new(hit.X,hit.Z);
        }
        var floor=origin+direction*((origin.Y)/-direction.Y);
        return new(floor.X,floor.Z);
    }
    public Godot.Vector2 ScreenPoint(System.Numerics.Vector3 arena)=>_camera.UnprojectPosition(new(arena.X,arena.Y*TempleStageView.HeightScale,arena.Z));
    private void ClearThrowPreviews()
    {
        foreach(var view in _throwPreviews.Values){view.HidePreview();view.QueueFree();}
        _throwPreviews.Clear();ThrowPreviews=[];_localCarriers.Clear();
    }
    private void UpdateThrowPreviews(JsonElement data,byte[] cover)
    {
        if(!_battleActive||data.GetProperty("phase").GetInt32() is not (1 or 2))
        {ClearThrowPreviews();return;}
        if(_terrain==null)return;
        var states=new List<ThrowPreviewState>();var seen=new HashSet<string>();_localCarriers.Clear();
        foreach(var player in data.GetProperty("players").EnumerateArray())
        {
            var id=player.GetProperty("id").GetString()!;
            if(!_localInputs.TryGetValue(id,out var input)||player.GetProperty("bot").GetBoolean()||!player.GetProperty("alive").GetBoolean()||
                player.GetProperty("ghost").GetBoolean()||player.GetProperty("stunned").GetBoolean()||player.GetProperty("carrying").GetString()!="bomb")continue;
            var bomb=data.GetProperty("bombs").EnumerateArray().FirstOrDefault(b=>b.TryGetProperty("carrierId",out var carrier)&&carrier.GetString()==id);
            if(bomb.ValueKind==JsonValueKind.Undefined)continue;
            int pumps=bomb.GetProperty("pumps").GetInt32();
            var origin=new System.Numerics.Vector3(F(player,"x"),F(player,"y")+BombThrowRules.LaunchHeight,F(player,"z"));
            _localCarriers[id]=new(F(player,"x"),F(player,"y"),F(player,"z"));
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
