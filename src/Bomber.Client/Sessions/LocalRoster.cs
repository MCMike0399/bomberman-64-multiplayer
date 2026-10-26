using Bomber.Client.Controls;

namespace Bomber.Client.Sessions;

public enum RosterRole { Off, Human, Cpu }
public sealed record RosterSlot(RosterRole Role, int Appearance);

/// <summary>Editable UI positions are packed into authority slots with humans first, then CPU players.</summary>
public sealed class LocalRoster
{
    private readonly RosterSlot[] _slots=Enumerable.Range(0,8).Select(i=>new RosterSlot(i==0?RosterRole.Human:i<4?RosterRole.Cpu:RosterRole.Off,i)).ToArray();
    private readonly IReadOnlyList<RosterSlot> _view;
    public LocalRoster(){_view=Array.AsReadOnly(_slots);}
    public IReadOnlyList<RosterSlot> Slots=>_view;
    public int Humans=>_slots.Count(s=>s.Role==RosterRole.Human);
    public int CpuPlayers=>_slots.Count(s=>s.Role==RosterRole.Cpu);
    public int Participants=>Humans+CpuPlayers;
    public void Set(int slot,RosterRole role,int appearance)
    {
        if(slot is <0 or >7||appearance is <0 or >7||!Enum.IsDefined(role))throw new ArgumentOutOfRangeException(nameof(slot));
        _slots[slot]=new(role,appearance);
    }
    public int[] AuthorityAppearances()=>_slots.Where(s=>s.Role==RosterRole.Human).Concat(_slots.Where(s=>s.Role==RosterRole.Cpu)).Select(s=>s.Appearance).ToArray();
    public string? ValidationError(int connectedPads)
    {
        if(Humans<1)return "Choose at least one human player.";
        if(Participants<2)return "Add a CPU rival or another human for a battle.";
        if(Humans>LocalControllerLayout.Capacity(connectedPads))return $"Connect {Humans-2} gamepads for {Humans} humans, or change missing seats to CPU.";
        return null;
    }
    public string Binding(int slot,int connectedPads)
    {
        if(_slots[slot].Role==RosterRole.Off)return "EMPTY SEAT";
        if(_slots[slot].Role==RosterRole.Cpu)return "CPU CONTROLLED";
        int human=_slots.Take(slot).Count(s=>s.Role==RosterRole.Human);
        if(Humans>2&&human>=2)return human-2<connectedPads?$"GAMEPAD {human-1}":$"GAMEPAD {human-1} • CONNECT PAD";
        return human==0?"WASD • SPACE / SHIFT / R":"ARROWS • ENTER / CTRL";
    }
}
