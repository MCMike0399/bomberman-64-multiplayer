using Godot;
using Bomber.Client.Sessions;
using BlastCircuit.InputDevices;
using BlastCircuit.UI.Menu;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task MenuNavigation()=>await WithControllerFocusRetries(MenuNavigationAttempt,"menu-navigation");

    private async Task MenuNavigationAttempt()
    {
        CtlReleaseAll();await ControllerFocusReady();
        var hub=ControllerHub.Current;hub.OverrideDevicesForPlaytest(QaPads);
        var menu=AuditDescendants(_root).OfType<ArcadeMenu>().Single();
        Event("menu-navigation-input-mode",new {physicalButtonsTested=false,source="native keyboard, canonical Xbox/DualSense and Switch USB publication events"});
        async Task Pad(JoyButton button,int device=12)
        {CtlButton(device,button,true);await ControllerFrames(1);CtlButton(device,button,false);await ControllerFrames(3);}
        async Task Focused(string name,string reason)
        {await ControllerFrames(1);Check(GetViewport().GuiGetFocusOwner()?.Name==name,reason+" (expected "+name+", actual "+GetViewport().GuiGetFocusOwner()?.Name+")");}
        async Task Start(string name,string reason)
        {
            string screen=_root.ScreenName;
            foreach(var pad in QaPads)
            {
                await Pad(JoyButton.Start,pad.RuntimeId);
                await Focused(name,pad.Name+": Start focuses "+reason);
                Check(_root.ScreenName==screen,"Start never activates "+reason);
            }
        }
        async Task TabCycle(string page)
        {
            var expected=AuditDescendants(menu).OfType<Control>().Where(c=>c.IsVisibleInTree()&&c.FocusMode==Control.FocusModeEnum.All&&c is not BaseButton {Disabled:true}).ToHashSet();
            var visited=new HashSet<Control>();
            var first=GetViewport().GuiGetFocusOwner();
            for(int i=0;i<=expected.Count;i++)
            {
                if(GetViewport().GuiGetFocusOwner() is {} focused)visited.Add(focused);
                await KeyPress(Key.Tab);
                if(GetViewport().GuiGetFocusOwner()==first)break;
            }
            Event("menu-tab-cycle",new {page,expected=expected.Select(c=>c.Name.ToString()),visited=visited.Select(c=>c.Name.ToString())});
            Check(expected.Count>0&&expected.SetEquals(visited),page+": every visible enabled control is reachable in one Tab cycle; missing "+string.Join(",",expected.Except(visited).Select(c=>c.Name)));
        }
        try
        {
            // Focus memory intentionally survives other journeys. Establish
            // known history through real controls, including a non-first rule,
            // instead of assuming these columns have never been visited.
            async Task Remember(string page,params string[] controls)
            {
                _root.Navigate(page);await ControllerFrames(4);
                foreach(string name in controls)
                {NamedButton(name).GrabFocus();await ControllerFrames(1);Check(NamedButton(name).HasFocus(),"focus history records "+name);}
            }
            _root.Navigate("Setup");await ControllerFrames(4);
            await ActivateNamed("AdvancedSettings");NamedButton("Allow_fire").GrabFocus();await ControllerFrames(2);
            await Remember("Setup","Roundtime");
            await Remember("Settings","GraphicsPreset");
            await Remember("Online","RoomSettings");
            await Remember("Roster","Slot2Role","Slot0Appearance");
            _root.Navigate("Modes");await ControllerFrames(4);
            foreach(var pad in QaPads)
            {
                FindButton("Local Battle").GrabFocus();
                CtlButton(pad.RuntimeId,JoyButton.DpadDown,true);await ControllerFrames(2);
                Check(FindButton("Online Battle").HasFocus(),pad.Name+": D-pad press moves exactly one row");
                await ControllerFrames(14);Check(FindButton("Tutorial").HasFocus(),pad.Name+": held D-pad repeats without more events");
                CtlButton(pad.RuntimeId,JoyButton.DpadDown,false);CtlButton(pad.RuntimeId,JoyButton.DpadUp,true);await ControllerFrames(2);
                Check(FindButton("Online Battle").HasFocus(),pad.Name+": D-pad reversal moves immediately once");
                CtlButton(pad.RuntimeId,JoyButton.DpadUp,false);await ControllerFrames(2);
                CtlButton(pad.RuntimeId,JoyButton.DpadDown,true);CtlButton(pad.RuntimeId,JoyButton.DpadDown,false);await ControllerFrames(2);
                Check(FindButton("Tutorial").HasFocus(),pad.Name+": sub-frame D-pad tap is retained without repeats");
                FindButton("Local Battle").GrabFocus();CtlAxis(pad.RuntimeId,JoyAxis.LeftY,.65f);await ControllerFrames(2);
                CtlButton(pad.RuntimeId,JoyButton.DpadUp,true);await ControllerFrames(2);
                Check(FindButton("Local Battle").HasFocus(),pad.Name+": D-pad takes priority over a held stick");
                CtlButton(pad.RuntimeId,JoyButton.DpadUp,false);CtlAxis(pad.RuntimeId,JoyAxis.LeftY,0);await ControllerFrames(2);
            }
            FindButton("Local Battle").GrabFocus();
            await KeyPress(Key.Down);await KeyPress(Key.Enter);Check(_root.ScreenName=="Online","keyboard opens Online from Modes");
            await Start("PlayOnline","public play from identity column");
            await KeyPress(Key.Escape);Check(_root.ScreenName=="Modes","Esc returns to Modes");
            Check(FindButton("Online Battle").HasFocus(),"Back restores the mode that opened Online");
            await KeyPress(Key.Up);await KeyPress(Key.Enter);await ControllerFrames(3);
            Check(_root.ScreenName=="Roster","keyboard opens player select");
            await Start("RosterContinue","Continue to rules");
            await KeyPress(Key.Up);await Focused("Slot0Appearance","Up from Continue returns to selected seat");
            await Pad(JoyButton.DpadRight);await Focused("Slot1Appearance","right keeps the same row across seats");
            await Pad(JoyButton.DpadUp);await Focused("Slot1Role","up stays in the selected seat");
            await Pad(JoyButton.RightShoulder);await Focused("Slot2Role","shoulder switches to next seat");
            await Pad(JoyButton.LeftShoulder);await Focused("Slot1Role","shoulder restores previous seat focus");
            await Capture("navigation-roster");
            // A native popup owns Back. Closing it must not leave player select.
            await Pad(JoyButton.A);var popup=((OptionButton)NamedButton("Slot1Role")).GetPopup();
            await PopupReady(popup,"navigation Back");
            await Pad(JoyButton.B);Check(!popup.Visible&&_root.ScreenName=="Roster","controller Back closes only the role popup");
            await Start("RosterContinue","Continue after closing a popup");await KeyPress(Key.Enter);
            Check(_root.ScreenName=="Setup","explicit confirm advances to rules");
            await Pad(JoyButton.RightShoulder);await Focused("Roundtime","shoulder restores a rule selected before this journey");
            await KeyPress(Key.Up);await Focused("Winstovictory","up stays in the rules rail");
            await KeyPress(Key.Down);await Focused("Roundtime","down stays in the rules rail");
            await Pad(JoyButton.LeftShoulder);await Focused("StageSelector","shoulder returns to arena column");
            await Pad(JoyButton.RightShoulder);await Focused("Roundtime","column switch restores the last rule");
            int rounds=menu.Settings.RoundSeconds;await KeyPress(Key.Right);
            Check(menu.Settings.RoundSeconds!=rounds,"left/right still edits a focused rule");
            await ActivateNamed("AdvancedSettings");await ControllerFrames(3);
            await Focused("Itemdropchance","advanced page starts on a rule");
            await Pad(JoyButton.RightShoulder);await Focused("Allow_fire","shoulder enters allowed-items column");
            await KeyPress(Key.Down);await Focused("Allow_remote","down stays in the item column");
            await KeyPress(Key.Right);await Focused("Allow_red","right crosses to matching item row");
            await KeyPress(Key.Down);await Focused("Allow_heart","down stays in right item column");
            await KeyPress(Key.Left);await Focused("Allow_bounce","left preserves the item row when revisiting a column");
            await KeyPress(Key.Up);await KeyPress(Key.Up);await KeyPress(Key.Right);
            await Focused("Allow_bomb","right preserves the new row instead of an old selection");
            await Capture("navigation-advanced");
            await Start("AdvancedBack","Back to match settings");await KeyPress(Key.Escape);
            Check(_root.ScreenName=="Setup"&&NamedButton("AdvancedSettings").HasFocus(),"Esc restores the Advanced settings opener");
            await Start("LaunchMatch","Start battle");await KeyPress(Key.Escape);
            Check(_root.ScreenName=="Roster"&&NamedButton("RosterContinue").HasFocus(),"Esc returns to roster and preserves Continue focus");
            await KeyPress(Key.Escape);Check(_root.ScreenName=="Modes","second Esc returns to Modes");

            await ActivateNamed("OpenSettings");await ControllerFrames(4);
            await Focused("SoundVolume","settings starts on sound volume, including non-button controls");
            await Pad(JoyButton.RightShoulder);await Focused("GraphicsPreset","shoulder switches from volume to graphics");
            await Pad(JoyButton.LeftShoulder);await Focused("SoundVolume","shoulder restores volume");
            await KeyPress(Key.Down);await Focused("Vibration","down reaches vibration without jumping columns");
            await KeyPress(Key.Tab);await Focused("TestVibration","Tab follows reading order");
            await KeyPress(Key.Down);await Focused("StageLighting","down reaches lighting in the same column");
            await KeyPress(Key.Down);await Focused("PerformanceMetrics","down reaches performance metrics");
            await KeyPress(Key.Down);await Focused("SettingsBack","down from last preference reaches Back");
            await KeyPress(Key.Up);await Focused("PerformanceMetrics","up from Back restores the active column");
            await Capture("navigation-settings");
            await Start("SettingsBack","save and return");await KeyPress(Key.Escape);
            Check(_root.ScreenName=="Modes"&&NamedButton("OpenSettings").HasFocus(),"settings Back restores its opener");

            _root.Navigate("Online");await ControllerFrames(4);
            await Pad(JoyButton.RightShoulder);await Focused("RoomSettings","shoulder enters hosting column");
            await Start("CreatePrivateRoom","Create private room");
            await Pad(JoyButton.RightShoulder);await Focused("PlayOnline","shoulder restores the join column's last action");
            await KeyPress(Key.Up);await KeyPress(Key.Up);await Focused("RoomCode","up reaches code entry in the join column");
            await Start("JoinWithCode","Join with code");
            await Capture("navigation-online");
            // Text input retains WASD/caret editing; Esc returns a whole menu in one press.
            var field=(LineEdit)_root.FindChild("PlayerName",true,false)!;
            string original=field.Text;field.Text="";field.GrabFocus();
            foreach(var (key,letter) in new[]{(Key.W,'w'),(Key.A,'a'),(Key.S,'s'),(Key.D,'d')})
            {
                Godot.Input.ParseInputEvent(new InputEventKey{Keycode=key,PhysicalKeycode=key,Unicode=letter,Pressed=true});await ControllerFrames(1);
                CtlKey(key,false);await ControllerFrames(1);
            }
            Check(field.HasFocus()&&field.Text=="wasd","typing WASD never navigates away from text fields");field.Text=original;
            await KeyPress(Key.Escape);Check(_root.ScreenName=="Modes","one Esc backs out while a text field is editing");
            _root.Navigate("Online");await ControllerFrames(3);await ActivateNamed("RoomSettings");
            await ActivateNamed("AdvancedSettings");await KeyPress(Key.Escape);await KeyPress(Key.Escape);
            Check(_root.ScreenName=="Online"&&NamedButton("RoomSettings").HasFocus(),"nested room settings return to their Online opener");
            await ActivateNamed("RoomSettings");await Start("LaunchMatch","Save room settings");await KeyPress(Key.Enter);
            Check(_root.ScreenName=="Online"&&NamedButton("RoomSettings").HasFocus(),"Save returns to Online and restores its opener");
            await KeyPress(Key.Escape);Check(_root.ScreenName=="Modes","Esc after Save does not loop back into room settings");
            await Click("Local Battle");await Start("RosterContinue","Continue to rules");await KeyPress(Key.Enter);
            await ActivateNamed("EditPlayers");await Start("RosterContinue","Continue after changing players");await KeyPress(Key.Enter);
            await KeyPress(Key.Escape);await KeyPress(Key.Escape);
            Check(_root.ScreenName=="Modes","changing players and continuing does not create a Back loop");
            _root.Navigate("Modes");await ControllerFrames(3);await Click("Practice");
            await ActivateNamed("AdvancedSettings");await KeyPress(Key.Escape);
            Check(menu.Practice,"advanced Back preserves Practice mode");
            await Start("LaunchMatch","Start practice");await KeyPress(Key.Escape);Check(_root.ScreenName=="Modes","practice Back returns to Modes");

            _root.Navigate("Tutorial");await ControllerFrames(4);
            await KeyPress(Key.Down);string lesson=GetViewport().GuiGetFocusOwner()!.Name.ToString();
            await Pad(JoyButton.RightShoulder);await Focused("StartLesson","shoulder reaches lesson action");
            await Pad(JoyButton.LeftShoulder);await Focused(lesson,"returning to lesson column preserves selection");
            await Start("StartLesson","Try it");await Capture("navigation-tutorial");
            await KeyPress(Key.Escape);_root.Navigate("Controls");await ControllerFrames(3);
            await Start("ControlsBack","Back from controller tester");await Pad(JoyButton.A);Check(_root.ScreenName=="Modes","confirm after Start leaves the controller tester");
            _root.Navigate("Results");await ControllerFrames(3);await KeyPress(Key.Right);
            await Start(FindButton("Play Again").Name.ToString(),"Play again");await Capture("navigation-results");
            _root.Navigate("Modes");_root.StartLocal(new SessionSettings(Bots:0),1,practice:true);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"navigation trial starts an actual practice session",150);
            var session=_root.ActiveSession;
            await KeyPress(Key.Escape);Check(_root.ScreenName=="Pause","Esc opens pause during gameplay");
            await ActivateNamed("PauseSettings");await KeyPress(Key.Escape);
            Check(_root.ScreenName=="Pause"&&NamedButton("PauseSettings").HasFocus(),"settings returns to its pause opener");
            await Start("ResumeGame","Resume game");await Capture("navigation-pause");
            await KeyPress(Key.Escape);Check(_root.ScreenName=="Match"&&ReferenceEquals(session,_root.ActiveSession),"Esc resumes the same match");
            _root.Back();await ControllerFrames(3);
            foreach(string page in new[]{"Modes","Roster","Setup","Practice","HostSetup","Tutorial","Online","Lobby","Settings","Controls","Results"})
            {
                _root.Navigate("Modes");await ControllerFrames(2);_root.Navigate(page);await ControllerFrames(4);
                await TabCycle(page);
                if(page=="Lobby")
                {
                    Check(NamedButton("CopyRoomCode").Disabled,"disconnected lobby has no copyable room code");
                    NamedButton("LobbySettings").GrabFocus();await KeyPress(Key.Up);
                    Check(FindButton("Leave room").HasFocus(),"Up crosses an empty lobby column from Settings to Leave");
                    await KeyPress(Key.Down);Check(NamedButton("LobbySettings").HasFocus(),"Down returns to the enabled lobby action row");
                }
                if(page=="Setup"){await ActivateNamed("AdvancedSettings");await TabCycle("Advanced");}
            }
        }
        finally {CtlReleaseAll();hub.RestorePhysicalDevicesForPlaytest();_root.Navigate("Modes");}
    }
}
