using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private StandardMaterial3D? _hollowLanternPaint;
    private Node3D? _hollowMole;
    // Blocked vignette only. No copied garden buildings or radial prop scatter.
    // Butterfly and fine surface finishing follow the bank shape review.
    private void BuildFieldOfGrassExterior()
    {
        var soil=_art.HollowSurface(0);_fieldMaterials.Add(soil);

        var hay=new Node3D{Name="HayRick",Position=new(-.95f,HollowLandscapeHeight(-.95f,10.2f),10.2f)};_scenery!.AddChild(hay);
        var mesh=new SurfaceTool();mesh.Begin(Mesh.PrimitiveType.Triangles);
        const int sides=12;var rings=new Vector3[4,sides];
        for(int row=0;row<4;row++)for(int i=0;i<sides;i++)
        {
            var(s,c)=PresentationMath.SinCos(i*Mathf.Tau/sides);
            float radius=row==0?.67f:row==1?.72f:row==2?.50f:.23f;
            float y=row==0?.02f:row==1?.30f:row==2?.65f:.90f;
            rings[row,i]=new(c*radius+row*.035f,y,s*radius*.74f);
        }
        for(int i=0;i<sides;i++)
        {
            int j=(i+1)%sides;
            for(int row=0;row<3;row++)HollowQuad(mesh,rings[row,i],rings[row,j],rings[row+1,j],rings[row+1,i],(rings[row,i]+rings[row,j])/2-new Vector3(0,.4f,0));
            HollowTriangle(mesh,new(.105f,.90f,0),rings[3,i],rings[3,j],Vector3.Up);
            HollowTriangle(mesh,new(0,.02f,0),rings[0,j],rings[0,i],Vector3.Down);
        }
        mesh.Index();hay.AddChild(new MeshInstance3D{Name="HollowHayRickMesh",Mesh=mesh.Commit(),MaterialOverride=_art.Color(new("b2a15c"),.98f)});
    }

    private void BuildHollowStile()
    {
        if(!IncludeScenery)return;
        var stile=new Node3D{Name="HollowStile",Position=new(-1.05f,HollowLandscapeHeight(-1.05f,8.9f),8.9f),Basis=PresentationMath.Rotation(new(.03f,.15f,.12f))};AddChild(stile);
        var wood=_art.Toy(new("705039"),.9f);
        foreach(float x in new[]{-.5f,.5f})RoundedBox(stile,new(x,.6f,0),new(.18f,1.2f,.18f),wood,.7f);
        foreach(float y in new[]{.3f,.65f,1.0f})RoundedBox(stile,new(0,y,0),new(1.25f,.16f,.15f),wood,.7f);
        _hollowLanternPaint=new StandardMaterial3D{AlbedoColor=new("eab366"),EmissionEnabled=true,Emission=new("ffb450"),
            EmissionEnergyMultiplier=.08f,Roughness=.8f,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,DisableFog=true};
        RoundedBox(stile,new(.46f,.95f,.16f),new(.28f,.38f,.25f),_hollowLanternPaint,.7f).Name="HollowStileLantern";
        _hollowLantern=new OmniLight3D{Name="HollowLanternLight",Position=new(.46f,1.05f,.28f),LightColor=new("ffb65c"),LightEnergy=0,OmniRange=3.0f,ShadowEnabled=false};stile.AddChild(_hollowLantern);
    }

    private void ApplyHollowLanternLighting(StageLighting? lighting)
    {
        bool night=lighting?.Label=="Night";
        if(_hollowLanternPaint is not null)_hollowLanternPaint.EmissionEnergyMultiplier=night?1.15f:.08f;
        if(_hollowLantern is not null)_hollowLantern.LightEnergy=night&&GraphicsQuality.Current!=GraphicsPreset.Low?1.1f:0;
    }

    private void BuildHollowBurrowPeek()
    {
        if(!IncludeScenery)return;
        Bead(this,new(10.8f,-1.45f,Depth+.28f),new(1.10f,.34f,.68f),_art.Color(new("9b6a45"),.98f)).Name="HollowBurrowMound";
        _hollowMole=new Node3D{Name="HollowBurrowMole",Position=new(10.8f,-.89f,Depth+.40f)};AddChild(_hollowMole);
        var fur=_art.Toy(new("79563e"),.92f);var cream=_art.Toy(new("d2b68c"),.94f);
        Bead(_hollowMole,Vector3.Zero,new(.72f,.58f,.56f),fur).Name="HollowMoleHead";
        Bead(_hollowMole,new(0,-.045f,.26f),new(.31f,.20f,.20f),cream).Name="HollowMoleMuzzle";
        Bead(_hollowMole,new(0,.005f,.36f),new(.14f,.10f,.09f),_art.Toy(new("b67d68"),.9f));
        foreach(float x in new[]{-.16f,.16f})
        {
            Bead(_hollowMole,new(x,.12f,.235f),new(.115f,.13f,.065f),_art.Color(new("e9d8b7"),.95f));
            Bead(_hollowMole,new(x,.12f,.27f),new(.05f,.065f,.025f),_art.Color(new("352d26"),.95f));
        }
        foreach(float x in new[]{-.30f,.30f})Bead(_hollowMole,new(x,-.24f,.20f),new(.16f,.13f,.15f),cream);
    }

    private void ProcessHollowBurrowPeek()
    {
        if(_hollowMole is null)return;
        float phase=_fieldClock%24;
        float peek=Math.Clamp(1-HollowSmooth(6,8,phase)+HollowSmooth(22,24,phase),0,1);
        // Retraction passes behind the existing opaque cut face. The whole
        // creature stays below the lowest gameplay plane and has no collision.
        _hollowMole.Position=new(10.8f+PresentationMath.Sin(_fieldClock*.65f)*.025f,-.94f+peek*.05f,Depth-.30f+peek*.70f);
        _hollowMole.Basis=PresentationMath.Rotation(new(0,PresentationMath.Sin(_fieldClock*.45f)*.10f,0));
    }
}
