using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private readonly TextBlock awakeMark=UI.Text("●",9,UI.Brush("#B4F272"));
    private TextBlock? awakeStatus;
    private bool commandOpen,calendarEditing,calendarMonthExpanded;
    private TextBox? commandInput;
    private readonly StackPanel commandResults=new();
    private readonly List<Button> commandButtons=new();
    private DateTime calendarMonth=new(DateTime.Today.Year,DateTime.Today.Month,1),calendarDay=DateTime.Today;
    internal bool CommandsOpen=>commandOpen;
    private void CalendarChanged(){if(toolsMode=="calendar" && !calendarEditing)RefreshTools();}
    private void AwakeChanged(){UpdateRoadmapStatus(DateTimeOffset.UtcNow);if(toolsMode=="awake" && !utilityMutating && !commandOpen)RefreshTools();}
    private void UpdateRoadmapStatus(DateTimeOffset now)
    {
        if(app.Awake==null)return;
        RefreshCompactOptionalVisibility();
        if(awakeStatus!=null)Motion.Text(awakeStatus,AwakeSummary(now));
        if(peeking && peekId=="awake")refreshPeekAwake?.Invoke();
    }

    private string AwakeSummary(DateTimeOffset now)
    {
        if(!app.Awake.Active)return Localization.Text("Off · Windows power settings apply");
        string detail=app.Awake.Until is {} until
            ?Localization.Format("{0:0} min remaining",Math.Max(0,Math.Ceiling((until-now).TotalMinutes)))
            :Localization.Text("Until NabyScape quits");
        return Localization.Format("Awake · {0}\n",detail)+Localization.Text(app.Awake.KeepDisplayOn?"Display stays on":"Display can turn off");
    }

    private ComboBox CreateAwakeDuration()
    {
        var duration=new ComboBox{ItemsSource=new[]{"30 minutes","1 hour","2 hours","Until NabyScape quits"},SelectedIndex=0};
        UI.LocalizeOptions(duration);
        UI.Named(duration,Localization.Text("Keep awake duration"));
        return duration;
    }

    private void StartAwake(ComboBox duration,CheckBox display)
    {
        TimeSpan? length=duration.SelectedIndex switch
        {
            1=>TimeSpan.FromHours(1),
            2=>TimeSpan.FromHours(2),
            3=>null,
            _=>TimeSpan.FromMinutes(30)
        };
        app.Awake.Start(length,display.IsChecked==true,DateTimeOffset.UtcNow);
    }

    private void ResizeToolPanel()
    {body.UpdateLayout();Dimensions(true);if(expanded){motion.Go(surface,IslandSurface.SurfaceWidthProperty,expandedWidth,Math.Min(300,app.Settings.AnimationMs));motion.Go(surface,IslandSurface.SurfaceHeightProperty,expandedHeight,Math.Min(300,app.Settings.AnimationMs));}}
    internal void ShowCommands(bool activate=true)
    {
        LeaveAssistantPanel();FlushNotes();commandOpen=true;BuildBody();scroll.ScrollToTop();Reveal(activate);ResizeToolPanel();
        if(activate)Dispatcher.BeginInvoke(()=>commandInput?.Focus());
    }
    private void CloseCommands()
    {if(!commandOpen)return;commandOpen=false;BuildBody();ResizeToolPanel();if(expanded)scroll.Focus();}
    private FrameworkElement BuildCommandPage()
    {
        var panel=new StackPanel{HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new(3,0,3,0)};var heading=new DockPanel();var back=UI.Button(Localization.Text("Back"),CloseCommands);back.FontSize=11;back.MinHeight=28;back.Margin=new(8,0,0,0);DockPanel.SetDock(back,Dock.Right);heading.Children.Add(back);heading.Children.Add(UI.Heading(Localization.Text("Command bar"),18));panel.Children.Add(heading);
        commandInput=UI.Input(Localization.Text("Search commands"),"",Localization.Text("Search NabyScape actions and saved shortcuts. Enter runs the first match."));commandInput.Margin=new(0,14,0,12);commandInput.MaxLength=120;panel.Children.Add(commandInput);
        if(commandResults.Parent is Panel parent)parent.Children.Remove(commandResults);panel.Children.Add(commandResults);
        commandInput.TextChanged+=(_,_)=>RefreshCommands();
        commandInput.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter && commandButtons.Count>0){commandButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));e.Handled=true;}else if(e.Key==Key.Down && commandButtons.Count>0){commandButtons[0].Focus();e.Handled=true;}};
        RefreshCommands();return panel;
    }
    private IEnumerable<(string title,string detail,Action run)> Commands()
    {
        yield return (Localization.Text("AI companion"),Localization.Text("Chat, voice and reviewed actions · configure your own provider"),app.ShowAssistant);
        yield return (Localization.Text("Notifications"),Localization.Text("See every recent alert, including Downloads · Enter"),()=>{CloseCommands();OpenNotificationCenter();});
        yield return (Localization.Text("Tuck / Return to automatic"),Localization.Text("Toggle the manual tuck latch; hover the edge to preview"),()=>{CloseCommands();ToggleTuck();});
        yield return (Localization.Text("Open Settings"),Localization.Text("Appearance, widgets, and interaction"),app.ShowSettings);
        foreach(var pair in new[]{("media",Localization.Text("Media controls")),("timer",Localization.Text("Timer")),("focus",Localization.Text("Focus")),("status",Localization.Text("System status")),("access",Localization.Text("Quick launch"))})
        {
            var page=pair.Item1;if(!app.Settings.Widgets.Any(w=>w.Enabled && PageFor(w.Id)==page))continue;
            yield return (pair.Item2,Localization.Text("Open this island page"),()=>{CloseCommands();if(app.Settings.CompactLayout)SelectPage(page);});
        }
        if(Settings.ToolNames.Keys.Any(ToolOn))
        {
            foreach(var pair in new[]{("calendar",Localization.Text("Calendar")),("awake",Localization.Text("Keep awake")),("files",Localization.Text("File shelf")),("notes",Localization.Text("Notes")),("tasks",Localization.Text("Tasks")),("clips",Localization.Text("Clipboard"))})
            {string mode=pair.Item1;if(!ToolOn(mode))continue;yield return(pair.Item2,Localization.Text("Open everyday tool"),()=>ShowTools(mode));}
        }
        if(app.Awake.Active)yield return(Localization.Text("Stop keep awake"),Localization.Text("Restore normal Windows idle behavior"),()=>{app.Awake.Stop();RefreshCommands();});
        foreach(var shortcut in app.Settings.Shortcuts.Where(_=>WidgetOn("shortcuts")))
        {var item=shortcut;yield return(item.Name,item.Target,()=>{ShortcutService.Open(item);Collapse();});}
    }
    private void RefreshCommands()
    {
        commandResults.Children.Clear();commandButtons.Clear();var tokens=(commandInput?.Text??"").Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var matches=Commands().Where(c=>tokens.All(t=>(c.title+" "+c.detail).Contains(t,StringComparison.OrdinalIgnoreCase))).ToArray();
        foreach(var command in matches)
        {
            var item=command;var button=UI.Button(item.title,()=>{try{item.run();}catch(Exception ex){app.Store.Log(ex);app.Notify(Localization.Text("Could not run command"),ex.Message);}});button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Padding=new(12,9,12,9);button.Margin=new(0,0,0,7);button.BorderThickness=new(1);button.BorderBrush=Brushes.Transparent;
            var content=new StackPanel();content.Children.Add(UI.Text(item.title,12));var detail=UI.Text(item.detail,10,UI.Muted);detail.TextWrapping=TextWrapping.NoWrap;detail.TextTrimming=TextTrimming.CharacterEllipsis;detail.Margin=new(0,4,0,0);content.Children.Add(detail);button.Content=content;
            button.PreviewKeyDown+=(_,e)=>{if(e.Key is not (Key.Up or Key.Down))return;int index=commandButtons.IndexOf(button)+(e.Key==Key.Down?1:-1);if(index<0)commandInput?.Focus();else if(index<commandButtons.Count)commandButtons[index].Focus();e.Handled=true;};commandButtons.Add(button);commandResults.Children.Add(button);
        }
        if(matches.Length==0)commandResults.Children.Add(UI.Text(Localization.Text("No matching commands or shortcuts."),12,UI.Muted));
        if(commandOpen)ResizeToolPanel();
    }
    private void BuildAwake(StackPanel panel)
    {
        panel.Children.Add(UI.Heading(Localization.Text("Keep awake"),18));var hint=UI.Text(Localization.Text("Temporarily prevent automatic sleep. Your normal power settings return when this stops or NabyScape quits."),12,UI.Muted);hint.Margin=new(0,8,0,14);panel.Children.Add(hint);
        awakeStatus=UI.Text("",14);awakeStatus.Margin=new(0,0,0,16);panel.Children.Add(awakeStatus);UpdateRoadmapStatus(DateTimeOffset.UtcNow);
        if(app.Awake.Active){panel.Children.Add(UI.Button(Localization.Text("Stop keep awake"),()=>MutateUtility(app.Awake.Stop),true));return;}
        var durations=CreateAwakeDuration();panel.Children.Add(durations);
        var display=new CheckBox{Content=Localization.Text("Keep the display on too"),IsChecked=false,Margin=new(0,10,0,12)};panel.Children.Add(display);
        panel.Children.Add(UI.Button(Localization.Text("Start keep awake"),()=>MutateUtility(()=>StartAwake(durations,display)),true));
        panel.Children.Add(UI.Text(Localization.Text("Explicit sleep, locking, and closing the lid remain under Windows control."),11,UI.Muted));
    }
    private void BuildCalendar(StackPanel panel)
    {
        if(calendarEditing){BuildEventEditor(panel);return;}
        panel.Children.Add(CalendarToolbar());
        var grid=new Grid{Margin=new(0,10,0,10),Visibility=calendarMonthExpanded?Visibility.Visible:Visibility.Collapsed};for(int i=0;i<7;i++)grid.ColumnDefinitions.Add(new());for(int i=0;i<7;i++)grid.RowDefinitions.Add(new(){Height=new GridLength(i==0?20:24)});
        for(int i=0;i<7;i++){var label=UI.Text(new[]{Localization.Text("Mon"),Localization.Text("Tue"),Localization.Text("Wed"),Localization.Text("Thu"),Localization.Text("Fri"),Localization.Text("Sat"),Localization.Text("Sun")}[i],10,UI.Muted);label.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetColumn(label,i);grid.Children.Add(label);}
        int offset=((int)calendarMonth.DayOfWeek+6)%7;
        for(int i=0;calendarMonthExpanded && i<42;i++)
        {
            long ticks=calendarMonth.Ticks+(long)(i-offset)*TimeSpan.TicksPerDay;if(ticks<DateTime.MinValue.Ticks || ticks>DateTime.MaxValue.Ticks)continue;
            DateTime day=new(ticks);var button=UI.Button(day.Day.ToString(),()=>{calendarDay=day;calendarMonth=new(day.Year,day.Month,1);calendarMonthExpanded=false;RefreshTools();});button.FontSize=10;button.MinHeight=0;button.Padding=new(0);button.Margin=new(1);button.Tag=new CornerRadius(7);button.Foreground=day==calendarDay?Brushes.Black:day.Month==calendarMonth.Month?Brushes.White:UI.Muted;button.Background=day==calendarDay?Brushes.White:DayEvents(day).Any()?UI.Brush("#2BFFFFFF"):Brushes.Transparent;UI.Named(button,day.ToString("dddd, d MMMM yyyy",Localization.Culture));CalendarVisuals.MarkToday(button,day,lightSelection:day==calendarDay);Grid.SetColumn(button,i%7);Grid.SetRow(button,1+i/7);grid.Children.Add(button);
        }
        panel.Children.Add(grid);if(!calendarMonthExpanded)panel.Children.Add(CalendarWeek());
        var events=DayEvents(calendarDay).ToArray();if(events.Length==0)panel.Children.Add(UI.Text(Localization.Text("No events for this day."),12,UI.Muted));
        foreach(var item in events)panel.Children.Add(CalendarAgendaEvent(item));
    }
    private IEnumerable<CalendarEvent> DayEvents(DateTime day)
    {
        DateTime localStart=DateTime.SpecifyKind(day.Date,DateTimeKind.Unspecified);
        return app.Calendar.Events.Where(e=>e.Start.LocalDateTime.Date<=localStart && e.End.LocalDateTime>localStart).OrderBy(e=>e.Start);
    }
    internal static DateTimeOffset EventTime(string date,string time,TimeZoneInfo zone)
    {
        if(!DateOnly.TryParseExact(date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day) || !TimeOnly.TryParseExact(time,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var clock))throw new ArgumentException(Localization.Text("Use YYYY-MM-DD dates and HH:mm times."));
        var value=day.ToDateTime(clock,DateTimeKind.Unspecified);if(zone.IsInvalidTime(value) || zone.IsAmbiguousTime(value))throw new ArgumentException(Localization.Text("This time is skipped or repeated by daylight saving. Choose a different time."));return new(value,zone.GetUtcOffset(value));
    }
    private void BuildEventEditor(StackPanel panel)
    {
        panel.Children.Add(UI.Heading(Localization.Text("New local event"),18));var hint=UI.Text(Localization.Text("Saved on this device. Times use your Windows time zone."),11,UI.Muted);hint.Margin=new(0,8,0,12);panel.Children.Add(hint);
        var title=UI.Input(Localization.Text("Event title"),"");title.MaxLength=120;panel.Children.Add(UI.Text(Localization.Text("Title"),11,UI.Muted));panel.Children.Add(title);
        TextBox startDate=UI.Input(Localization.Text("Event start date"),calendarDay.ToString("yyyy-MM-dd")),startTime=UI.Input(Localization.Text("Event start time"),"09:00"),endDate=UI.Input(Localization.Text("Event end date"),calendarDay.ToString("yyyy-MM-dd")),endTime=UI.Input(Localization.Text("Event end time"),"10:00");
        void DateRow(string label,TextBox date,TextBox time){panel.Children.Add(UI.Text(label+Localization.Text(" · YYYY-MM-DD / HH:mm"),11,UI.Muted));var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(83)});date.Margin=new(0,4,10,8);time.Margin=new(0,4,0,8);Grid.SetColumn(time,1);row.Children.Add(date);row.Children.Add(time);panel.Children.Add(row);}
        DateRow(Localization.Text("Start"),startDate,startTime);DateRow(Localization.Text("End"),endDate,endTime);panel.Children.Add(UI.Text(Localization.Text("Meeting URL · optional http(s)"),11,UI.Muted));var url=UI.Input(Localization.Text("Meeting URL"),"");url.MaxLength=2048;panel.Children.Add(url);var status=UI.Text("",11,UI.Muted);panel.Children.Add(status);
        var save=UI.Button(Localization.Text("Save event"),()=>{try{var start=EventTime(startDate.Text,startTime.Text,TimeZoneInfo.Local);var end=EventTime(endDate.Text,endTime.Text,TimeZoneInfo.Local);app.Calendar.Add(title.Text,start,end,url.Text);calendarEditing=false;calendarDay=start.LocalDateTime.Date;calendarMonth=new(calendarDay.Year,calendarDay.Month,1);RefreshTools();}catch(Exception ex){app.Store.Log(ex);status.Text=ex.Message;}},true);
        panel.Children.Add(UI.Row(save,UI.Button(Localization.Text("Cancel"),()=>{calendarEditing=false;RefreshTools();})));
    }
}
