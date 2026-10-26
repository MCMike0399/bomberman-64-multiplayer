// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using Bomber.Game.Simulation.Rules;

namespace Bomber.Game.Simulation.State;

/// <summary>One player in the simulation.</summary>
public sealed class BlastPlayer
{
    public string Id { get; init; } = "";
    public int Slot { get; init; }
    public int Appearance { get; set; }
    public bool IsBot { get; init; }
    public string DisplayName { get; init; } = "";

    // Per-tick body contact bookkeeping; not replicated presentation state.
    internal float BodyStartX,BodyStartZ,BodyYield;

    // Position and orientation.
    public float X { get; set; }
    public float Z { get; set; }
    public int Tier { get; set; }
    public float FacingX { get; set; } = 0;
    public float FacingZ { get; set; } = 1;

    // Vital state.
    public bool Alive { get; set; } = true;
    public bool Ghost { get; set; }

    // Timed state.
    public float StunTimer { get; set; }       // seconds remaining stunned
    public float InvincibleTimer { get; set; } // damage protection from a heart or respawn
    public float ImpactProtectionTimer { get; set; } // brief bomb-contact cooldown; never blocks blasts
    public string? KnockdownReason { get; set; }
    public string? CarriedById { get; set; }
    public float RecoveryTotal { get; set; }
    public float HoldRemaining { get; set; }
    public float StruggleCooldown { get; set; }
    public float RecoveryX { get; set; }
    public float RecoveryZ { get; set; }
    public bool Airborne { get; set; }
    public bool Falling { get; set; }
    public float AirVX { get; set; }
    public float AirVZ { get; set; }
    public float AirVY { get; set; }

    // A contact kick commits a direction for its brief planted anticipation.
    public string? KickBombId { get; set; }
    public float KickWindupRemaining { get; set; }
    public float KickRecoveryRemaining { get; set; }
    public float KickDirectionX { get; set; }
    public float KickDirectionZ { get; set; }

    // Carrying.
    public bool CarryFromPrimary { get; set; }
    public string? PrimaryBombCandidateId { get; set; }
    public float PrimaryHoldTimer { get; set; }
    public string? CarryingBombId { get; set; }   // bomb id if carrying a bomb
    public string? CarryingPlayerId { get; set; } // player id if carrying a stunned player

    // Deliberate throwing intent belongs to the carrier, never the client or target.
    public bool Aiming { get; set; }
    public bool AimSet { get; set; }
    public float AimX { get; set; }
    public float AimZ { get; set; } = 1;
    public float AimRange { get; set; } = BombThrowRules.DefaultAimRange;
    internal float AimCursorXAge,AimCursorZAge;
    internal int AimCursorXSign,AimCursorZSign;

    public int MaxFire {get;set;}=5;
    public int MaxBombs {get;set;}=5;
    public float EffectDuration {get;set;}=10;
    public string StatusEffect {get;set;}="none";
    public float StatusRemaining {get;set;}
    public float StatusSpeed=>StatusEffect switch {"tiny" or "haste"=>1.5f,"big" or "slow"=>.65f,_=>1};
    public float StatusScale=>StatusEffect switch {"tiny"=>.65f,"big"=>1.4f,_=>1};

    // Power-ups.
    public int BombCapacity { get; set; } = PowerUpRules.StartingBombs;  // max active bombs
    public int FireLevel { get; set; } = 1;      // 1–5 normal, pumping raises effective level
    public string BombType { get; set; } = "normal";
    public string Ability { get; set; } = "none";
    public bool HasRemote { get=>BombType=="remote";set{if(value)BombType="remote";else if(HasRemote)BombType="normal";} }
    public bool HasRed { get=>BombType=="red";set{if(value)BombType="red";else if(HasRed)BombType="normal";} }
    public bool HasFocus => Ability=="focus";
    public long PickupSerial { get; set; }
    public string? LastPickup { get; set; }
    public long PumpSerial { get; set; }
    public bool HasHeart { get=>Ability=="heart";set{if(value)Ability="heart";else if(HasHeart)Ability="none";} }

    // Score.
    public int Wins { get; set; }

    // Ghost mechanics (latching onto a living player).
    public string? LatchedToId { get; set; }
    public float LatchTimer { get; set; }
    public int LatchReverseCount { get; set; }
    public float LatchReverseCooldown { get; set; }
    public float LatchPrevDX { get; set; }
    public float LatchPrevDZ { get; set; }

    // Input tracking.
    public InputState Input { get; set; }
    private readonly Queue<(bool A,bool B,bool R,bool Aim,bool Stop)> _pendingButtons = [];
    private (bool A,bool B,bool R,bool Aim,bool Stop) _receivedButtons;
    public void ReceiveInput(InputState input)
    {
        var buttons=(input.A,input.B,input.R,input.Aim,input.Stop);
        if(buttons!=_receivedButtons)
        {
            if(_pendingButtons.Count==16)_pendingButtons.Dequeue();
            _pendingButtons.Enqueue(buttons);
        }
        _receivedButtons=buttons;Input=input;
    }
    public void AdvanceReceivedInput()
    {
        // TCP may deliver a press and release in one lobby step. Keep their order,
        // consume at most one transition per tick, and always use the newest axes.
        if(_pendingButtons.TryDequeue(out var buttons))
            Input=Input with {A=buttons.A,B=buttons.B,R=buttons.R,Aim=buttons.Aim,Stop=buttons.Stop};
    }
    public void ClearInput(){Input=default;_receivedButtons=default;_pendingButtons.Clear();}
    public bool PrevA { get; set; }
    public bool PrevB { get; set; }
    public bool PrevR { get; set; }
    public bool PrevStop { get; set; }

    // Bot AI state.
    public float BotThinkTimer { get; set; }
    internal int BotEscapeNode { get; set; } = -1;
    public bool BotPlacingBomb { get; set; }
    public bool BotBombTapPending { get; set; }

    // Spectator state: joined in progress, waits for next round.
    public bool PendingRoundStart { get; set; }

    public int ActiveBombs { get; set; } // count of live (non-resting-picked-up) bombs

    public float SurfaceY { get; set; }
    public float Y => SurfaceY;  // continuous authoritative surface elevation
}
