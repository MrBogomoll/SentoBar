using System;

namespace NabyScape;

public sealed record PanelBackdropPreferences
{
    internal static readonly string[] Gradients={"No gradient","Linear gradient","Radial gradient","Two soft lights"};
    internal static readonly string[] Positions={"Top left","Top","Top right","Left","Center","Right","Bottom left","Bottom","Bottom right"};
    internal static readonly string[] Images={"No image","Custom picture","Windows wallpaper"};

    public bool Enabled {get;set;}
    public double Opacity {get;set;}=.88;
    public string Gradient {get;set;}="Two soft lights";
    public string FirstColor {get;set;}="#345F9E";
    public string SecondColor {get;set;}="#AD8930";
    public string FirstPosition {get;set;}="Bottom left";
    public string SecondPosition {get;set;}="Bottom right";
    public double Strength {get;set;}=.65;
    public string Image {get;set;}="No image";
    public double ImageOpacity {get;set;}=.5;
    public double Blur {get;set;}=12;

    internal void Normalize()
    {
        if(Array.IndexOf(Gradients,Gradient)<0)Gradient="No gradient";
        if(Array.IndexOf(Images,Image)<0)Image="No image";
        if(Array.IndexOf(Positions,FirstPosition)<0)FirstPosition="Bottom left";
        if(Array.IndexOf(Positions,SecondPosition)<0)SecondPosition="Bottom right";
        if(!Settings.ValidColor(FirstColor))FirstColor="#345F9E";
        if(!Settings.ValidColor(SecondColor))SecondColor="#AD8930";
        Opacity=Bound(Opacity,.88,1);Strength=Bound(Strength,.65,1);
        ImageOpacity=Bound(ImageOpacity,.5,1);Blur=Bound(Blur,12,32);
    }
    private static double Bound(double value,double fallback,double max)=>double.IsFinite(value)?Math.Clamp(value,0,max):fallback;
}

public sealed partial class Settings
{
    public PanelBackdropPreferences PanelBackdrop {get;set;}=new();
}
