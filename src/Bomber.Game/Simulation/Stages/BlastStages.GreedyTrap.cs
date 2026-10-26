// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
namespace Bomber.Game.Simulation.Stages;

public static partial class BlastStages
{
    public const string GreedyTrap="greedy-trap";

    private static StageLayout BuildGreedyTrap()
    {
        const int size=19;
        var cells=Fill(size,size,CellDef.Floor2);
        for(int z=0;z<size;z++)for(int x=0;x<size;x++)
        {
            // The near wall is cut down for the battle camera, while keeping the
            // same impassable perimeter. There is no water in the turquoise pit.
            if(x==0||z==0||x==size-1||z==size-1)
                cells[x,z]=new CellDef{Kind=CellKind.Pillar,Tier=2,ObstacleHeight=z==size-1?.48f:z==0?2.2f:1.8f};
            else if(x is >=5 and <=13&&z is >=5 and <=13)
                cells[x,z]=x is >=6 and <=12&&z is >=6 and <=12?CellDef.Floor0:CellDef.Floor1;
        }
        // Two long returns to the upper court. The west/east notches are
        // dead-end middle landings, not additional upper exits.
        for(int step=0;step<2;step++)
        {
            cells[9,4-step]=CellDef.Ramp(1+step*.5f,1.5f+step*.5f,0,-1);
            cells[9,14+step]=CellDef.Ramp(1+step*.5f,1.5f+step*.5f,0,1);
        }
        foreach(int x in new[]{3,4,14,15})cells[x,9]=CellDef.Floor1;
        foreach(int z in new[]{6,12})
        {
            cells[6,z]=CellDef.Ramp(0,1,-1,0);
            cells[12,z]=CellDef.Ramp(0,1,1,0);
        }
        HashSet<(int X,int Z)> cover=[(8,7),(10,7),(7,9),(9,9),(11,9),(8,11),(9,11),(10,11)];
        return new StageLayout
        {
            Id=GreedyTrap,Name="Greedy Trap",Width=size,Height=size,Cells=cells,
            Spawns=[(2,2),(16,2),(2,16),(16,16),(6,2),(12,2),(6,16),(12,16)],
            OpeningCoverPerSpawn=0,SoftBlockDensity=1,MaxSoftBlocks=8,
            CoverAreas=[new("reward-pit",1,7,11,7,11,0,0)],
            ClearCells=Enumerable.Range(0,size).SelectMany(x=>Enumerable.Range(0,size).Select(z=>(X:x,Z:z)))
                .Where(c=>!cover.Contains(c)).ToHashSet(),
        };
    }
}
