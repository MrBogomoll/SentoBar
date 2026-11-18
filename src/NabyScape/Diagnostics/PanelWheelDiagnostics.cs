using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace NabyScape;
internal static class PanelWheelDiagnostics
{
    internal static async Task Run(App app,OverlayWindow dock,Action<string,bool> check)
    {
        var s=app.Settings;s.CompactLayout=s.PanelSwipe=s.WheelWrapTabs=true;s.ReduceMotion=true;s.SmartActivities=false;
        s.HideFullscreen=s.HideMaximized=s.AlwaysTucked=false;s.PanelWidth=526;s.PanelMaxHeight=417;s.PanelScale=1.04;s.PanelPadding=32;
        foreach(var w in s.Widgets)w.Enabled=w.Id is "media" or "notes" or "tasks";
        app.Productivity.Tasks.Clear();app.Productivity.Note="";app.ChangeSettings();
        async Task Open(string id){dock.ShowTools(id,false);await Task.Delay(100);dock.UpdateLayout();}
        bool Wheel(DependencyObject source,bool horizontal=false)
        {
            ((PeekWheelAccumulator)typeof(OverlayWindow).GetField("panelWheel",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(dock)!).Reset();
            string before=dock.ActivePage;return dock.RoutePanelWheel(-120,horizontal,source) && dock.ActivePage!=before;
        }
        TextBox Editor()=>Diagnostics.Descendants(dock).OfType<TextBox>().First(t=>t.IsVisible && t.AcceptsReturn);
        await Open("notes");var note=Editor();Keyboard.ClearFocus();
        check("Hovering an unfocused note allows panel navigation",Wheel(note));
        await Open("notes");var title=Diagnostics.Descendants(dock).OfType<TextBox>().First(t=>t.IsVisible && !t.AcceptsReturn);title.Focus();
        check("Single-line title does not trap the wheel even when focused",Wheel(title));
        await Open("notes");note=Editor();Keyboard.ClearFocus();note.ApplyTemplate();
        var inside=Diagnostics.Descendants(note).OfType<ScrollViewer>().First();
        check("Text input template descendants do not create a wheel dead zone",Wheel(inside));
        await Open("notes");note=Editor();Keyboard.ClearFocus();
        check("The area beside the note input allows navigation at the user's panel size",Wheel((DependencyObject)note.Parent));
        await Open("notes");note=Editor();Keyboard.ClearFocus();string previous=dock.ActivePage;
        ((PeekWheelAccumulator)typeof(OverlayWindow).GetField("panelWheel",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(dock)!).Reset();
        var routed=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=Mouse.PreviewMouseWheelEvent};note.RaiseEvent(routed);
        check("Real routed wheel input over a note reaches page navigation",routed.Handled && dock.ActivePage!=previous);
        await Open("notes");note=Editor();note.Focus();
        check("A short focused note with nothing to scroll permits navigation",Wheel(note));
        await Open("tasks");var task=Diagnostics.Descendants(dock).OfType<TextBox>().First(t=>t.IsVisible);task.Focus();
        check("Task input allows wheel navigation",Wheel(task));
        await Open("tasks");var empty=Diagnostics.Descendants(dock).OfType<ScrollViewer>().First(v=>v.Tag as string=="panel-content-scroll");
        check("An empty task list does not trap nearby wheel input",empty.ScrollableHeight<1 && Wheel(empty));
        for(int i=1;i<=7;i++)app.Productivity.AddTask($"Task {i}");
        await Open("tasks");
        var taskList=Diagnostics.Descendants(dock).OfType<ScrollViewer>().Single(v=>v.IsVisible && v.Tag as string=="panel-content-scroll");
        var taskEditor=Diagnostics.Descendants(dock).OfType<TextBox>().Single(t=>t.IsVisible);
        var taskViewport=Diagnostics.Descendants(dock).OfType<ScrollViewer>().First(v=>v.IsVisible && v.IsAncestorOf(taskList));
        double EditorBottom()=>taskEditor.TranslatePoint(new Point(0,taskEditor.ActualHeight),taskViewport).Y;
        check("The task composer stays fully visible with seven tasks at the user's panel size",
            taskEditor.ActualHeight>0 && EditorBottom()<=taskViewport.ViewportHeight+.5);
        for(int i=0;i<12;i++){dock.RoutePanelWheel(-120,false,(StackPanel)taskList.Content,true);dock.UpdateLayout();}
        await Task.Delay(80);
        check("Ctrl wheel reaches the last task while the composer stays visible",
            taskList.ScrollableHeight>1 && Math.Abs(taskList.VerticalOffset-taskList.ScrollableHeight)<1 &&
            dock.ActivePage=="tasks" && EditorBottom()<=taskViewport.ViewportHeight+.5);
        s.PanelMaxHeight=240;s.PanelScale=1.25;s.SectionGap=28;app.ChangeSettings();
        await Open("tasks");
        taskList=Diagnostics.Descendants(dock).OfType<ScrollViewer>().Single(v=>v.IsVisible && v.Tag as string=="panel-content-scroll");
        taskEditor=Diagnostics.Descendants(dock).OfType<TextBox>().Single(t=>t.IsVisible);
        taskViewport=Diagnostics.Descendants(dock).OfType<ScrollViewer>().First(v=>v.IsVisible && v.IsAncestorOf(taskList));
        taskList.ScrollToBottom();await Task.Delay(60);dock.UpdateLayout();
        dock.RoutePanelWheel(-120,false,(StackPanel)taskList.Content,true);await Task.Delay(60);dock.UpdateLayout();
        check($"The task composer fits at minimum height and large text scale ({EditorBottom():F1} / {taskViewport.ViewportHeight:F1})",
            taskEditor.ActualHeight>0 && EditorBottom()<=taskViewport.ViewportHeight+.5);
        s.PanelMaxHeight=417;s.PanelScale=1.04;s.SectionGap=18;app.ChangeSettings();
        await Open("notes");note=Editor();string draft=string.Join("\n",Enumerable.Range(1,80).Select(i=>"Строка заметки "+i));note.Text=draft;note.Focus();await Task.Delay(100);dock.UpdateLayout();
        string page=dock.ActivePage;
        check("Ctrl with the wheel scrolls a focused overflowing note",
            note.ExtentHeight>note.ViewportHeight+1 && dock.RoutePanelWheel(-120,false,note,true) && dock.ActivePage==page);
        await Task.Delay(100);dock.UpdateLayout();
        check("The note content actually scrolls",note.VerticalOffset>0);
        check("Plain wheel leaves a focused multiline editor",Wheel(note));
        check("Wheel navigation preserves the note draft",app.Productivity.Note==draft);
        await Open("notes");note=Editor();
        const string pending="Draft to save immediately before a wheel switch";
        note.Text=pending;
        check("Leaving an edited note by wheel still saves the pending draft",
            Wheel(note) && app.Store.Load<ProductivityData>("productivity").Note==pending && !app.Productivity.DraftNeedsSave);
        string document=System.IO.Path.Combine(app.Store.DirectoryPath,"productivity.json");
        DateTime lastWrite=System.IO.File.GetLastWriteTimeUtc(document);
        check("Switching away from an unchanged draft does not rewrite the productivity file",
            Wheel(dock) && System.IO.File.GetLastWriteTimeUtc(document)==lastWrite);
        var rows=new StackPanel();for(int i=0;i<30;i++)rows.Children.Add(new TextBlock{Text="Row "+i,Height=24});
        var list=new ScrollViewer{Content=rows,Height=96,Width=240,Tag="panel-content-scroll"};list.Measure(new Size(240,96));list.Arrange(new Rect(0,0,240,96));list.UpdateLayout();page=dock.ActivePage;
        check("Ctrl with the wheel scrolls any overflowing content list",list.ScrollableHeight>1 && dock.RoutePanelWheel(-120,false,rows.Children[0],true) && dock.ActivePage==page);
        await Task.Delay(60);list.UpdateLayout();
        check("Generic list scrolls without a widget exception",list.VerticalOffset>0);
        check("Plain wheel over an overflowing list changes the tab",Wheel(rows.Children[0]));
    }
}
