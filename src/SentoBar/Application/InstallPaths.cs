using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SentoBar;

internal static class InstallPaths
{
    internal const string RegistryKey=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SentoBar";
    internal const string PreviousForkKey=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NabyScape";

    internal static string? PreviousForkDirectory()
    {
        using var entry=Registry.CurrentUser.OpenSubKey(PreviousForkKey);
        if(entry?.GetValue("InstallLocation") is not string path || !Path.IsPathFullyQualified(path) ||
            (!string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)),"NabyScape",StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(Path.TrimEndingDirectorySeparator(path),Legacy,StringComparison.OrdinalIgnoreCase)))
            return null;
        string directory=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        for(var parent=new DirectoryInfo(directory);parent!=null;parent=parent.Parent)
            if(parent.Exists && (parent.Attributes&FileAttributes.ReparsePoint)!=0)return null;
        string executable=Path.Combine(directory,"NabyScape.exe");
        if(!File.Exists(executable) || (File.GetAttributes(executable)&FileAttributes.ReparsePoint)!=0)return null;
        var version=System.Diagnostics.FileVersionInfo.GetVersionInfo(executable);
        string profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NabyScape");
        string marker=Path.Combine(profile,"russian-build-initialized.txt");
        if(!File.Exists(marker) || (File.GetAttributes(profile)&FileAttributes.ReparsePoint)!=0 ||
            (File.GetAttributes(marker)&FileAttributes.ReparsePoint)!=0)return null;
        return KnownForkBuild(version.ProductName,version.ProductVersion)?directory:null;
    }

    internal static bool KnownForkBuild(string? product,string? informationalVersion)
    {
        if(product!="NabyScape" || string.IsNullOrEmpty(informationalVersion))return false;
        string version=informationalVersion.Split('+')[0];
        return Regex.IsMatch(version,@"\A(?:1\.(?:0|2)\.0(?:_b\.[1-9][0-9]*)?|1\.1\.[1-6]|2\.26\.1-ru\.[1-9][0-9]*)\z",RegexOptions.CultureInvariant);
    }

    internal static string Default=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","SentoBar");
    internal static string Legacy=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","LumaIsland");
    internal static string Current=>RegisteredDirectory??Default;
    internal static string? RegisteredDirectory
    {
        get
        {
            using var key=Registry.CurrentUser.OpenSubKey(RegistryKey);
            if(key?.GetValue("InstallLocation") is string path)
                try{return Validate(path);}catch(IOException){}catch(ArgumentException){}
            return null;
        }
    }
    internal static string Validate(string path)
    {
        if(string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\",StringComparison.Ordinal))throw new IOException("Choose a local folder with a full drive path.");
        string full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if(!string.Equals(Path.GetFileName(full),"SentoBar",StringComparison.OrdinalIgnoreCase))throw new IOException(Localization.Text("Choose a dedicated SentoBar folder, such as D:\\Apps\\SentoBar."));
        for(var directory=new DirectoryInfo(full);directory!=null;directory=directory.Parent)
            if(directory.Exists && (directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException(Localization.Text("Choose a folder outside symbolic links or junctions."));
        string profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SentoBar");
        if(string.Equals(full,profile,StringComparison.OrdinalIgnoreCase) || full.StartsWith(profile+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException(Localization.Text("This folder holds your personal data. Choose a different installation folder."));
        return full;
    }
    internal static string FromArguments(string[] args,string fallback)
    {
        int index=Array.IndexOf(args,"--install-dir");
        if(index<0)return Validate(fallback);
        if(index+1>=args.Length || args.Count(a=>a=="--install-dir")!=1)throw new IOException("--install-dir needs one installation folder.");
        return Validate(args[index+1]);
    }
    internal static bool OwnsExecutable(string path)
    {
        string full=Path.GetFullPath(path);
        return string.Equals(full,Path.Combine(Current,"SentoBar.exe"),StringComparison.OrdinalIgnoreCase);
    }
}
