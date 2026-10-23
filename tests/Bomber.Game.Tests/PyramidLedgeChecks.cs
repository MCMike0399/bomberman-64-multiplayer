using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;

internal static class PyramidLedgeChecks
{
    public static int Run()
    {
        var terrain=BlastStages.Get(BlastStages.SunkenTemple);var failures=new List<string>();int checkedEdges=0;
        for(int x=1;x<terrain.Width-1;x++)for(int z=1;z<terrain.Height-1;z++)
        foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1),(1,1),(-1,1),(1,-1),(-1,-1)})
        foreach(int alignment in new[]{-1,0,1})
        {
            var from=terrain.At(x,z);var to=terrain.At(x+dx,z+dz);
            if(!from.IsPassable||!to.IsPassable||!(dx==0||dz==0?terrain.CanWalk(x,z,x+dx,z+dz):
                terrain.CanWalk(x,z,x+dx,z)&&terrain.CanWalk(x+dx,z,x+dx,z+dz)&&terrain.CanWalk(x,z,x,z+dz)&&terrain.CanWalk(x,z+dz,x+dx,z+dz)))continue;
            float upper=terrain.SurfaceAt(x+.5f,z+.5f),lower=terrain.SurfaceAt(x+dx+.5f,z+dz+.5f);
            if(upper<=lower+.01f)continue;
            // A diagonal with an uphill intermediate terrace is a real wall, not a downhill exit.
            if(dx!=0&&dz!=0&&(terrain.SurfaceAt(x+dx+.5f,z+.5f)>upper+.01f||terrain.SurfaceAt(x+.5f,z+dz+.5f)>upper+.01f))continue;
            var stage=new StageLayout {Id="temple-ledge",Width=terrain.Width,Height=terrain.Height,Cells=terrain.Cells,Spawns=[(x,z),(1,20)],SoftBlockDensity=0,InitialItems=[(x,z,"remote")]};
            var m=new LocalMatch(new MatchOptions {HumanPlayers=2,BotPlayers=0,Practice=true},stage);Step(m,64);
            if(alignment!=0){m.SetInput(0,new PlayerInput(-dz*alignment,dx*alignment));Step(m,1);}
            m.SetInput(0,new PlayerInput(dx,dz));Step(m,2);m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,2);m.SetInput(0,default);Step(m,2);m.SetInput(0,new PlayerInput(dx,dz,A:true));
            bool crossed=false,launched=false;string last="";
            for(int tick=0;tick<35;tick++)
            {
                Step(m,1);var bombs=State(m).GetProperty("bombs");if(bombs.GetArrayLength()==0)continue;
                var b=bombs[0];last=b.ToString();int state=b.GetProperty("state").GetInt32();
                if(state is 1 or 3){launched=true;m.SetInput(0,default);}
                float bx=b.GetProperty("x").GetSingle(),bz=b.GetProperty("z").GetSingle();
                if((bx-(x+.5f))*dx+(bz-(z+.5f))*dz>.5f&&b.GetProperty("y").GetSingle()<upper-.02f)crossed=true;
            }
            checkedEdges++;
            if(!crossed)failures.Add($"({x},{z}) -> ({x+dx},{z+dz}) {upper}->{lower}, alignment={alignment}; launched={launched}: {last}");
        }
        foreach(var failure in failures)Console.WriteLine("FAIL pyramid ledge "+failure);
        if(failures.Count>0)throw new InvalidOperationException($"{failures.Count}/{checkedEdges} pyramid ledges failed.");
        Console.WriteLine($"PASS {checkedEdges} pyramid downhill edge/alignment cases (cardinal + diagonal kicks, exact terrain, cover removed)");return checkedEdges;
    }
    static void Step(LocalMatch m,int n){for(int i=0;i<n;i++)m.Tick();}
    static JsonElement State(LocalMatch m){using var d=JsonDocument.Parse(m.StateJson!);return d.RootElement.GetProperty("d").Clone();}
}
