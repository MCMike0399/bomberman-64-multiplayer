namespace Bomber.Game;

/// <summary>One seat-based fallback for unnamed guests, local players, HUD and results.</summary>
public static class PlayerNames
{
    public const string UnnamedGuest = "Player";
    public static string Display(string? name,int slot)
    {
        var value=name?.Trim();
        return string.IsNullOrEmpty(value)||value==UnnamedGuest||value==$"Player {slot+1}"?$"P{slot+1}":value;
    }
}
