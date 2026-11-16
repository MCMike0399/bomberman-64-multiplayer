using Godot;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildLostAtSea()
    {
        Name="LostAtSeaTerrain";
        BuildGlassBuoy();
    }

    private Node3D CreateSeaCargo(Node3D root,int cell)
    {
        root.Name="ObservationCargo"+cell;
        var wood=_art.Toy(new("cfae42"),.85f);var ends=_art.Toy(new("e9cb65"),.82f);
        var binding=_art.Toy(new("566879"),.7f);
        float height=Layout.SoftBlockHeight*HeightScale;
        Box(root,new(0,height/2,0),new(.82f,height,.82f),wood,.045f);
        foreach(float z in new[]{-.425f,.425f})
        {
            foreach(float sign in new[]{-1f,1f})
            {
                var brace=Box(root,new(0,height/2,z),new(.09f,height*1.1f,.035f),ends,.01f);
                brace.Rotation=new(0,0,sign*.72f);
            }
            foreach(float y in new[]{.075f,height-.075f})Box(root,new(0,y,z),new(.87f,.075f,.055f),binding,.01f);
        }
        Box(root,new(0,height+.015f,0),new(.88f,.03f,.88f),ends,.015f);
        return root;
    }
}
