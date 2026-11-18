using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SentoBar;

internal static class ShortcutDropDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        var s=app.Settings;
        s.Preset="Mac";s.CompactLayout=s.HoverPeek=s.HoverFollowsWidgets=s.ReduceMotion=true;
        s.HideFullscreen=s.HideMaximized=s.TwoStageHover=s.SmartActivities=s.DropReveal=false;
        s.Widgets.Single(widget=>widget.Id=="shortcuts").Enabled=true;
        s.Tools.Single(tool=>tool.Id=="files").Enabled=true;
        app.ChangeSettings();
        island.DiagnosticPointerPresent=true;

        var file=Path.Combine(directory,"Example document.txt");
        var folder=Directory.CreateDirectory(Path.Combine(directory,"Example folder")).FullName;
        File.WriteAllText(file,"Shortcut fixture");
        int previousShelf=app.Productivity.Shelf.Count;
        int previousShortcuts=s.Shortcuts.Count;

        DragEventArgs SendDrag(RoutedEvent action,params string[] paths)
        {
            var args=(DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,
                null,new object[]{new DataObject(DataFormats.FileDrop,paths),DragDropKeyStates.None,DragDropEffects.Copy,island,new Point()},null)!;
            args.RoutedEvent=action;
            island.RaiseEvent(args);
            return args;
        }
        bool VisibleButton(string id)=>Diagnostics.Descendants(island).OfType<Button>().Any(b=>b.IsVisible && UI.Identity(b)==id);

        island.Collapse();island.PreviewActivity();island.SelectPeek("shortcuts");
        await Task.Delay(100);
        check("Mini shortcuts offers a direct Add shortcut action",VisibleButton("Add shortcut"));
        var enter=SendDrag(DragDrop.DragEnterEvent,file);
        check("Mini shortcuts accepts a file without switching to the shelf",
            enter.Handled && enter.Effects==DragDropEffects.Copy && island.PeekPage=="shortcuts" &&
            Diagnostics.Descendants(island).OfType<TextBlock>().Any(t=>t.IsVisible && t.Text==Localization.Text("Drop to add shortcuts")));
        SendDrag(DragDrop.DropEvent,file,file);
        await Task.Delay(100);
        check("A mini drop adds a single launcher entry and keeps the mini panel open",
            island.IsPeeking && island.PeekPage=="shortcuts" && s.Shortcuts.Count==previousShortcuts+1 &&
            s.Shortcuts[0].Target==file && app.Productivity.Shelf.Count==previousShelf);

        island.Reveal(false);island.SelectPage("access",false);
        await Task.Delay(100);
        check("Expanded shortcuts also offers Add shortcut",VisibleButton("Add shortcut"));
        SendDrag(DragDrop.DragEnterEvent,folder);
        SendDrag(DragDrop.DropEvent,folder,file);
        check("Expanded shortcuts saves the folder and deduplicates the existing file",
            island.ActivePage=="access" && s.Shortcuts.Count==previousShortcuts+2 &&
            app.Store.Load<Settings>("settings").Shortcuts[0].Target==folder && app.Productivity.Shelf.Count==previousShelf);
        check("Dropping preserves the source files",File.Exists(file) && Directory.Exists(folder));

        s.DropReveal=true;app.ChangeSettings();
        island.Collapse();island.PreviewActivity();island.SelectPeek("files");
        SendDrag(DragDrop.DragEnterEvent,file);
        SendDrag(DragDrop.DropEvent,file);
        check("Dropping onto the shelf still stages files",app.Productivity.Shelf.Any(entry=>entry.Path==file) &&
            s.Shortcuts.Count==previousShortcuts+2);

        island.Collapse();island.PreviewActivity();island.SelectPeek("shortcuts");await Task.Delay(100);
        var add=Diagnostics.Descendants(island).OfType<Button>().Single(b=>b.IsVisible && UI.Identity(b)=="Add shortcut");
        add.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Task.Delay(100);
        var settings=Application.Current.Windows.OfType<SettingsWindow>().Single();
        check("The Add shortcut action goes directly to shortcut management",
            Diagnostics.LogicalDescendants(settings).OfType<Button>().Any(b=>UI.Identity(b)=="Add installed app"));
        settings.Close();
        island.DiagnosticPointerPresent=false;
    }
}
