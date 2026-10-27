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
        // Upper stockpiles frame four corner courts. The lower treasure cache
        // forms four islands around a clear cross; the middle ring stays empty.
        var clear=Enumerable.Range(0,size).SelectMany(x=>Enumerable.Range(0,size).Select(z=>(X:x,Z:z)))
            .Where(c=>cells[c.X,c.Z].Tier==1 || (cells[c.X,c.Z].Tier==0
                ? c.X is 6 or 9 or 12 || c.Z is 6 or 9 or 12
                : c.X is 2 or 9 or 16 || c.Z is 2 or 9 or 16)).ToHashSet();
        return new StageLayout
        {
            Id=GreedyTrap,Name="Greedy Trap",Width=size,Height=size,Cells=cells,
            Spawns=[(2,2),(16,2),(2,16),(16,16),(6,2),(12,2),(6,16),(12,16)],
            OpeningCoverPerSpawn=0,SoftBlockDensity=1,MaxSoftBlocks=64,
            CoverAreas=[
                new("reward-pit",.25,7,11,7,11,0,0),
                new("northwest-stockpile",.1875,1,8,1,8,2,2){Centers=[(4,4),(3,6)]},
                new("northeast-stockpile",.1875,10,17,1,8,2,2){Centers=[(14,4),(15,6)]},
                new("southwest-stockpile",.1875,1,8,10,17,2,2){Centers=[(4,14),(3,12)]},
                new("southeast-stockpile",.1875,10,17,10,17,2,2){Centers=[(14,14),(15,12)]}],
            ClearCells=clear,
        };
    }
}
