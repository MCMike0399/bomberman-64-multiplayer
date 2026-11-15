using Godot;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Rendering;

namespace BlastCircuit.Arena;

public partial class ArenaView
{
    /// <summary>Rock the ship and all its actors/effects in one presentation space.
    /// The sea, sky, camera and HUD remain still; authority never sees this basis.</summary>
    private void AdvanceShipMotion(float delta)
    {
        if(_stage!=BlastStages.SeaSick||_terrain==null||!_battleActive)return;
        _shipClock+=delta;
        var basis=PresentationMath.Rotation(new(PresentationMath.Sin(_shipClock*.55f)*ShipRollAmplitude,0,
            PresentationMath.Sin(_shipClock*.43f)*.0035f));
        var pivot=new Vector3(_terrain.Width/2f,0,_terrain.Depth/2f);
        _stageAssembly.Transform=new(basis,pivot-basis*pivot);
    }
}
