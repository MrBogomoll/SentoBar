using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace NabyScape;
public sealed partial class OverlayWindow
{
    private readonly StackPanel peekClipboard=new(),peekTasks=new();
    private string clipboardPeekKey="",tasksPeekKey="";
    private FrameworkElement BuildToolPeek(string id)
    {
        bool clipboard=id=="clips";
        var heading=UI.Heading(Localization.Text(clipboard?"Clipboard":"Tasks"),13);
        heading.MinHeight=23;
        var open=GlassPeekButton("Open all "+id,clipboard?"Open clipboard":"All tasks",()=>ShowTools(id));
        open.FontSize=11;
        open.HorizontalAlignment=HorizontalAlignment.Center;
        open.Margin=new(0,4,0,0);
        if(clipboard)
        {
            var page=new StackPanel();
            page.Children.Add(heading);page.Children.Add(peekClipboard);page.Children.Add(open);
            return page;
        }

        var tasksPage=new Grid();
        tasksPage.RowDefinitions.Add(new(){Height=GridLength.Auto});
        tasksPage.RowDefinitions.Add(new());
        tasksPage.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var list=UI.Scroll(peekTasks);
        list.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        list.PanningMode=PanningMode.VerticalOnly;
        UI.LocalizedName(list,"Task list");
        Grid.SetRow(list,1);
        Grid.SetRow(open,2);
        tasksPage.Children.Add(heading);tasksPage.Children.Add(list);tasksPage.Children.Add(open);
        return tasksPage;
    }
    private void UpdateToolPeeks()
    {
        if(!peekInitialized)return;
        var p=app.Productivity;
        string clips=p.ClipboardEnabled?string.Join("|",p.Clips.Take(2).Select(c=>c.Id))+"|"+p.ClipboardPaused:"off";
        if(peekId=="clips" && clips!=clipboardPeekKey)
        {
            clipboardPeekKey=clips;peekClipboard.Children.Clear();
            if(!p.ClipboardEnabled)peekClipboard.Children.Add(UI.Text(Localization.Text("History is off. Enable it in Settings → Controls."),11,UI.Muted));
            else if(p.Clips.Count==0)peekClipboard.Children.Add(UI.Text(Localization.Text("Nothing captured yet"),11,UI.Muted));
            else foreach(var clip in p.Clips.Take(2))
            {
                var row=new DockPanel{Height=28};
                if(clip.Kind=="image"){var image=new Image{Source=p.LoadClipImage(clip,(int)Math.Ceiling(24*VisualTreeHelper.GetDpi(this).DpiScaleX)),Width=24,Height=24,Stretch=Stretch.Uniform,Margin=new(0,0,8,0)};row.Children.Add(image);}
                var line=UI.Text(clip.Label,12);line.TextWrapping=TextWrapping.NoWrap;line.TextTrimming=TextTrimming.CharacterEllipsis;line.ToolTip=clip.Kind=="image"?Localization.Text("Image"):clip.Label;row.Children.Add(line);peekClipboard.Children.Add(row);
            }
        }
        string tasks=string.Join("|",p.Tasks.Select(t=>t.Id+":"+t.Text));
        if(peekId=="tasks" && (tasks!=tasksPeekKey || peekTasks.Children.Count==0))
        {
            tasksPeekKey=tasks;peekTasks.Children.Clear();
            foreach(var task in p.Tasks)
            {
                var label=UI.Text(task.Text,12);label.TextWrapping=TextWrapping.NoWrap;label.TextTrimming=TextTrimming.CharacterEllipsis;
                var toggle=new CheckBox{Content=label,Tag=task,IsChecked=task.Done,Style=(Style)FindResource("TaskToggle"),Height=24,Margin=new(0)};UI.Named(toggle,Localization.Text("Preview task ")+task.Id);
                toggle.Click+=(_,_)=>{if(!p.SetTaskDone(task.Id,toggle.IsChecked==true))toggle.IsChecked=task.Done;};peekTasks.Children.Add(toggle);
            }
            if(p.Tasks.Count==0)peekTasks.Children.Add(UI.Text(Localization.Text("No tasks yet. Add one in Tools → Tasks."),11,UI.Muted));
        }
        foreach(var toggle in peekTasks.Children.OfType<CheckBox>())if(toggle.Tag is TaskItem task){toggle.IsChecked=task.Done;((TextBlock)toggle.Content).TextDecorations=task.Done?TextDecorations.Strikethrough:null;}
        for(int i=0;i<peekClipboard.Children.Count;i++)peekClipboard.Children[i].Visibility=i==0 || peekDetailed?Visibility.Visible:Visibility.Collapsed;
    }
}
