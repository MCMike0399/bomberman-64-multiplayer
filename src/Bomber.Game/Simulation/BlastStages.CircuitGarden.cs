namespace Bomber.Game.Simulation;

public static partial class BlastStages
{
    public const string CircuitGarden = "circuit-garden";

    private static StageLayout BuildCircuitGarden()
    {
        const int width=23,height=19;
        var cells=Fill(width,height,CellDef.Floor0);
        // An open lawn, a broad teaching terrace, and a separate canal-side landing dock.
        for(int x=0;x<width;x++)for(int z=0;z<height;z++)
            if(x>=19 || z>=17)cells[x,z]=CellDef.Water;
        for(int x=8;x<=14;x++)for(int z=3;z<=6;z++)cells[x,z]=CellDef.Floor1;
        for(int x=10;x<=12;x++)cells[x,7]=CellDef.Ramp(0,1,0,-1);
        for(int x=10;x<=12;x++)for(int z=1;z<=2;z++)cells[x,z]=CellDef.Floor2;
        for(int x=10;x<=12;x++)cells[x,3]=CellDef.Ramp(1,2,0,-1);
        for(int x=20;x<=22;x++)for(int z=9;z<=14;z++)cells[x,z]=CellDef.Floor0;
        foreach(var (x,z) in new[]{(4,5),(4,9),(16,5),(16,9)})cells[x,z]=CellDef.Pillar;
        return new StageLayout
        {
            Id=CircuitGarden,Name="Circuit Garden",Width=width,Height=height,Cells=cells,
            Spawns=[(1,1),(17,1),(1,15),(17,15),(6,12),(14,12),(6,3),(16,3)],
            SoftBlockDensity=0,MaxSoftBlocks=0,
            TrainingItems=[(5,12,"fire"),(6,12,"bomb"),(7,12,"remote"),(8,12,"red"),
                (9,12,"bounce"),(10,12,"fast"),(11,12,"heart"),(12,12,"focus"),
                (5,14,"tiny"),(7,14,"big"),(9,14,"haste"),(11,14,"slow")],
        };
    }

}
