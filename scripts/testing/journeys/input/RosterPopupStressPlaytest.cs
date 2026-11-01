using Godot;
using BlastCircuit.InputDevices;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private const int RosterPopupStressRounds=7;
    private static IEnumerable<Node> AllDescendants(Node parent)
    {
        foreach(var child in parent.GetChildren(true)){yield return child;foreach(var descendant in AllDescendants(child))yield return descendant;}
    }
    /// <summary>Opens and closes every Roster role popup through the real controller path many times, then
    /// proves Godot's embedded-popup teardown left no root-Window connections or popups behind
    /// (native Linux popups crashed NVIDIA's driver: GPU runs 41285894565, 41304300722).</summary>
    private async Task RosterPopupStress()
    {
        var hub=ControllerHub.Current;PopupMenu? openPopup=null;
        try
        {
            CtlReleaseAll();hub.OverrideDevicesForPlaytest([QaPads[1]]);_root.Navigate("Roster");await Frames(5);await ControllerFocusReady();
            var root=GetTree().Root;
            int focusConnections=root.GetSignalConnectionList(Window.SignalName.FocusEntered).Count;
            int lifetimeConnections=root.GetSignalConnectionList(Node.SignalName.TreeExited).Count;
            int popupsBefore=AllDescendants(root).Count(node=>node is Popup);
            async Task Confirm(){CtlButton(1001,JoyButton.B,true);await Frames(1);CtlButton(1001,JoyButton.B,false);await Frames(3);}
            var roles=Enumerable.Range(0,8).Select(slot=>(OptionButton)NamedButton($"Slot{slot}Role")).ToArray();
            var before=roles.Select(role=>role.Selected).ToArray();
            int cycles=0;bool allClosed=true,stayed=true,unchanged=true,opened=true;
            for(int round=0;round<RosterPopupStressRounds;round++)
                for(int slot=0;slot<roles.Length;slot++)
                {
                    var role=roles[slot];var popup=role.GetPopup();
                    role.GrabFocus();await Frames(1);await Confirm();
                    opened&=popup.Visible;openPopup=popup;
                    await PopupReady(popup,$"stress slot {slot} round {round}");
                    // Accept the item that is already selected: a full open/select/close cycle
                    // without changing the lineup.
                    await Confirm();openPopup=null;
                    allClosed&=!popup.Visible;stayed&=_root.ScreenName=="Roster";unchanged&=role.Selected==before[slot];
                    cycles++;
                }
            await Frames(4);
            int focusAfter=root.GetSignalConnectionList(Window.SignalName.FocusEntered).Count;
            int lifetimeAfter=root.GetSignalConnectionList(Node.SignalName.TreeExited).Count;
            var popups=AllDescendants(root).OfType<Popup>().ToArray();
            Event("roster-popup-stress",new {cycles,focusConnections,focusAfter,lifetimeConnections,lifetimeAfter,popupsBefore,
                popupsAfter=popups.Length,visiblePopups=popups.Count(popup=>popup.Visible),embedded=roles.All(role=>role.GetPopup().IsEmbedded())});
            Check(cycles>=50,"role popups opened and closed at least 50 times through the controller");
            Check(opened,"every printed-A press opened the actual role popup");
            Check(allClosed,"every confirm closed the role popup");
            Check(stayed,"popup selection never activated parent navigation");
            Check(unchanged,"re-selecting the current item kept every seat's role");
            Check(roles.All(role=>role.GetPopup().IsEmbedded()),"role popups are embedded windows");
            Check(focusAfter==focusConnections&&lifetimeAfter==lifetimeConnections,
                $"root Window focus_entered/tree_exited connections return to baseline after {cycles} popup cycles");
            Check(popups.Length==popupsBefore&&popups.All(popup=>!popup.Visible),"no PopupMenu or PopupPanel survives the stress cycles");
            await Capture("roster-popup-stress-final");
        }
        finally
        {
            openPopup?.Hide();CtlReleaseAll();hub.RestorePhysicalDevicesForPlaytest();_root.Navigate("Modes");await Frames(3);
        }
    }
}
