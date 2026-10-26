using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using System.Text.Json;
namespace BlastCircuit.Testing;

/// <summary>Replays a JSON input script (tools/playtest/*.json) through ordinary match input.</summary>
public partial class PlaytestDriver
{
    private async Task Script(string path)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(path));
        foreach(var step in document.RootElement.GetProperty("steps").EnumerateArray())
        {
            var op=step.GetProperty("op").GetString();Event("step",step);
            int Number(string key,int fallback) => step.TryGetProperty(key,out var p)?p.GetInt32():fallback;
            float Scalar(string key,float fallback=0) => step.TryGetProperty(key,out var p)?p.GetSingle():fallback;
            bool Flag(string key) => step.TryGetProperty(key,out var p)&&p.GetBoolean();
            switch(op)
            {
                case "navigate":_root.Navigate(step.GetProperty("screen").GetString()!);break;
                case "back":_root.Back();break;
                case "click":await Click(step.GetProperty("label").GetString()!);break;
                case "mouse-click":await MouseClick(step.GetProperty("label").GetString()!);break;
                case "resize":GetWindow().Size=new Vector2I(Number("width",1280),Number("height",800));await Frames(6);break;
                case "start-local":_root.StartLocal(new SessionSettings(Stage:step.GetProperty("stage").GetString()!,Bots:Number("bots",0)),Number("humans",1),Flag("practice"));break;
                case "frames":await Frames(Number("count",1));break;
                case "input":Input(Number("seat",0),new PlayerInput(Scalar("x"),Scalar("z"),Flag("a"),Flag("b"),Flag("r"),Flag("aim")));break;
                case "move-to":await MoveTo(Scalar("x"),Scalar("z"),Number("maxFrames",300));break;
                case "capture":await Capture(step.GetProperty("label").GetString()!);break;
                case "assert-screen":Check(_root.ScreenName==step.GetProperty("screen").GetString(),"script screen "+step.GetProperty("screen"));break;
                case "assert-state":
                    var value=State;foreach(var segment in step.GetProperty("path").GetString()!.Split('.'))value=int.TryParse(segment,out var index)?value[index]:value.GetProperty(segment);
                    Check(step.TryGetProperty("equals",out var equal)?value.ToString()==equal.ToString():value.GetDouble()>=step.GetProperty("gte").GetDouble(),"script state "+step.GetProperty("path"));break;
                default:throw new ArgumentException("Unknown script op: "+op);
            }
        }
    }
}
