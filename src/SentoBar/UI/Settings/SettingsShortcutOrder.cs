using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SentoBar;

public sealed partial class SettingsWindow
{
    private FrameworkElement ShortcutOrder()
    {
        const string dragFormat="SentoBar.ShortcutOrder";
        var list=new StackPanel();
        Shortcut? dragged=null;
        Border? marker=null;

        void ClearDropMarker()
        {
            if(marker!=null)marker.Visibility=Visibility.Collapsed;
            marker=null;
        }

        void MoveBefore(Shortcut source,int destination)
        {
            var shortcuts=app.Settings.Shortcuts;
            int from=shortcuts.IndexOf(source);
            if(from<0)return;
            int to=Math.Clamp(destination-(from<destination?1:0),0,shortcuts.Count-1);
            if(from==to)return;
            shortcuts.RemoveAt(from);
            shortcuts.Insert(to,source);
            Save();
            Build(source);
        }

        void Build(Shortcut? focus=null)
        {
            list.Children.Clear();
            foreach(var shortcut in app.Settings.Shortcuts)
            {
                var row=new DockPanel{Background=Brushes.Transparent,Margin=new(0,3,0,3)};
                var grip=UI.Button(Localization.Format("Drag to reorder {0}",shortcut.Name),()=>{});
                grip.Content="⠿";
                grip.Width=28;
                grip.FontSize=19;
                grip.Padding=new(0);
                grip.Margin=new(0,0,8,0);
                grip.Background=Brushes.Transparent;
                grip.Foreground=UI.Muted;
                grip.Cursor=Cursors.SizeNS;
                AutomationProperties.SetHelpText(grip,Localization.Text("Drag to reorder · Alt+↑ / Alt+↓ with keyboard"));
                DockPanel.SetDock(grip,Dock.Left);
                row.Children.Add(grip);

                var remove=UI.CaptionButton("Remove",()=>
                {
                    if(!app.Settings.Shortcuts.Remove(shortcut))return;
                    Save();
                    Build();
                });
                DockPanel.SetDock(remove,Dock.Right);
                row.Children.Add(remove);
                var detail=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,8,0)};
                detail.Children.Add(new TextBlock{Text=shortcut.Name,TextTrimming=TextTrimming.CharacterEllipsis});
                var target=UI.Text(shortcut.Target,10,UI.Muted);
                target.TextWrapping=TextWrapping.NoWrap;
                target.TextTrimming=TextTrimming.CharacterEllipsis;
                detail.Children.Add(target);
                row.Children.Add(detail);

                var card=new Grid{Background=Brushes.Transparent,AllowDrop=true};
                card.Children.Add(row);
                var line=new Border{Height=2,CornerRadius=new(1),IsHitTestVisible=false,Visibility=Visibility.Collapsed};
                line.SetResourceReference(Border.BackgroundProperty,"SettingsPrimaryBorder");
                card.Children.Add(line);
                list.Children.Add(card);

                Point? start=null;
                grip.PreviewMouseLeftButtonDown+=(_,e)=>start=e.GetPosition(grip);
                grip.PreviewMouseLeftButtonUp+=(_,_)=>start=null;
                grip.LostMouseCapture+=(_,_)=>start=null;
                grip.PreviewMouseMove+=(_,e)=>
                {
                    if(start is not Point origin || e.LeftButton!=MouseButtonState.Pressed ||
                        Math.Abs(e.GetPosition(grip).Y-origin.Y)<SystemParameters.MinimumVerticalDragDistance)return;
                    start=null;
                    dragged=shortcut;
                    row.Opacity=.55;
                    try{DragDrop.DoDragDrop(grip,new DataObject(dragFormat,"shortcut"),DragDropEffects.Move);}
                    finally{dragged=null;row.Opacity=1;ClearDropMarker();}
                };
                grip.PreviewKeyDown+=(_,e)=>
                {
                    if(Keyboard.Modifiers!=ModifierKeys.Alt || e.Key is not (Key.Up or Key.Down))return;
                    int from=app.Settings.Shortcuts.IndexOf(shortcut);
                    MoveBefore(shortcut,from+(e.Key==Key.Up?-1:2));
                    e.Handled=true;
                };
                card.DragOver+=(_,e)=>
                {
                    bool valid=dragged!=null && e.Data.GetDataPresent(dragFormat,false);
                    e.Effects=valid?DragDropEffects.Move:DragDropEffects.None;
                    e.Handled=true;
                    if(!valid)return;
                    if(marker!=line)ClearDropMarker();
                    marker=line;
                    line.VerticalAlignment=e.GetPosition(card).Y<card.ActualHeight/2?VerticalAlignment.Top:VerticalAlignment.Bottom;
                    line.Visibility=Visibility.Visible;
                    double edge=e.GetPosition(settingsScroll).Y;
                    if(edge<40)settingsScroll.LineUp();
                    else if(edge>settingsScroll.ActualHeight-40)settingsScroll.LineDown();
                };
                card.DragLeave+=(_,_)=>{if(marker==line)ClearDropMarker();};
                card.Drop+=(_,e)=>
                {
                    ClearDropMarker();
                    if(dragged==null || !e.Data.GetDataPresent(dragFormat,false))return;
                    int destination=app.Settings.Shortcuts.IndexOf(shortcut);
                    if(destination>=0)MoveBefore(dragged,destination+(e.GetPosition(card).Y<card.ActualHeight/2?0:1));
                    e.Handled=true;
                };
                if(ReferenceEquals(shortcut,focus))grip.Focus();
            }
        }

        Build();
        return list;
    }
}
