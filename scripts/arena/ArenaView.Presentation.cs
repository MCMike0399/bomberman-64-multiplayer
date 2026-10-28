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

/// <summary>Timed presentation events: audio cues, effects, reactions and floating labels.</summary>
public partial class ArenaView
{
    private void CountEffect(string key)=>_effectCounts[key]=_effectCounts.GetValueOrDefault(key)+1;

    private void Present(IReadOnlyList<JsonElement> events)
    {
        foreach(var e in events)
        {
            string kind=e.GetProperty("kind").GetString()!,reason=e.GetProperty("reason").GetString()??"";
            string? player=e.GetProperty("playerId").GetString();
            PlayerFeedback?.Invoke(kind,player);
            if(player!=null&&_entities.TryGetValue("p:"+player,out var actor)&&actor is BomberModel model)model.React(kind);
            switch(kind)
            {
                case "bomb-blocked":PowerupMessage(PositionOf(e),reason=="capacity"?"BOMBS OUT!":"NEED ROOM!",false);CountEffect(kind);break;
                case "bomb-drop":case "bomb-carry":_audio.Bomb();break;
                case "kick-windup":CountEffect(kind);break;
                case "kick":
                    _audio.Kick();CountEffect("kick");
                    var heading=player!=null&&_entities.TryGetValue("p:"+player,out var kicker)?kicker.Basis.Z:Vector3.Forward;
                    _effects.AddChild(new KickBurst(_art,heading){Position=PositionOf(e)+Vector3.Up*.25f});break;
                case "remote-fuse":PowerupMessage(PositionOf(e),"FUSE LIT!",false);CountEffect(kind);break;
                case "lift":_audio.Lift();break;
                case "player-grab":_audio.Lift();CountEffect(kind);break;
                case "player-throw":_audio.Throw();CountEffect(kind);break;
                case "player-release":case "recover":_audio.Pickup();CountEffect(kind);break;
                case "player-land":_audio.Kick();break;
                case "ghost-latch":_audio.Lift();CountEffect(kind);break;
                case "ghost-detach":_audio.Throw();CountEffect(kind);break;
                case "knockdown":
                    _audio.Kick();CountEffect(kind);
                    PowerupMessage(PositionOf(e),reason=="rolling-bomb"?"KICK IMPACT!":"FALLING BOMB HIT!",false);break;
                case "pump":_audio.Pump((int)F(e,"magnitude"));break;
                case "throw":_audio.Throw(F(e,"magnitude")/6);break;
                case "blast":_audio.Blast(F(e,"radius"));break;
                case "death":
                    bool water=reason is "water" or "fall";
                    if(water)_audio.Splash();else _audio.Death();
                    var pos=PositionOf(e);
                    if(water)
                    {
                        // Death is an immediate state transition. Finish the buffered fall
                        // at its accepted contact point, but never rewind a practice respawn.
                        if(player!=null&&_entities.TryGetValue("p:"+player,out var exiting)&&
                            _targets.TryGetValue("p:"+player,out var latest)&&latest.DistanceTo(pos)<.3f)
                        {exiting.Position=latest;_playerMotion["p:"+player]=new SnapshotMotion(latest,_snapshotTick);}
                        pos.Y=WaterHeight;
                    }
                    if(water)_effects.AddChild(new ExitEffect(_art,true){Position=pos+Vector3.Up*.12f});
                    else
                    {
                        // Blasts leave a charred, flailing stand-in; the real actor is already gone or ghosted.
                        var look=player!=null&&_lastLooks.TryGetValue("p:"+player,out var last)?last:(Suit:Colors[0],Facing:0f);
                        _effects.AddChild(new BurnoutEffect(_art,look.Suit,look.Facing){Position=pos});
                    }
                    CountEffect(water?"splash":"death");break;
                case "round-ready":_audio.RoundReady();break;
                case "fight":_audio.Fight();break;
                case "hurry":_audio.Hurry();break;
                case "waterfall":_audio.Splash();CountEffect("water-rise");break;
                case "round-over":
                    if(player is null)_audio.RoundDraw();else _audio.RoundWin();
                    if(player!=null&&_entities.TryGetValue("p:"+player,out var winner)&&winner is BomberModel bomber)bomber.React("victory");break;
            }
        }
    }
    private void PowerupMessage(Vector3 position,string kind,bool maximum=false)
    {
        string text=kind switch {"fire"=>maximum?"MAX BLAST!":"BLAST UP!","bomb"=>maximum?"MAX BOMBS!":"BOMB UP!","remote"=>"REMOTE!","red"=>"POWER BOMB!","heart"=>"HEART!","bounce"=>"BOUNCE BOMBS!","focus"=>"QUICK CHARGE!","tiny"=>"TINY + QUICK!","big"=>"BIG + SLOW!","haste"=>"HASTE!","slow"=>"SLOWED!",_=>kind};
        var label=new Label3D {Text=text,Position=position+new Vector3(0,2.2f,0),FontSize=102,PixelSize=.004f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest=true,OutlineSize=30,Modulate=new Color("ffe571"),OutlineModulate=new Color("233657")};
        _effects.AddChild(label);var tween=label.CreateTween().SetParallel();tween.TweenProperty(label,"position",label.Position+Vector3.Up*.9f,1.2);tween.TweenProperty(label,"modulate:a",0f,1.2);tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }
    private void Debris(Vector3 position)
    {
        var root=new Node3D {Position=position};_effects.AddChild(root);
        var material=_art.Color(new("d5b472"));
        for(int i=0;i<6;i++)
        {
            float angle=i*Mathf.Tau/6;
            var piece=_stage==BlastStages.BlizzardBattle?_terrain!.CreateIceShard(root,new(0,.4f,0),i):
                Box(root,new(0,.4f,0),Vector3.One*(.12f+i*.008f),material,.02f);
            var tween=piece.CreateTween().SetParallel();tween.TweenProperty(piece,"position",new Vector3(Mathf.Cos(angle)*.65f,.06f,Mathf.Sin(angle)*.65f),.35);tween.TweenProperty(piece,"rotation",new Vector3(i,2,i*.4f),.35);
        }
        var cleanup=root.CreateTween();cleanup.TweenInterval(.4);cleanup.TweenProperty(root,"scale",Vector3.Zero,.2);cleanup.TweenCallback(Callable.From(root.QueueFree));
    }
}
