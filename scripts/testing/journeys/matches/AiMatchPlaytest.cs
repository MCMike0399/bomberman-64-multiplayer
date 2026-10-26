using Godot;
using Bomber.Client.Sessions;
using System.Text.Json;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task AiMatches()
    {
        foreach(string difficulty in new[]{"easy","medium","hard"})
        {
            if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
            _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:3,Wins:3,BotDifficulty:difficulty),1);
            Input(0,default);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,$"{difficulty} real four-player match reaches fight",150);
            Event("ai-match-start",new {difficulty,frame=Engine.GetProcessFrames(),human="stationary observer; no actor state edits"});
            var anchors=new Dictionary<string,(float X,float Z,int Idle)>();
            var moving=new HashSet<string>();var bombs=new HashSet<string>();var blasts=new HashSet<string>();
            int maxIdle=0,round=State.GetProperty("round").GetInt32();
            for(int frame=0;frame<600&&_root.ScreenName=="Match";frame++)
            {
                await Frames(1);var state=State;
                int currentRound=state.GetProperty("round").GetInt32();
                if(currentRound!=round){anchors.Clear();round=currentRound;}
                foreach(var b in state.GetProperty("bombs").EnumerateArray())
                    if(b.GetProperty("ownerId").GetString()!=Player.GetProperty("id").GetString())bombs.Add(b.GetProperty("id").GetString()!);
                foreach(var h in state.GetProperty("hazards").EnumerateArray())blasts.Add(h.GetProperty("id").GetString()!);
                foreach(var p in state.GetProperty("players").EnumerateArray().Where(p=>p.GetProperty("bot").GetBoolean()))
                {
                    string id=p.GetProperty("id").GetString()!;float x=p.GetProperty("x").GetSingle(),z=p.GetProperty("z").GetSingle();
                    bool eligible=state.GetProperty("phase").GetInt32() is 1 or 2&&p.GetProperty("alive").GetBoolean()&&!p.GetProperty("ghost").GetBoolean()&&!p.GetProperty("stunned").GetBoolean()&&!p.GetProperty("airborne").GetBoolean()&&p.GetProperty("carriedBy").ValueKind==JsonValueKind.Null&&p.GetProperty("kickWindup").GetSingle()<=0&&p.GetProperty("kickRecovery").GetSingle()<=0;
                    if(!eligible||!anchors.TryGetValue(id,out var a)){anchors[id]=(x,z,0);continue;}
                    float distance=MathF.Sqrt((x-a.X)*(x-a.X)+(z-a.Z)*(z-a.Z));
                    if(distance>.12f){moving.Add(id);anchors[id]=(x,z,0);}
                    else {int idle=a.Idle+1;maxIdle=Math.Max(maxIdle,idle);anchors[id]=(a.X,a.Z,idle);}
                }
                if(frame==90)await Capture("ai-"+difficulty+"-opening",0);
                if(frame==360)await Capture("ai-"+difficulty+"-battle",0);
            }
            Check(moving.Count==3,$"{difficulty} all three rendered CPU contenders demonstrate actual movement");
            Check(bombs.Count>0&&blasts.Count>0,$"{difficulty} produces real bombs and rendered explosions");
            Check(maxIdle<150,$"{difficulty} actual-match eligible actors never freeze for five seconds");
            Event("ai-match-observed",new {difficulty,moving=moving.Count,bombs=bombs.Count,blasts=blasts.Count,longestEligibleIdle=maxIdle/30f,frame=Engine.GetProcessFrames()});
            await Capture("ai-"+difficulty+"-outcome",0);
            _root.Back();await Frames(3);
        }
    }
}
