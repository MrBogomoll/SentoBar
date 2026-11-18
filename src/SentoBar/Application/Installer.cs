using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SentoBar;

internal static class Installer
{
    internal static string InstallDirectory=>InstallPaths.Current;
    internal static string ShortcutPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"SentoBar.lnk");
    private const string UninstallKey=InstallPaths.RegistryKey;
    public static void Run(string[] args)
    {
        bool silent=args.Contains("--silent");
        if(silent)
        {
            try {string target=InstallPaths.FromArguments(args,InstallDirectory);Install(target);if(args.Contains("--launch"))Launch(target);}
            catch(Exception ex) {File.WriteAllText(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!,"install-error.txt"),ex.ToString());Environment.ExitCode=1;}
            return;
        }
        var application=new Application();application.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/SentoBar;component/Theme.xaml",UriKind.Relative)});
        application.Run(CreateWindow(args));
    }
    internal static Window CreateWindow(string[] args)
    {
        var window=new Window{Title=Localization.Text("Install SentoBar · ")+RuntimePlatform.Rid,Width=680,Height=640,MinWidth=540,MinHeight=500,ResizeMode=ResizeMode.CanResize,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        window.Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/island.ico"));window.SourceInitialized+=(_,_)=>Native.DarkTitleBar(window);
        var panel=new StackPanel{Margin=new(36)};panel.Children.Add(UI.Text("SentoBar",36,UI.Brush("#B4F272")));panel.Children.Add(UI.Text(Localization.Text("A little space. More possibilities."),15,UI.Muted));
        panel.Children.Add(new Border{Height=26});panel.Children.Add(UI.Text(Localization.Text("Music, focus, and your everyday tools — always close.\n\nInstalls for this Windows user only. Choose your folder and startup preference below."),14));
        var folderLabel=UI.Text(Localization.Text("Install folder"),12,UI.Muted);folderLabel.Margin=new(0,20,0,6);panel.Children.Add(folderLabel);
        string suggested=InstallDirectory;
        var folder=new TextBox{Text=InstallPaths.FromArguments(args,suggested),MinHeight=36,VerticalContentAlignment=VerticalAlignment.Center};UI.Named(folder,Localization.Text("Installation folder"));
        var row=new DockPanel{Margin=new(0,0,0,6)};
        var browse=UI.Button(Localization.Text("Browse…"),()=>
        {
            var picker=new OpenFolderDialog{Title=Localization.Text("Choose where to install SentoBar")};
            if(picker.ShowDialog(window)==true)folder.Text=string.Equals(Path.GetFileName(picker.FolderName),"SentoBar",StringComparison.OrdinalIgnoreCase)?picker.FolderName:Path.Combine(picker.FolderName,"SentoBar");
        });DockPanel.SetDock(browse,Dock.Right);row.Children.Add(browse);row.Children.Add(folder);panel.Children.Add(row);
        var state=UI.Text(Localization.Text("Choose a location you can write to. Your personal settings are kept separately."),11,UI.Muted);state.Margin=new(0,0,0,12);panel.Children.Add(state);
        var startupChoice=new CheckBox{Content=Localization.Text("Start SentoBar with Windows"),IsChecked=HasSavedPreferences?StartupService.Enabled():FactoryDefaults.Create().AutoStart,Margin=new(0,0,0,8)};panel.Children.Add(startupChoice);
        var launch=new CheckBox{Content=Localization.Text("Open SentoBar after installation"),IsChecked=true};panel.Children.Add(launch);
        var button=UI.Button(Localization.Text("Install SentoBar"),()=>{},true);button.Click+=(_,_)=>
        {
            button.IsEnabled=false;
            try {string target=InstallPaths.Validate(folder.Text.Trim());Install(target,startupChoice.IsChecked==true);if(launch.IsChecked==true)Launch(target);window.Close();}
            catch(Exception ex) {state.Text=Localization.Text("Installation failed: ")+ex.Message;button.IsEnabled=true;}
        };panel.Children.Add(button);window.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};return window;
    }
    private static void Launch(string folder)=>Process.Start(new ProcessStartInfo(Path.Combine(folder,"SentoBar.exe")){UseShellExecute=true});
    internal static void Install(string? directory=null,bool? startWithWindows=null)
    {
        ValidateArchitecture(Environment.ProcessPath??throw new IOException(Localization.Text("Installer executable is unavailable.")));
        string previous=InstallDirectory;string target=InstallPaths.Validate(directory??previous);
        string? fork=InstallPaths.PreviousForkDirectory();
        if(fork!=null)
        {
            foreach(var process in Process.GetProcessesByName("NabyScape"))
            {
                using(process)
                {
                    try
                    {
                        if(string.Equals(process.MainModule?.FileName,Path.Combine(fork,"NabyScape.exe"),StringComparison.OrdinalIgnoreCase))
                            throw new IOException(Localization.Text("Close the previous installation before updating to SentoBar."));
                    }
                    catch(System.ComponentModel.Win32Exception){}
                    catch(InvalidOperationException){}
                }
            }
            _=new Store();
        }
        foreach(string processName in new[]{"SentoBar","SentoBar.UI","SentoBar.Core"})
        foreach(var process in Process.GetProcessesByName(processName))
        {
            using(process)
            {
                try {if(process.Id!=Environment.ProcessId && new[]{target,previous}.Any(folder=>new[]{"SentoBar.exe","SentoBar.UI.exe","SentoBar.Core.exe"}.Any(name=>string.Equals(process.MainModule?.FileName,Path.Combine(folder,name),StringComparison.OrdinalIgnoreCase))))throw new IOException(Localization.Text("Quit SentoBar from the system tray before updating."));}
                catch(System.ComponentModel.Win32Exception) { }
            }
        }
        Directory.CreateDirectory(target);
        string source=Environment.ProcessPath!;string executable=Path.Combine(target,"SentoBar.exe");
        if(!Path.GetFileName(source).StartsWith("SentoBar",StringComparison.OrdinalIgnoreCase))throw new IOException(Localization.Text("Use SentoBar-Setup.exe to install the app."));
        if(!string.Equals(source,executable,StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(source,executable+".new",true);File.Move(executable+".new",executable,true);
        }
        RemoveRetiredHostFiles(target,source);
        string installedIcon=WriteIcon(target);
        File.WriteAllText(Path.Combine(target,"Uninstall.ps1"),Uninstaller,new System.Text.UTF8Encoding(true));
        foreach(var name in new[]{"LICENSE","LUCIDE-LICENSE.txt","MANROPE-OFL.txt","NAudio-LICENSE.txt","System.Speech-LICENSE.txt","DOTNET-LICENSE.txt","DOTNET-ThirdPartyNotices.txt","ROMAJI-ThirdPartyNotices.txt","LYRICS-LANGUAGE-LICENSE.txt"})
        {
            using var license=typeof(Installer).Assembly.GetManifestResourceStream("SentoBar."+name);
            if(license!=null){using var reader=new StreamReader(license);File.WriteAllText(Path.Combine(target,name),reader.ReadToEnd());}
        }
        using var startup=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        bool previousStartup=fork!=null && (startup.GetValue("NabyScape") as string)?.StartsWith("\""+Path.Combine(fork,"NabyScape.exe")+"\"",StringComparison.OrdinalIgnoreCase)==true;
        object? shell=null,shortcut=null;
        try
        {
            var type=Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException(Localization.Text("The Windows shortcut service is unavailable."));
            shell=Activator.CreateInstance(type);dynamic dynamicShell=shell!;shortcut=dynamicShell.CreateShortcut(ShortcutPath);dynamic link=shortcut;
            link.TargetPath=executable;link.WorkingDirectory=target;link.Description=Localization.Text("SentoBar for Windows");link.IconLocation=installedIcon+",0";link.Save();
            object uninstallLink=dynamicShell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Uninstall SentoBar.lnk"));
            try{dynamic uninstall=uninstallLink;uninstall.TargetPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");uninstall.Arguments="-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(target,"Uninstall.ps1")+"\"";uninstall.WorkingDirectory=target;uninstall.Description=Localization.Text("Remove SentoBar and optionally all downloaded speech models and personal data");uninstall.IconLocation=installedIcon+",0";uninstall.Save();}
            finally{if(Marshal.IsComObject(uninstallLink))Marshal.FinalReleaseComObject(uninstallLink);}

        }
        finally {if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
        using var key=Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName","SentoBar");key.SetValue("DisplayVersion",AppVersion.Display);key.SetValue("Publisher","SentoBar");key.SetValue("InstallLocation",target);key.SetValue("DisplayIcon",installedIcon);
        key.SetValue("UninstallString","powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \""+Path.Combine(target,"Uninstall.ps1")+"\"");
        key.SetValue("QuietUninstallString","powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \""+Path.Combine(target,"Uninstall.ps1")+"\" -Quiet");
        key.SetValue("NoModify",1,RegistryValueKind.DWord);key.SetValue("NoRepair",1,RegistryValueKind.DWord);key.SetValue("EstimatedSize",(int)(new FileInfo(executable).Length/1024),RegistryValueKind.DWord);
        // Carry the fork's autostart choice into the renamed installation.
        bool startupEnabled=startWithWindows??(StartupService.Enabled() || previousStartup || !HasSavedPreferences && FactoryDefaults.Create().AutoStart);
        if(startupEnabled)
        {
            startup.SetValue("SentoBar","\""+executable+"\" --background");
        }
        else startup.DeleteValue("SentoBar",false);
        if(fork!=null)
        {
            if(previousStartup)startup.DeleteValue("NabyScape",false);
            RetirePreviousFork(fork);
        }

        if(!string.Equals(previous,target,StringComparison.OrdinalIgnoreCase))RemovePreviousFiles(previous,source);
    }
    private static void RetirePreviousFork(string directory)
    {
        var type=Type.GetTypeFromProgID("WScript.Shell");
        if(type!=null && Activator.CreateInstance(type) is {} shell)
        {
            try
            {
                dynamic wsh=shell;
                foreach(string name in new[]{"NabyScape.lnk","Uninstall NabyScape.lnk","Удаление NabyScape.lnk"})
                {
                    string shortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),name);
                    if(!File.Exists(shortcut))continue;
                    object instance=wsh.CreateShortcut(shortcut);
                    try
                    {
                        dynamic link=instance;
                        bool appLink=string.Equals((string)link.TargetPath,Path.Combine(directory,"NabyScape.exe"),StringComparison.OrdinalIgnoreCase);
                        bool uninstallLink=((string)link.Arguments).Contains("\""+Path.Combine(directory,"Uninstall.ps1")+"\"",StringComparison.OrdinalIgnoreCase);
                        if(appLink || uninstallLink)File.Delete(shortcut);
                    }
                    finally{Marshal.FinalReleaseComObject(instance);}
                }
            }
            finally{Marshal.FinalReleaseComObject(shell);}
        }
        if(string.Equals(InstallPaths.PreviousForkDirectory(),directory,StringComparison.OrdinalIgnoreCase))
            Registry.CurrentUser.DeleteSubKeyTree(InstallPaths.PreviousForkKey,false);
    }
    // Only migration cleanup: the application no longer contains a separate renderer/worker.
    internal static string WriteIcon(string folder)
    {
        string target=Path.Combine(folder,"SentoBar.ico");
        using var stream=typeof(Installer).Assembly.GetManifestResourceStream("SentoBar.g.resources")??throw new IOException("Missing application resources.");
        using var reader=new System.Resources.ResourceReader(stream);
        foreach(System.Collections.DictionaryEntry entry in reader)
            if(entry.Key is string key && key=="assets/island.ico" && entry.Value is Stream icon)
            {using var output=File.Create(target);icon.Position=0;icon.CopyTo(output);return target;}
        throw new IOException("Missing application icon.");
    }
    internal static readonly string[] RetiredHostFiles={"SentoBar.UI.exe","SentoBar.Core.exe","SentoBar.ico","NATIVE-ThirdPartyNotices.txt","RUST-Library-COPYRIGHT.html"};
    internal static void RemoveRetiredHostFiles(string folder,string source)
    {
        foreach(string name in RetiredHostFiles)
        {string path=Path.Combine(folder,name);if(!string.Equals(path,source,StringComparison.OrdinalIgnoreCase)&&File.Exists(path))File.Delete(path);}
    }
    private static void RemovePreviousFiles(string folder,string source)
    {
        RemoveRetiredHostFiles(folder,source);
        // Moving an installation removes only shipped files, never unrelated files.
        foreach(string name in new[]{"SentoBar.exe","Uninstall.ps1","LICENSE","LUCIDE-LICENSE.txt","MANROPE-OFL.txt","NAudio-LICENSE.txt","System.Speech-LICENSE.txt","DOTNET-LICENSE.txt","DOTNET-ThirdPartyNotices.txt","ROMAJI-ThirdPartyNotices.txt","LYRICS-LANGUAGE-LICENSE.txt"})
        {
            string path=Path.Combine(folder,name);if(string.Equals(path,source,StringComparison.OrdinalIgnoreCase))continue;
            try{if(File.Exists(path))File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        try{if(Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
    private static void ValidateArchitecture(string source)
    {
        var process=RuntimeInformation.ProcessArchitecture;var system=RuntimeInformation.OSArchitecture;
        if(process is not (Architecture.X64 or Architecture.Arm64) || system is not (Architecture.X64 or Architecture.Arm64) || process==Architecture.Arm64 && system!=Architecture.Arm64)
            throw new PlatformNotSupportedException(Localization.Text("Use the SentoBar setup matching your Windows architecture (x64 or ARM64)."));
        string name=Path.GetFileName(source);
        foreach(string rid in new[]{"win-x64","win-arm64"})
            if(name.Contains(rid,StringComparison.OrdinalIgnoreCase) && rid!=RuntimePlatform.Rid)throw new IOException(Localization.Text("The setup filename does not match its executable architecture. Download the correct official setup."));
        using var stream=File.OpenRead(source);using var reader=new BinaryReader(stream);
        if(stream.Length<64 || reader.ReadUInt16()!=0x5A4D)throw new IOException(Localization.Text("Invalid setup executable."));
        stream.Position=0x3C;int offset=reader.ReadInt32();
        if(offset<64 || offset>stream.Length-6)throw new IOException(Localization.Text("Invalid setup executable header."));
        stream.Position=offset;
        if(reader.ReadUInt32()!=0x00004550 || reader.ReadUInt16()!=RuntimePlatform.PeMachine)throw new IOException(Localization.Text("Setup architecture verification failed. Download the correct official setup."));
    }
    internal static string Uninstaller
    {
        get
        {
            using var stream=typeof(Installer).Assembly.GetManifestResourceStream("SentoBar.Uninstall.ps1")??throw new IOException(Localization.Text("The bundled uninstaller is missing."));
            using var reader=new StreamReader(stream);return reader.ReadToEnd();
        }
    }
    internal static bool HasSavedPreferences=>File.Exists(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SentoBar","settings.json"));
    internal static void OpenUninstaller()
    {
        if(!OfficialUpdates.IsInstalled(Environment.ProcessPath!))throw new IOException(Localization.Text("This is a portable copy. Remove its folder, or use Windows Installed apps for the installed copy."));
        string script=Path.Combine(InstallDirectory,"Uninstall.ps1");
        InstallPaths.Validate(InstallDirectory);
        if(!File.Exists(script) || (File.GetAttributes(script)&FileAttributes.ReparsePoint)!=0)throw new IOException(Localization.Text("Run the latest installer to repair the uninstall entry."));
        _=Process.Start(new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"-NoProfile","-ExecutionPolicy","Bypass","-File",script}})??throw new IOException(Localization.Text("Could not open the uninstaller."));
    }
}
