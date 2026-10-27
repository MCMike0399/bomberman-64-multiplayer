using System.Numerics;
using Bomber.Game.Simulation.Rules;
namespace Bomber.Client.Controls;

/// <summary>Twin-stick throw pointing: right-stick tilt picks the direction, deflection the distance.
/// A spring-loaded stick sweeps back through shorter deflections when it is let go; a fast inward
/// sweep keeps the last target, so releasing the stick never shortens the chosen throw.</summary>
public sealed class AimStickPointer
{
    public const float MinRange = 1f;
    /// <summary>Real sticks rarely report a full 1.0, especially on diagonals.</summary>
    public const float OuterDeflection = .92f;
    /// <summary>Deflection lost per second that is treated as a release rather than deliberate aim.</summary>
    public const float ReleaseSpeed = 6f;
    private float _previous;
    private Vector2? _held;

    /// <summary>Returns the landing offset in arena cells, or null at rest (the authority keeps the last one).</summary>
    public Vector2? Step(float x, float z, float dt)
    {
        var direction = InputPolicy.Direction(x, z, InputPolicy.StickDeadzone);
        float deflection = 0;
        if (direction != Vector2.Zero)
        {
            double length = Math.Sqrt((double)x * x + (double)z * z);
            deflection = (float)Math.Clamp((length - InputPolicy.StickDeadzone) / (OuterDeflection - InputPolicy.StickDeadzone), 0, 1);
        }
        bool releasing = float.IsFinite(dt) && dt > 0 && (_previous - deflection) / dt > ReleaseSpeed;
        _previous = deflection;
        if (direction == Vector2.Zero) { _held = null; return null; }
        if (releasing && _held is { } kept) return kept;
        _held = direction * (MinRange + deflection * (BombThrowRules.MaxAimRange - MinRange));
        return _held;
    }

    public void Reset() { _previous = 0; _held = null; }
}
