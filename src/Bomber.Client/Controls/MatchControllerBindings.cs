namespace Bomber.Client.Controls;

public sealed record GamepadDevice(int RuntimeId,string Guid,string Name);
public sealed record ControllerSeatBinding(int Seat,bool EligibleForGamepad,int? RuntimeId,GamepadDevice? LastKnownDevice,bool Ambiguous)
{
    public bool IsConnected=>RuntimeId.HasValue;
}

/// <summary>Reservations last for the match. Enumeration changes never move a connected player's pad.</summary>
public sealed class MatchControllerBindings
{
    private readonly int _humans;
    private readonly GamepadDevice?[] _last;
    private readonly int?[] _ids;
    private readonly bool[] _ambiguous;
    private GamepadDevice[] _devices=[];
    public IReadOnlyList<ControllerSeatBinding> Seats {get;private set;}=[];
    public MatchControllerBindings(int humanCount,IReadOnlyList<GamepadDevice> devices)
    {
        if(humanCount is <1 or >8)throw new ArgumentOutOfRangeException(nameof(humanCount));
        _humans=humanCount;_last=new GamepadDevice?[humanCount];_ids=new int?[humanCount];_ambiguous=new bool[humanCount];
        _devices=Validate(devices);
        for(int seat=0;seat<_humans;seat++)
            if(LocalControllerLayout.GamepadIndex(seat,humanCount,_devices.Length) is {} index)Assign(seat,_devices[index]);
        Snapshot();
    }
    private bool Eligible(int seat)=>_humans<=2||seat>=2;
    public int? DeviceIdForSeat(int seat)
    {
        if(seat<0||seat>=_humans)throw new ArgumentOutOfRangeException(nameof(seat));
        return _ids[seat];
    }
    public void Refresh(IReadOnlyList<GamepadDevice> devices)
    {
        _devices=Validate(devices);Array.Clear(_ambiguous);
        var available=_devices.ToDictionary(d=>d.RuntimeId);
        var claimed=new HashSet<int>();
        for(int seat=0;seat<_humans;seat++)
        {
            if(_ids[seat] is {} id&&available.TryGetValue(id,out var current)&&SameIdentity(current,_last[seat]!))
            {Assign(seat,current);claimed.Add(id);}
            else _ids[seat]=null;
        }
        for(int seat=0;seat<_humans;seat++)
        {
            if(!Eligible(seat)||_ids[seat]!=null||_last[seat] is not {} old)continue;
            var candidates=_devices.Where(d=>!claimed.Contains(d.RuntimeId)&&SameIdentity(d,old)).ToArray();
            if(string.IsNullOrWhiteSpace(old.Guid)&&string.IsNullOrWhiteSpace(old.Name))
            {_ambiguous[seat]=candidates.Length>0;continue;}
            int waiting=Enumerable.Range(0,_humans).Count(s=>_ids[s]==null&&_last[s] is {} other&&SameIdentity(other,old));
            if(candidates.Length==1&&waiting==1){Assign(seat,candidates[0]);claimed.Add(candidates[0].RuntimeId);}
            else _ambiguous[seat]=candidates.Length>0;
        }
        for(int seat=0;seat<_humans;seat++)
        {
            if(!Eligible(seat)||_last[seat]!=null)continue;
            var fresh=_devices.FirstOrDefault(d=>!claimed.Contains(d.RuntimeId)&&
                !Enumerable.Range(0,_humans).Any(s=>_ids[s]==null&&_last[s] is {} reserved&&SameIdentity(d,reserved)));
            if(fresh!=null){Assign(seat,fresh);claimed.Add(fresh.RuntimeId);}
        }
        Snapshot();
    }
    public bool BindDeviceToSeat(int seat,int runtimeId)
    {
        if(seat<0||seat>=_humans||!Eligible(seat))return false;
        if(Enumerable.Range(0,_humans).Any(s=>s!=seat&&_ids[s]==runtimeId))return false;
        var device=_devices.FirstOrDefault(d=>d.RuntimeId==runtimeId);
        if(device==null)return false;
        Assign(seat,device);Snapshot();return true;
    }
    private void Assign(int seat,GamepadDevice device){_ids[seat]=device.RuntimeId;_last[seat]=device;_ambiguous[seat]=false;}
    private void Snapshot()=>Seats=Array.AsReadOnly(Enumerable.Range(0,_humans)
        .Select(s=>new ControllerSeatBinding(s,Eligible(s),_ids[s],_last[s],_ambiguous[s])).ToArray());
    private static bool SameIdentity(GamepadDevice a,GamepadDevice b)=>
        string.Equals(a.Guid.Trim(),b.Guid.Trim(),StringComparison.OrdinalIgnoreCase)&&
        string.Equals(a.Name.Trim(),b.Name.Trim(),StringComparison.OrdinalIgnoreCase);
    private static GamepadDevice[] Validate(IReadOnlyList<GamepadDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if(devices.Any(d=>d==null||d.RuntimeId<0||d.Guid==null||d.Name==null)||devices.Select(d=>d.RuntimeId).Distinct().Count()!=devices.Count)
            throw new ArgumentException("Gamepads require unique nonnegative runtime IDs and non-null identity fields.",nameof(devices));
        return devices.ToArray();
    }
}
