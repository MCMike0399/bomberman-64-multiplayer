using Godot;
using System.Text.Json;
using BlastCircuit.Arena.Stages;
namespace BlastCircuit.Arena;

public partial class ArenaView
{
    // Called only by the local tutorial owner. Peer/online snapshots never drive training UI.
    public void TutorialTarget(JsonElement state)
    {
        bool show=_battleActive && state.TryGetProperty("tutorial",out var data) && data.ValueKind==JsonValueKind.Object &&
            !data.GetProperty("complete").GetBoolean()&&!data.GetProperty("failed").GetBoolean()&&data.GetProperty("targetX").ValueKind==JsonValueKind.Number;
        if(!show){if(_tutorialTarget!=null)_tutorialTarget.Visible=false;return;}
        var tutorial=state.GetProperty("tutorial");
        if(_tutorialTarget==null)
        {
            _tutorialTarget=new MeshInstance3D {Name="TutorialTarget",Mesh=new TorusMesh {InnerRadius=.59f,OuterRadius=.69f,Rings=48,RingSegments=8},
                MaterialOverride=new StandardMaterial3D {AlbedoColor=new Color("ffd66d"),EmissionEnabled=true,Emission=new Color("a17222"),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded},CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
            AddChild(_tutorialTarget);
        }
        _tutorialTarget.Visible=true;
        _tutorialTarget.Position=new(tutorial.GetProperty("targetX").GetSingle(),tutorial.GetProperty("targetY").GetSingle()*TempleStageView.HeightScale+.065f,tutorial.GetProperty("targetZ").GetSingle());
    }
}
