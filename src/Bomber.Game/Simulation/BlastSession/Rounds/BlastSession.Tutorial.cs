// Product-owned tutorial orchestration. Lessons use the ordinary simulation and input path.
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.State;

namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    private BlastPlayer? Trainee => _players.Values.FirstOrDefault(p => p.Slot == 0);

    private void PrepareTutorial()
    {
        if (_tutorialLesson is null || Trainee is not { } player) return;
        _tutorialPlantedBomb=null;
        _tutorialSignals.Clear(); _tutorialStep=0; _tutorialEfforts=0; _tutorialTime=0;
        _tutorialAimTime=0; _tutorialFailed=false; _tutorialComplete=false; _tutorialStarted=false;
        _tutorialFeedback=""; _tutorialPickupSerial=player.PickupSerial;
        _items.Clear(); _bombs.Clear(); _hazards.Clear();
        _softBlocks=new SoftBlockGrid(_stage.Width,_stage.Height);
        player.X=8.5f; player.Z=12.5f; player.SurfaceY=0; player.Tier=0;
        player.FacingX=1; player.FacingZ=0;
        string id=_tutorialLesson.Id;
        if (id is "cover" or "red")
            foreach(var cell in new[]{(12,12),(13,12),(12,13)}) _softBlocks.Set(cell.Item1,cell.Item2,true);
        string[] items=id switch
        {
            "chain"=>["bomb"], "swap"=>["remote","heart","red","focus"], "expiry"=>["tiny"], "duel"=>["heart","remote"], "items"=>["fire","bomb"], "effects"=>["tiny","big","haste","slow"],
            "remote" or "red" or "bounce" or "heart" or "focus"=>[id], _=>[]
        };
        _tutorialItems=items;
        for(int i=0;i<items.Length;i++)
            _items.Add(new BlastItem {Id=NextItemId(),CellX=id=="chain"?8:id=="duel"?9-i*2:9+i*2,CellZ=12,Tier=0,Type=items[i],LifeRemaining=60});
        if(id is "impact" or "grapple" or "ghost" or "escape" or "shake" or "duel")
        {
            var partner=new BlastPlayer {Id="tutorial-partner",Slot=1,Appearance=2,DisplayName="Training partner",
                X=12.5f,Z=12.5f,SurfaceY=0,Tier=0,Alive=true,IsBot=id=="duel",FacingX=-1,FacingZ=0};
            PowerUpRules.Reset(partner,_advanced); _players[partner.Id]=partner;
            if(id=="grapple") {partner.X=10;KnockDown(partner,4.2f,"training",null,null);}
            if(id=="escape") { partner.X=player.X; partner.Z=player.Z; BeginHold(partner,player); }
            if(id=="shake") {partner.Ghost=true;partner.X=player.X;partner.Z=player.Z;TryLatch(partner);}
        }
        if(id=="recovery") KnockDown(player,4.2f,"training",null,null);
    }

    private void BeginTutorialFight()
    {
        if(_tutorialLesson is null)return;
        _tutorialStarted=true;
        if(_tutorialLesson.Id=="flood") { _phase=RoundPhase.Hurry; Present("hurry"); }
    }

    private void TickTutorial(float dt)
    {
        if(_tutorialLesson is null || !_tutorialStarted || _tutorialComplete || _tutorialFailed || Trainee is not { } p)return;
        _tutorialTime+=dt;
        if(_tutorialLesson.Id=="flood") _waterLevel=FloodRules.InitialLevel+_tutorialTime*FloodRules.RisePerSecond;
        if(p.PickupSerial!=_tutorialPickupSerial)
        { _tutorialPickupSerial=p.PickupSerial; _tutorialSignals.Add("item:"+p.LastPickup); }
        // Steering counts from the aim stance with movement keys or from a pointer (mouse / right stick).
        if(p.AimSet && (p.Aiming && p.Input.X*p.Input.X+p.Input.Z*p.Input.Z>.01f || p.Input.AimTarget is not null && p.CarryingBombId is not null))
            _tutorialAimTime+=dt;
        if(p.CarryingBombId is { } carried)
        {
            _tutorialSignals.Add("carry");
            if(p.CarryFromPrimary)_tutorialSignals.Add("primary-hold");
            var bomb=_bombs.FirstOrDefault(b=>b.Id==carried);
            if(bomb?.Pumps>=3)_tutorialSignals.Add("pump3");
        }
        bool Has(string signal)=>_tutorialSignals.Contains(signal);
        bool cleared=!_softBlocks.IsAlive(12,12)||!_softBlocks.IsAlive(13,12)||!_softBlocks.IsAlive(12,13);
        bool safe=_hazards.Count==0 && p.Alive && !p.Ghost && !_tutorialFailed;
        bool Satisfied(int step)=>_tutorialLesson.Id switch
        {
            "move"=>Dist2(p.X,p.Z,11.5f,12.5f)<.65f*.65f,
            "bomb"=>step==0?Has("bomb-drop"):Has("planted-blast")&&safe,
            "carry"=>step==0?Has("carry"):Has("throw"),
            "primary-hold"=>step==0?Has("primary-hold"):Has("throw"),
            "pump"=>step==0?Has("pump3"):Has("charged-throw"),
            "aim"=>step==0?_tutorialAimTime>=.3f:Has("aimed-throw"),
            "kick"=>step==0?Has("kick"):Has("kick-stop"),
            "chain"=>step==0?Has("chain"):Has("chain")&&safe,
            "swap"=>step==0?Has("item:remote")&&Has("item:heart"):p.BombType=="red"&&p.Ability=="focus",
            "expiry"=>step==0?Has("item:tiny"):p.StatusEffect=="none",
            "cover"=>step==0?cleared:Has("blast")&&safe,
            "items"=>Has(step==0?"item:fire":"item:bomb"),
            "remote"=>step==0?Has("item:remote"):Has("remote-command")&&safe,
            "red"=>step==0?Has("item:red"):Has("red-cover")&&safe,
            "bounce"=>step==0?Has("item:bounce"):Has("charged-bounce"),
            "heart"=>step==0?Has("item:heart"):Has("heart-hit"),
            "focus"=>step==0?Has("item:focus"):Has("focus-charge"),
            "effects"=>Has("item:"+new[]{"tiny","big","haste","slow"}[step]),
            "terrain"=>step==0?p.Y>=1.95f: p.Y<.1f&&!p.Airborne,
            "recovery"=>step==0?_tutorialEfforts>=3:Has("recover"),
            "impact"=>Has("partner-down"),
            "grapple"=>step==0?Has("player-grab"):Has("player-throw"),
            "escape"=>step==0?_tutorialEfforts>=3:Has("escaped"),
            "ghost"=>step==0?p.Ghost:Has("ghost-latch"),
            "shake"=>Has("ghost-shaken"),
            "flood"=>step==0?p.Y>=1.95f:p.Y>=1.95f&&_waterLevel>=.25f&&safe,
            "duel"=>Has("partner-defeated")&&safe,
            _=>false
        };
        while(_tutorialStep<_tutorialLesson.Objectives.Length && Satisfied(_tutorialStep))_tutorialStep++;
        if(_tutorialStep==_tutorialLesson.Objectives.Length)
        { _tutorialComplete=true; _tutorialFeedback="Nice work! Replay this lesson or try the next one."; }
        else if(_tutorialLesson.Id is "recovery" or "escape" && p.StunTimer<=0 && p.CarriedById is null && _tutorialEfforts<3)
            FailTutorial("Try again: make three fresh presses or direction changes before recovery finishes.");
        else if(_tutorialLesson.Id=="shake" && _players["tutorial-partner"].LatchedToId is null && !Has("ghost-shaken"))
            FailTutorial("The latch expired. Retry and reverse direction six times before four seconds pass.");
        else if(_tutorialLesson.Id=="grapple" && _tutorialStep==0 && _players["tutorial-partner"].StunTimer<=0)
            FailTutorial("The partner recovered. Retry, move beside them, then hold carry before they get up.");
        else if(_tutorialLesson.Id!="duel" && _tutorialItems.Any(type=>!Has("item:"+type)&&!_items.Any(item=>item.Type==type)))
            FailTutorial("A required training pickup expired. Retry for a fresh set.");
    }

    private void FailTutorial(string message)
    {if(_tutorialLesson is null)return;_tutorialFailed=true;_tutorialFeedback=message;}

    private void TutorialEvent(string kind,string? entityId,string? playerId,string? causedBy,string? reason)
    {
        if(_tutorialLesson is null || !_tutorialStarted || _tutorialFailed || _tutorialComplete || Trainee is not { } p)return;
        if(playerId==p.Id)_tutorialSignals.Add(kind);
        if(playerId==p.Id && kind=="bomb-drop")_tutorialPlantedBomb??=entityId;
        if(playerId==p.Id && kind=="blast" && entityId==_tutorialPlantedBomb)_tutorialSignals.Add("planted-blast");
        if(playerId==p.Id && kind=="pump" && p.Ability=="focus")_tutorialSignals.Add("focus-charge");
        if(playerId==p.Id && kind=="blast")
        {
            _tutorialBlastsAtTick=_tutorialBlastTick==_tickCount?_tutorialBlastsAtTick+1:1;
            _tutorialBlastTick=_tickCount;
            if(_tutorialBlastsAtTick>=2)_tutorialSignals.Add("chain");
        }
        var bomb=entityId is null?null:_bombs.FirstOrDefault(b=>b.Id==entityId);
        if(playerId==p.Id && bomb is not null)
        {
            if(kind=="throw" && bomb.Pumps>=3)_tutorialSignals.Add("charged-throw");
            if(kind=="throw" && _tutorialAimTime>=.3f)_tutorialSignals.Add("aimed-throw");
            if(kind=="bounce" && bomb.Pumps>0)_tutorialSignals.Add("charged-bounce");
        }
        if(kind=="knockdown" && playerId=="tutorial-partner" && causedBy==p.Id)_tutorialSignals.Add("partner-down");
        if(kind=="player-release" && playerId==p.Id && reason=="escaped")_tutorialSignals.Add("escaped");
        if(kind=="ghost-latch" && entityId==p.Id)_tutorialSignals.Add("ghost-latch");
        if(kind=="ghost-detach" && playerId==p.Id && reason=="shaken-off")_tutorialSignals.Add("ghost-shaken");
        if(kind=="death" && playerId=="tutorial-partner" && causedBy==p.Id)_tutorialSignals.Add("partner-defeated");
        if(kind=="death" && playerId=="tutorial-partner" && _tutorialLesson.Id is "grapple" or "ghost" or "impact")
            FailTutorial("The partner was eliminated. Retry to practice the impact or hold before the explosion.");
        if(kind=="death" && playerId==p.Id && _tutorialLesson.Id!="ghost")
            FailTutorial("You were caught! Retry with a fresh arena and leave yourself an escape route.");
    }

    private object? TutorialSnapshot()
    {
        if(_tutorialLesson is null)return null;
        string id=_tutorialLesson.Id;
        float? targetX=null,targetZ=null,targetY=null;
        if(id=="move") {targetX=11.5f;targetZ=12.5f;targetY=0;}
        else if(id is "terrain" or "flood")
        {
            bool down=id=="terrain"&&_tutorialStep>0;
            targetX=11.5f;targetZ=down?9.5f:1.5f;targetY=down?0:2;
        }
        else if(id is "impact" or "grapple" or "ghost") {var partner=_players.GetValueOrDefault("tutorial-partner");targetX=partner?.X;targetZ=partner?.Z;targetY=partner?.Y;}
        return new {id,step=_tutorialStep,total=_tutorialLesson.Objectives.Length,complete=_tutorialComplete,failed=_tutorialFailed,
            feedback=_tutorialFeedback,targetX,targetZ,targetY};
    }
}
