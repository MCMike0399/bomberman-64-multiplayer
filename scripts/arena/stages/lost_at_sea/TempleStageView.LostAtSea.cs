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
        _seaGlass=_art.ObservationGlass();
        var ivory=_art.Toy(new("d8e2dc"),.65f);
        var frame=_art.Toy(new("6d8e9b"),.6f);
        var brass=_art.Toy(new("bca16d"),.55f);
        var dark=_art.Toy(new("354b60"),.8f);
        // The lower ocean bed remains visible through the viewing floor. Only
        // authority cells support actors; all marine scenery is below/outside it.
        Box(this,new(Width/2f,-3.4f,Depth/2f),new(160,.16f,160),_art.Color(new("286477"),.95f)).Name="ObservationOceanBed";
        var glass=new SurfaceTool();glass.Begin(Mesh.PrimitiveType.Triangles);
        foreach(var point in new[]{new Vector3(3,0,3),new Vector3(Width-3,0,Depth-3),new Vector3(Width-3,0,3),new Vector3(3,0,3),new Vector3(3,0,Depth-3),new Vector3(Width-3,0,Depth-3)})
        {glass.SetNormal(Vector3.Up);glass.SetUV(new(point.X/Width,point.Z/Depth));glass.AddVertex(point);}
        glass.Index();AddChild(new MeshInstance3D{Name="SlipperyVioletGlass",Mesh=glass.Commit(),MaterialOverride=_seaGlass});
        for(int z=0;z<Depth;z++)for(int x=0;x<Width;x++)
        {
            var cell=Layout.At(x,z);
            bool boundary=x==0||z==0||x==Width-1||z==Depth-1;
            if(boundary)
            {
                Box(this,new(x+.5f,-.22f,z+.5f),new(1,.44f,1),frame,.035f);
                // The near edge is a low safety coaming, never a high visual wall.
                Box(this,new(x+.5f,.29f,z+.5f),new(.97f,.58f,.97f),ivory,.05f).Name="ObservationBoundary";
                Box(this,new(x+.5f,.59f,z+.5f),new(1,.06f,1),brass,.015f);
                continue;
            }
            if(x<3||z<3||x>=Width-3||z>=Depth-3)
            {
                Box(this,new(x+.5f,-.09f,z+.5f),new(.995f,.18f,.995f),ivory,.035f).Name="NonslipObservationRim";
                // Recessed rubber treads and rounded rivets distinguish safe footing.
                bool edge=x==2||x==Width-3||z==2||z==Depth-3;
                if(edge)
                {
                    Box(this,new(x+.5f,.006f,z+.5f),new(.84f,.012f,.18f),frame,.025f);
                    foreach(float dx in new[]{-.32f,.32f})Bead(this,new(x+.5f+dx,.045f,z+.5f),new(.09f,.06f,.09f),brass);
                }
            }
            else if(cell.Kind==CellKind.Pillar)
            {
                float h=(cell.ObstacleHeight??2.1f)*HeightScale;
                var tower=new Node3D{Name=$"CoralOrangeTower{x}_{z}",Position=new(x+.5f,0,z+.5f)};AddChild(tower);
                var coral=_art.Toy(new("cb7859"),.68f);
                var warm=_art.Toy(new("e7a075"),.7f);
                Box(tower,new(0,.10f,0),new(.99f,.2f,.99f),ivory,.045f);
                Box(tower,new(0,h/2,0),new(.72f,h,.72f),coral,.075f);
                foreach(float dx in new[]{-.28f,.28f})
                    Box(tower,new(dx,h*.54f,.385f),new(.1f,h*.85f,.06f),warm,.025f);
                Box(tower,new(0,h*.5f,.38f),new(.18f,h*.76f,.025f),dark,.035f);
                Box(tower,new(0,h-.11f,0),new(.88f,.22f,.88f),warm,.045f);
                Box(tower,new(0,h-.105f,.45f),new(.18f,.1f,.015f),brass,.02f);
            }
        }
        foreach(float side in new[]{1.4f,Width-1.4f})
        {
            Box(this,new(side,-1.1f,Depth/2f),new(.3f,1.7f,Depth-1),dark,.04f);
            Box(this,new(Width/2f,-1.1f,side),new(Width-1,1.7f,.3f),dark,.04f);
        }
    }

    private Node3D CreateSeaCargo(Node3D root,int cell)
    {
        root.Name="ObservationCargo"+cell;
        var wood=_art.Toy(new("c6a77c"),.85f);var ends=_art.Toy(new("e1c8a0"),.82f);
        var binding=_art.Toy(new("657f86"),.7f);
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
