using Bomber.Client.Sessions;
using BlastCircuit.UI.Menu;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task MiniaturePreviewRegression()
    {
        await FirstUseRendering();
        await GraphicsSettings();
        await AssetAudit();
        _root.FindChildren("*","",true,false).OfType<ArcadeMenu>().Single().Configure(new SessionSettings(),1);
        await SetupMenus();
    }
}
