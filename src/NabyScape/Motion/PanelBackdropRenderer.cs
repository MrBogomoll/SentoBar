using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal sealed class PanelBackdropRenderer
{
    private const string Picture="panel-background.png";
    private const int ImageEdge=1000;
    private (string Path,long Stamp)? decodedKey;
    private BitmapSource? decoded,blurred;
    private double blurRadius=-1;
    private (PanelBackdropPreferences Options,string Base,(string Path,long Stamp)? File)? paintKey;
    private PanelBackdropPaint? paint;

    internal static bool HasPicture(string directory)=>File.Exists(Path.Combine(directory,Picture));

    internal static void ImportPicture(string directory,string path)
    {
        using var input=File.OpenRead(path);
        var image=Decode(input);
        Directory.CreateDirectory(directory);
        string temporary=Path.Combine(directory,"panel-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            var encoder=new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))encoder.Save(output);
            File.Move(temporary,Path.Combine(directory,Picture),true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }

    private static BitmapSource Decode(Stream input)
    {
        if(input.Length>32*1024*1024)throw new InvalidDataException(Localization.Text("Choose a background picture under 32 MB."));
        var decoder=BitmapDecoder.Create(input,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
        var frame=decoder.Frames[0];
        if((long)frame.PixelWidth*frame.PixelHeight>60_000_000)throw new InvalidDataException(Localization.Text("The background picture is too large."));
        input.Position=0;
        return DisplayThumbnail.Decode(input,ImageEdge);
    }

    internal PanelBackdropPaint? Get(Settings settings,string directory)
    {
        var options=settings.PanelBackdrop;
        if(!options.Enabled || Accessibility.HighContrast)return null;
        var file=ImageFile(options.Image,directory);
        var key=(options,settings.Background,file);
        if(paintKey is {} previous && previous==key)return paint;
        BitmapSource? image=null;
        if(file is {} source && options.ImageOpacity>0)
        {
            if(decodedKey!=source)
            {
                decodedKey=source;decoded=null;blurred=null;blurRadius=-1;
                try{using var input=File.OpenRead(source.Path);decoded=Decode(input);}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException){}
            }
            if(decoded!=null && blurRadius!=options.Blur)
            {
                blurRadius=options.Blur;
                blurred=options.Blur>0?Blur(decoded,options.Blur):decoded;
            }
            image=blurred;
        }
        paintKey=(options with{},settings.Background,file);
        return paint=new PanelBackdropPaint(UI.Brush(settings.Background),options,image);
    }

    private static (string Path,long Stamp)? ImageFile(string mode,string directory)
    {
        string path=mode switch{"Custom picture"=>Path.Combine(directory,Picture),"Windows wallpaper"=>WallpaperPath(),_=>""};
        if(path.Length==0)return null;
        try
        {
            var file=new FileInfo(path);
            return file.Exists?(path,file.LastWriteTimeUtc.Ticks):null;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException){return null;}
    }

    private static string WallpaperPath()
    {
        var path=new StringBuilder(1024);
        return SystemParametersInfo(0x0073,(uint)path.Capacity,path,0)?path.ToString():"";
    }

    [DllImport("user32.dll",CharSet=CharSet.Unicode)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action,uint parameter,StringBuilder value,uint flags);

    private static BitmapSource Blur(BitmapSource source,double radius)
    {
        int width=source.PixelWidth,height=source.PixelHeight,stride=width*4;
        int pixels=Math.Max(1,(int)Math.Round(radius));
        var input=new byte[height*stride];
        var buffer=new byte[input.Length];
        var premultiplied=new FormatConvertedBitmap(source,PixelFormats.Pbgra32,null,0);
        premultiplied.CopyPixels(input,stride,0);

        // Sliding sums keep cost independent of radius. Repeat the two passes
        // for a softer falloff and extend edge samples instead of padding with black.
        BoxPass(input,buffer,height,width,stride,4,pixels);
        BoxPass(buffer,input,width,height,4,stride,pixels);
        BoxPass(input,buffer,height,width,stride,4,pixels);
        BoxPass(buffer,input,width,height,4,stride,pixels);
        var result=BitmapSource.Create(width,height,96,96,PixelFormats.Pbgra32,null,input,stride);
        result.Freeze();
        return result;
    }

    private static void BoxPass(byte[] input,byte[] output,int lines,int samples,int lineStep,int step,int radius)
    {
        int count=radius*2+1;
        for(int line=0;line<lines;line++)
        for(int channel=0;channel<4;channel++)
        {
            int start=line*lineStep+channel;
            int sum=input[start]*(radius+1);
            for(int i=1;i<=radius;i++)sum+=input[start+Math.Min(samples-1,i)*step];
            for(int i=0;i<samples;i++)
            {
                output[start+i*step]=(byte)((sum+count/2)/count);
                sum+=input[start+Math.Min(samples-1,i+radius+1)*step]-input[start+Math.Max(0,i-radius)*step];
            }
        }
    }
}

internal sealed class PanelBackdropPaint
{
    private readonly Brush basis;
    private readonly ImageBrush? image;
    private readonly Brush? first,second;
    private readonly double opacity;

    internal PanelBackdropPaint(Brush basis,PanelBackdropPreferences options,BitmapSource? bitmap)
    {
        this.basis=basis;
        opacity=options.Opacity;
        if(bitmap!=null)
        {
            image=new ImageBrush(bitmap){Stretch=Stretch.UniformToFill,Opacity=options.ImageOpacity};
            image.Freeze();
        }
        var a=At(options.FirstPosition);
        var b=At(options.SecondPosition);
        Color start=WithOpacity(UI.Brush(options.FirstColor).Color,options.Strength);
        Color end=WithOpacity(UI.Brush(options.SecondColor).Color,options.Strength);
        if(options.Gradient=="Linear gradient")
        {
            if(a==b)b=a.X<.5?new Point(1,a.Y):new Point(0,a.Y);
            first=new LinearGradientBrush(start,end,a,b);
        }
        else if(options.Gradient is "Radial gradient" or "Two soft lights")
        {
            first=Glow(start,a);
            if(options.Gradient=="Two soft lights")second=Glow(end,b);
        }
        first?.Freeze();second?.Freeze();
    }

    private static Color WithOpacity(Color color,double value)=>Color.FromArgb((byte)Math.Round(255*value),color.R,color.G,color.B);
    private static Brush Glow(Color color,Point spot)=>new RadialGradientBrush(color,WithOpacity(color,0))
        {Center=spot,GradientOrigin=spot,RadiusX=.77,RadiusY=.9};
    private static Point At(string spot)=>spot switch
    {
        "Top left"=>new(0,0),"Top"=>new(.5,0),"Top right"=>new(1,0),
        "Left"=>new(0,.5),"Right"=>new(1,.5),
        "Bottom left"=>new(0,1),"Bottom"=>new(.5,1),"Bottom right"=>new(1,1),_=>new(.5,.5)
    };

    internal void Draw(DrawingContext context,Geometry contour,double? opacityOverride=null)
    {
        context.PushOpacity(opacityOverride??opacity);
        context.DrawGeometry(basis,null,contour);
        if(image!=null)context.DrawGeometry(image,null,contour);
        if(first!=null)context.DrawGeometry(first,null,contour);
        if(second!=null)context.DrawGeometry(second,null,contour);
        context.Pop();
    }

    internal Brush ForSize(double width,double height)
    {
        var drawing=new DrawingGroup();
        using(var context=drawing.Open())Draw(context,new RectangleGeometry(new Rect(0,0,Math.Max(1,width),Math.Max(1,height))));
        drawing.Freeze();
        var brush=new DrawingBrush(drawing);brush.Freeze();return brush;
    }
}
