
namespace Bomber.Game.Matches;

/// <summary>Device-resolved intent. <paramref name="AimTarget"/> is an optional arena X/Z landing point from
/// a pointer; it steers a carried bomb without planting the carrier. The mouse sends an absolute point;
/// the right stick sends an offset from the carrier (<paramref name="AimRelative"/>) so latency cannot shift it.</summary>
public readonly record struct PlayerInput(float X, float Z, bool A = false, bool B = false, bool R = false, bool Aim = false, bool Stop = false,
    System.Numerics.Vector2? AimTarget = null, bool AimRelative = false);
