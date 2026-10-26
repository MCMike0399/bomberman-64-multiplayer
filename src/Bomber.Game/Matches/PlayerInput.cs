
namespace Bomber.Game.Matches;

public readonly record struct PlayerInput(float X, float Z, bool A = false, bool B = false, bool R = false, bool Aim = false, bool Stop = false);
