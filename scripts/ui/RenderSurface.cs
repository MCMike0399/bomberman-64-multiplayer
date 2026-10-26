using Godot;
using BlastCircuit.Rendering;
namespace BlastCircuit.UI;

/// <summary>A display-only 3D texture sized in output pixels, independent of logical UI layout.</summary>
public partial class RenderSurface : TextureRect
{
    private SubViewport? _renderViewport;
    private int _revision=-1;
    private bool _wasVisible;
    public void SetViewport(SubViewport viewport)
    {
        MouseFilter=MouseFilterEnum.Ignore;
        ExpandMode=ExpandModeEnum.IgnoreSize;
        StretchMode=StretchModeEnum.Scale;
        _renderViewport=viewport;
        GraphicsQuality.ApplyViewport(viewport);
        AddChild(viewport);
        Texture=viewport.GetTexture();
        UpdateSurface();
    }
    public override void _Process(double delta)=>UpdateSurface();
    private void UpdateSurface()
    {
        if(_renderViewport==null)return;
        bool visible=IsVisibleInTree();
        if(!visible){_wasVisible=false;return;}
        // SubViewportContainer.Stretch uses logical Control.Size. On a Retina/fullscreen
        // canvas that enlarges an already rasterized image, including its 3D labels.
        var transform=GetViewport().GetScreenTransform()*GetGlobalTransformWithCanvas();
        var pixels=Size*new Vector2(transform.X.Length(),transform.Y.Length());
        var size=new Vector2I(Math.Max(2,Mathf.RoundToInt(pixels.X)),Math.Max(2,Mathf.RoundToInt(pixels.Y)));
        // Ready/startup preparation can draw before Control layout has settled.
        // High's contact shading needs five depth mip levels; a temporary 2x2
        // target cannot supply them and crashes the renderer. Keep the authored
        // (or last valid) target until layout can support that depth pyramid.
        if(size.X<32||size.Y<32){_wasVisible=false;return;}
        bool redraw=!_wasVisible||_renderViewport.Size!=size||_revision!=GraphicsQuality.Revision;
        if(_renderViewport.Size!=size)_renderViewport.Size=size;
        if(_revision!=GraphicsQuality.Revision)
        {GraphicsQuality.ApplyViewport(_renderViewport);_revision=GraphicsQuality.Revision;}
        if(redraw)_renderViewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
        _wasVisible=true;
    }
}
