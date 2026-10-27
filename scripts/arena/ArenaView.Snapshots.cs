using Godot;
using System.Text.Json;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using static BlastCircuit.Rendering.ArtGeometry;
using BlastCircuit.Arena.Effects;
using BlastCircuit.Arena.Models;
using BlastCircuit.Rendering;
using BlastCircuit.Arena.Stages;

namespace BlastCircuit.Arena;

/// <summary>Reconciles accepted state snapshots into rendered players, bombs, items, cover and hurry walls.</summary>
public partial class ArenaView
{
    public void State(JsonElement data,long tick=-1)
    {
        if(_terrain==null)return;
        _snapshotTick=tick<0?_snapshotTick+2:tick;
        int round=(int)F(data,"round");
        if(round!=_lastRound){_lastRound=round;_wasHurry=false;_pickups.Clear();}
        bool eventsAvailable=data.TryGetProperty("events",out _);
        bool hurry=(int)F(data,"phase")==2;
        if(!eventsAvailable&&hurry&&!_wasHurry)_audio.Hurry();_wasHurry=hurry;
        if(data.TryGetProperty("waterLevel",out var water))_terrain.SetWaterLevel(water.GetSingle());
        string encoding=data.GetProperty("softBlocks").GetString()!;
        if(encoding!=_softBlockEncoding)
        {
            _softBlockBits=Convert.FromBase64String(encoding);_softBlockEncoding=encoding;
            for(int i=0;i<_terrain.Width*_terrain.Depth;i++)
            {
                bool exists=i/8<_softBlockBits.Length&&(_softBlockBits[i/8]&(1<<(i%8)))!=0;
                if(exists&&!_blocks.ContainsKey(i)){var block=_terrain.CreateCover(i);_dynamic.AddChild(block);_blocks[i]=block;}
                if(!exists&&_blocks.Remove(i,out var old)){Debris(old.Position);old.QueueFree();}
            }
        }
        var seen=_seenEntities;seen.Clear();
        foreach(var p in data.GetProperty("players").EnumerateArray())
        {
            string id="p:"+p.GetProperty("id").GetString();bool ghost=p.GetProperty("ghost").GetBoolean();
            if(!p.GetProperty("alive").GetBoolean()&&!ghost)continue;
            seen.Add(id);int slot=p.GetProperty("slot").GetInt32();
            int appearance=p.TryGetProperty("appearance",out var selected)?selected.GetInt32():slot;
            var color=Colors[Math.Clamp(appearance,0,Colors.Length-1)];
            if(!_entities.TryGetValue(id,out var node))
            {
                node=new BomberModel(_art,color,Bomber.Game.Matches.PlayerNames.Display(p.GetProperty("name").GetString(),slot));_dynamic.AddChild(node);_entities[id]=node;node.Position=PositionOf(p);
            }
            ((BomberModel)node).SetSuitColor(color);
            ((BomberModel)node).StatusScale=p.TryGetProperty("statusScale",out var statusScale)?statusScale.GetSingle():1;
            ((BomberModel)node).KickWindup(F(p,"kickWindup"));
            _targets[id]=PositionOf(p);
            if(!_playerMotion.TryGetValue(id,out var motion))_playerMotion[id]=new SnapshotMotion(node.Position,_snapshotTick);
            else motion.Push(_targets[id],_snapshotTick);
            float angle=Mathf.Atan2(F(p,"fx"),F(p,"fz"));_facings[id]=angle;_lastLooks[id]=(color,angle);
            if(p.TryGetProperty("latchedTo",out var latch)&&latch.ValueKind==JsonValueKind.String)_ghostHosts[id]="p:"+latch.GetString();
            else _ghostHosts.Remove(id);
            var carrying=p.GetProperty("carrying");
            ((BomberModel)node).Pose(carrying.ValueKind!=JsonValueKind.Null,p.GetProperty("stunned").GetBoolean(),ghost,carrying.ValueKind==JsonValueKind.String&&carrying.GetString()=="player");
            bool falling=p.TryGetProperty("falling",out var fall)&&fall.GetBoolean();
            bool held=p.TryGetProperty("carriedBy",out var heldBy)&&heldBy.ValueKind==JsonValueKind.String;
            if(held)_holders[id]="p:"+heldBy.GetString();else _holders.Remove(id);
            ((BomberModel)node).RecoveryPose(held,!falling&&p.TryGetProperty("airborne",out var air)&&air.GetBoolean());
            if(p.TryGetProperty("pickupSerial",out var serial))
            {
                int value=serial.GetInt32();
                if(_pickups.TryGetValue(id,out int previous)&&value>previous&&p.TryGetProperty("lastPickup",out var pickup))
                {
                    string kind=pickup.GetString()??"";
                    bool maximum=kind=="fire"&&F(p,"fire")>=(p.TryGetProperty("maxFire",out var mf)?mf.GetInt32():5)||kind=="bomb"&&F(p,"bombs")>=(p.TryGetProperty("maxBombs",out var mb)?mb.GetInt32():5);
                    PowerupMessage(node.Position,kind,maximum);_audio.Pickup();
                }
                _pickups[id]=value;
            }
        }
        // A held player lies across the carrier's shoulders, so it turns with them.
        foreach(var (victim,carrier) in _holders)
            if(_facings.TryGetValue(carrier,out var carrierFacing))_facings[victim]=carrierFacing;
        foreach(var b in data.GetProperty("bombs").EnumerateArray())
        {
            string id="b:"+b.GetProperty("id").GetString();seen.Add(id);
            if(!_entities.TryGetValue(id,out var node))
            {
                node=new BombModel(_art);_dynamic.AddChild(node);_entities[id]=node;node.Position=PositionOf(b);if(!eventsAvailable)_audio.Bomb();
            }
            _targets[id]=PositionOf(b);
            string? carrier=b.TryGetProperty("carrierId",out var carriedBy)&&carriedBy.ValueKind==JsonValueKind.String?carriedBy.GetString():null;
            var model=(BombModel)node;
            if(model.UpdateState((int)F(b,"pumps"),(int)F(b,"level"),b.GetProperty("red").GetBoolean(),b.GetProperty("remote").GetBoolean(),F(b,"fuse"),carrier,(int)F(b,"state"),b.TryGetProperty("mode",out var bombMode)?bombMode.GetString()!:"normal",b.TryGetProperty("charge",out var charge)?charge.GetSingle():0))
            {
                if(!eventsAvailable)_audio.Pump(model.Charge);
                if(carrier!=null&&_entities.TryGetValue("p:"+carrier,out var player)&&player is BomberModel bomber)bomber.Pump(model.Charge);
            }
        }
        foreach(var h in data.GetProperty("hazards").EnumerateArray())
        {
            string id=h.GetProperty("id").GetString()!;
            if(_explosions.TryGetValue(id,out var active)&&GodotObject.IsInstanceValid(active))continue;
            float y=h.TryGetProperty("y",out var yValue)?yValue.GetSingle()*TempleStageView.HeightScale:_terrain.Surface(F(h,"x"),F(h,"z"));
            float radius=F(h,"r"),age=h.TryGetProperty("age",out var lifeAge)?lifeAge.GetSingle():0;
            bool red=h.TryGetProperty("red",out var redValue)&&redValue.GetBoolean();
            var blast=new ExplosionView(_art,radius,red,age,_explosions.Count>=16){Position=new(F(h,"x"),y+.3f,F(h,"z"))};
            _effects.AddChild(blast);_explosions[id]=blast;
            CountEffect("blast-"+blast.Tier);_shake=Mathf.Max(_shake,Mathf.Clamp(radius*.024f,.04f,.15f));
            if(!eventsAvailable)_audio.Blast(radius);
        }
        if(data.TryGetProperty("meteors",out var meteors))foreach(var meteor in meteors.EnumerateArray())
        {
            string id="m:"+meteor.GetProperty("id").GetString();seen.Add(id);
            if(!_entities.TryGetValue(id,out var node))
            {
                node=new MeteorView(_art);_dynamic.AddChild(node);_entities[id]=node;
            }
            node.Position=new(F(meteor,"x"),0,F(meteor,"z"));
            ((MeteorView)node).SetRemaining(F(meteor,"remaining"));
            node.SetProcess(_battleActive);
        }
        foreach(var item in data.GetProperty("items").EnumerateArray())
        {
            string id="i:"+item.GetProperty("id").GetString();seen.Add(id);
            if(!_entities.TryGetValue(id,out var node))
            {
                float x=F(item,"cx")+.5f,z=F(item,"cz")+.5f;node=new PowerupModel(_art,item.GetProperty("type").GetString()??"");
                float y=item.TryGetProperty("y",out var height)?height.GetSingle()*TempleStageView.HeightScale:_terrain.Surface(x,z);
                node.Position=new(x,y+.48f,z);_dynamic.AddChild(node);_entities[id]=node;
            }
            if(item.TryGetProperty("life",out var remaining)){float seconds=remaining.GetSingle();node.Visible=seconds<0||seconds>3||((int)(seconds*6)%2==0);}
        }
        foreach(string id in _entities.Keys)if(!seen.Contains(id))_staleEntities.Add(id);
        foreach(string id in _staleEntities)
        {
            _entities[id].QueueFree();_entities.Remove(id);_targets.Remove(id);_playerMotion.Remove(id);_facings.Remove(id);_ghostHosts.Remove(id);_holders.Remove(id);
        }
        _staleEntities.Clear();
        UpdateThrowPreviews(data,_softBlockBits);
        Present(_presentation.Read(data));
        if(data.TryGetProperty("blizzard",out var weather))_terrain.SetBlizzard(weather);
        UpdateClosure(data,(int)F(data,"closedLeft"),(int)F(data,"closedRight"));
    }
}
