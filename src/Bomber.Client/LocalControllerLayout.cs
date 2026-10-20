namespace Bomber.Client;

/// <summary>Two keyboard seats can be combined with six independent pads. For one or two players, pads also work as alternatives.</summary>
public static class LocalControllerLayout
{
    public static int Capacity(int connectedPads) => Math.Min(8, 2 + Math.Max(0, connectedPads));
    public static int? GamepadIndex(int seat, int humanCount, int connectedPads)
    {
        if (humanCount is < 1 or > 8 || seat < 0 || seat >= humanCount) throw new ArgumentOutOfRangeException(nameof(seat));
        int index = humanCount > 2 ? seat - 2 : seat;
        return index >= 0 && index < connectedPads ? index : null;
    }
}
