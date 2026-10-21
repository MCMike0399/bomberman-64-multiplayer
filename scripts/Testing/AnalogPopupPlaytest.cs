using Godot;
using BlastCircuit.InputDevices;
using BlastCircuit.UI;
namespace Bomber.Testing;

public partial class PlaytestDriver
{
    private async Task PopupReady(PopupMenu popup,string context)
    {
        var wall=System.Diagnostics.Stopwatch.StartNew();double? stableSince=null;
        int initial=popup.GetFocusedItem();bool ready=false;
        while(wall.Elapsed.TotalSeconds<2)
        {
            if(popup.Visible&&popup.GetFocusedItem()>=0)
            {
                stableSince??=wall.Elapsed.TotalSeconds;
                if(wall.Elapsed.TotalSeconds-stableSince.Value>=.1){ready=true;break;}
            }
            else stableSince=null;
            await Frames(1);await Task.Delay(15);
        }
        Event("popup-native-ready",new{context,initial,focused=popup.GetFocusedItem(),visible=popup.Visible,
            elapsedSeconds=wall.Elapsed.TotalSeconds,window=GetWindow().HasFocus(),application=ControllerHub.Current.HasApplicationFocus});
        Check(ready,"actual popup visibility and item focus settle before navigation: "+context);
    }
    private async Task AnalogPopupMenus()
    {
        var hub=ControllerHub.Current;PopupMenu? openPopup=null;
        Event("analog-popup-input-mode",new {source="synthetic Switch reports through production native publication bridge",physicalButtonsTested=false,windowTargetingByHarness=false});
        try
        {
            CtlReleaseAll();hub.OverrideDevicesForPlaytest([QaPads[1]]);_root.Navigate("Roster");await Frames(5);await ControllerFocusReady();
            var role=(OptionButton)NamedButton("Slot1Role");
            async Task Confirm()
            {CtlButton(1001,JoyButton.B,true);await Frames(1);CtlButton(1001,JoyButton.B,false);await Frames(3);}
            async Task Tilt(JoyAxis axis,int sign)
            {
                foreach(float amount in new[]{.35f,.42f,.48f,.65f}){CtlAxis(1001,axis,amount*sign);await Frames(1);}
                CtlAxis(1001,axis,0);await Frames(2);
            }
            foreach(var (target,expected) in new[]{(1,"human"),(2,"cpu")})
            {
                role.GrabFocus();await Frames(1);await Confirm();
                var popup=role.GetPopup();openPopup=popup;
                Check(popup.Visible,"Switch printed A opens actual role PopupMenu for "+expected);
                await PopupReady(popup,"Switch role "+expected);
                int current=popup.GetFocusedItem();Check(current>=0,"native popup focuses an actual role item");
                Event("analog-native-popup",new {popupWindow=popup.GetWindowId(),focused=current,target,applicationFocus=hub.HasApplicationFocus});
                for(int step=0;step<Math.Abs(target-current);step++)await Tilt(JoyAxis.LeftY,target<current?-1:1);
                Check(popup.GetFocusedItem()==target,"gradual native stick selects role item: "+expected);
                await Capture("analog-role-popup-"+expected);await Confirm();
                Check(!popup.Visible,"Switch printed A accepts and closes role popup: "+expected);
                Check(Roster.GetProperty("slots")[1].GetProperty("role").GetString()==expected,"native stick and printed A update actual roster role: "+expected);
                openPopup=null;
            }
            _root.Navigate("Setup");await Frames(4);await ControllerFocusReady();
            var state=_root.FindChild("SetupSettings",true,false)??throw new InvalidOperationException("Setup state missing.");
            var menu=(ArcadeMenu)state.GetParent().GetParent().GetParent();
            int before=menu.Settings.Wins;NamedButton("Winstovictory").GrabFocus();await Frames(1);
            await Tilt(JoyAxis.LeftX,1);Check(menu.Settings.Wins==(before==5?1:before+1),"native gradual analog right changes real setup win dial");
            await Tilt(JoyAxis.LeftX,-1);Check(menu.Settings.Wins==before,"native gradual analog left reverses real setup win dial");
            Check(NamedButton("Winstovictory").HasFocus(),"analog setup value changes preserve native dial focus");
            await Tilt(JoyAxis.LeftY,1);Check(NamedButton("Roundtime").HasFocus(),"native gradual analog down navigates setup rules rail");
            await Capture("analog-setup-dial");
        }
        finally
        {
            openPopup?.Hide();CtlReleaseAll();hub.RestorePhysicalDevicesForPlaytest();_root.Navigate("Modes");await Frames(3);
        }
    }
}
