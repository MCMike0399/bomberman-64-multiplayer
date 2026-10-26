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
        byte[] bits=Convert.FromBase64String(data.GetProperty("softBlocks").GetString()!);
        for(int i=0;i<_terrain.Width*_terrain.Depth;i++)
        {
            bool exists=i/8<bits.Length&&(bits[i/8]&(1<<(i%8)))!=0;
            if(exists&&!_blocks.ContainsKey(i)){var block=_terrain.CreateCover(i);_dynamic.AddChild(block);_blocks[i]=block;}
            if(!exists&&_blocks.Remove(i,out var old)){Debris(old.Position);old.QueueFree();}
        }
        var seen=new HashSet<string>();
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
            float angle=Mathf.Atan2(F(p,"fx"),F(p,"fz"));_facings[id]=angle;
            if(p.TryGetProperty("latchedTo",out var latch)&&latch.ValueKind==JsonValueKind.String)_ghostHosts[id]="p:"+latch.GetString();
            else _ghostHosts.Remove(id);
            ((BomberModel)node).Pose(p.GetProperty("carrying").ValueKind!=JsonValueKind.Null,p.GetProperty("stunned").GetBoolean(),ghost);
            bool falling=p.TryGetProperty("falling",out var fall)&&fall.GetBoolean();
            ((BomberModel)node).RecoveryPose(p.TryGetProperty("carriedBy",out var heldBy)&&heldBy.ValueKind==JsonValueKind.String,!falling&&p.TryGetProperty("airborne",out var air)&&air.GetBoolean());
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
        foreach(string id in _entities.Keys.Where(k=>!seen.Contains(k)).ToArray())
        {
            _entities[id].QueueFree();_entities.Remove(id);_targets.Remove(id);_playerMotion.Remove(id);_facings.Remove(id);_ghostHosts.Remove(id);
        }
        UpdateThrowPreviews(data,bits);
        Present(_presentation.Read(data));
        if(data.TryGetProperty("blizzard",out var weather))_terrain.SetBlizzard(weather);
        int left=(int)F(data,"closedLeft"),right=(int)F(data,"closedRight");
        _terrain.SetGreedyCompression(left,right);
        for(int x=0;_stage!=BlastStages.GreedyTrap&&x<_terrain.Width;x++)
        {
            bool closed=x<left||x>=_terrain.Width-right;
            if(closed&&!_walls.ContainsKey(x))
            {
                var n=_stage==BlastStages.BlizzardBattle?_terrain.CreateAvalanche(x):new Node3D();
                if(_stage==BlastStages.TopRules)
                {
                    var stone=_terrain.CastleWallMaterial;
                    float wallHeight=(_terrain.Layout.At(0,_terrain.Depth/2).ObstacleHeight??3.9f)*TempleStageView.HeightScale;
                    Box(n,new(x+.5f,wallHeight/2,_terrain.Depth/2f),new(1,wallHeight,_terrain.Depth-2),stone);
                    Box(n,new(x+.5f,wallHeight+.05f,_terrain.Depth/2f),new(1.03f,.16f,_terrain.Depth-2),_art.Color(new Color("bdc4c5")),.025f);
                }
                else if(_stage!=BlastStages.BlizzardBattle)Box(n,new(x+.5f,2.1f,_terrain.Depth/2f),new(.92f,5.8f,_terrain.Depth),_art.Color(new Color(.92f,.18f,.17f,.4f),.7f,true),.03f);
                _effects.AddChild(n);_walls[x]=n;
            }
            if(!closed&&_walls.Remove(x,out var wall))wall.QueueFree();
        }
    }
}
