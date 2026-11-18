using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private MediaControls? mediaControls;
    private long mediaExternalDismissedAt;
    private FrameworkElement BuildMediaControls(StackPanel panel)
    {
        mediaControls=new MediaControls(panel,WidgetOn("media"),WidgetOn("volume"),
            ()=>new[]{new MediaSourceChoice(MediaSourceSelection.NoneId,Localization.Text("None"),app.Media.SelectNone),new MediaSourceChoice(MediaSourceSelection.AutoId,Localization.Text("Auto"),app.Media.SelectAutomatic)}.Concat(app.Media.Sessions.Select(session=>new MediaSourceChoice(session.SourceAppUserModelId,MediaService.SessionName(session),()=>
            {
                // A session may disappear after its menu was rendered.
                if(app.Media.Sessions.Contains(session))app.Media.Select(session);
            }))).ToArray(),()=>app.Media.SourceSelectionId,BuildVolumePopover,WidgetOn("media")?new SpotifyLikeButton(app):null);
        mediaControls.LayoutRequested+=QueueMediaLayout;mediaControls.SizeChanged+=(_,_)=>QueueMediaLayout();RefreshMediaControls(true);return mediaControls;
    }
    private FrameworkElement BuildVolumePage()
    {
        var page=new StackPanel();
        var label=UI.Heading(Localization.Text("Volume"),17);label.TextAlignment=TextAlignment.Center;
        page.Children.Add(label);
        var slider=new Slider{Minimum=0,Maximum=100,Margin=new(0,8,0,8)};
        UI.Named(slider,Localization.Text("Windows volume"));
        page.Children.Add(slider);
        var toggle=UI.CaptionButton("Mute or unmute",()=>{app.Audio.ToggleMute();Update();});
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center};
        actions.Children.Add(toggle);
        actions.Children.Add(UI.CaptionButton("Audio devices and microphone",OpenAudioMenu));
        actions.Children.Add(UI.CaptionButton("Volume mixer",OpenMixer));
        page.Children.Add(actions);
        bool syncing=false;
        void Refresh()
        {
            if(!page.IsVisible)return;
            syncing=true;
            if(!slider.IsMouseCaptureWithin && !slider.IsKeyboardFocusWithin)slider.Value=app.Audio.Volume*100;
            slider.IsEnabled=toggle.IsEnabled=app.Audio.Available;
            Motion.Text(label,app.Audio.Available?Localization.Format("Volume · {0:0}%",app.Audio.Volume*100):Localization.Text("No output device"));
            toggle.Content=Localization.Text(app.Audio.Muted?"Unmute":"Mute");
            syncing=false;
        }
        slider.ValueChanged+=(_,_)=>{if(!syncing){app.Audio.SetVolume(slider.Value/100);Refresh();}};
        page.Loaded+=(_,_)=>{app.Updated+=Refresh;Refresh();};
        page.IsVisibleChanged+=(_,_)=>Refresh();
        page.Unloaded+=(_,_)=>app.Updated-=Refresh;
        return page;
    }

    private FrameworkElement BuildVolumePopover()
    {
        DetachMediaControl(volume);DetachMediaControl(volumeText);
        var panel=new StackPanel();var header=new DockPanel{Margin=new(0,0,0,7)};
        var devices=Icons.Button("chevron",Localization.Text("Audio devices and microphone"),()=>{mediaControls?.Close(false);OpenAudioMenu();},false,22);devices.Background=Brushes.Transparent;devices.Margin=new(6,0,0,0);devices.ToolTip=Localization.Text("Output devices & microphone");DockPanel.SetDock(devices,Dock.Right);header.Children.Add(devices);DockPanel.SetDock(volumeText,Dock.Right);header.Children.Add(volumeText);header.Children.Add(UI.Text(Localization.Text("Volume"),12));panel.Children.Add(header);
        mute=Icons.Button("volume",Localization.Text("Mute or unmute"),()=>{app.Audio.ToggleMute();Update();},false,28);mute.Background=Brushes.Transparent;mute.Margin=new(0);muteIcon="";
        var row=new DockPanel();var mixer=Icons.Button("mixer",Localization.Text("Open volume mixer"),OpenMixer,false,28);mixer.Margin=new(7,0,0,0);mixer.Background=Brushes.Transparent;DockPanel.SetDock(mixer,Dock.Right);row.Children.Add(mixer);DockPanel.SetDock(mute,Dock.Left);row.Children.Add(mute);volume.Margin=new(8,0,5,0);UI.Named(volume,Localization.Text("Windows volume"));row.Children.Add(volume);panel.Children.Add(row);
        Update();return panel;
    }
    private static void DetachMediaControl(FrameworkElement control)
    {
        if(control.Parent is Panel panel)panel.Children.Remove(control);else if(control.Parent is Decorator decorator)decorator.Child=null;else if(control.Parent is ContentControl content)content.Content=null;
    }
    private void RefreshMediaControls(bool sources=false)=>mediaControls?.Refresh(volume.IsMouseCaptureWithin || volume.IsKeyboardFocusWithin?volume.Value:app.Audio.Volume*100,app.Audio.Muted,app.Audio.Available,sources);
    private void DismissMediaOrCollapse()
    {
        // Deactivation and the outside-click hook can describe the same click.
        long now=Environment.TickCount64;
        if(mediaControls?.Close(false)==true){mediaExternalDismissedAt=now;return;}
        if(now-mediaExternalDismissedAt<200)return;
        Collapse();
    }
}
