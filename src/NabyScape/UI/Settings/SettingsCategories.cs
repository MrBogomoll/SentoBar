using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace NabyScape;

internal sealed record SettingsCategory(string Id,string Title,string Group,string Description,string Terms,string[]? Sections=null);
public sealed partial class SettingsWindow
{
    private static readonly SettingsCategory[] SettingsCategories=
    {
        new("Home","Start here","Settings","Make NabyScape work the way you want.","home start setup help beginner"),
        new("Appearance","Dock colors & shape","Dock","Set the background and shape shared by the closed dock, mini panel and expanded panel.","appearance theme warna tampilan font shape glass цвет панели вырез форма",new[]{"Color & material","Shape","Surface","Controls & outlines","Typography"}),
        new("ClosedDock","Dock content","Dock","Choose what the closed dock shows when idle and when music plays.","closed notch lyrics music ask naby clock текст выреза что показывать часы дата спросить песня",new[]{"When idle","Closed media display"}),
        new("Controls","Tabs & controls","Dock","Choose labels, dots or icons for the mini and expanded panels. Set up the mouse, hover and shortcuts.","mouse keyboard shortcut hotkey swipe scroll gesture точки подписи значки иконки вкладки колесико колёсико",new[]{"Panel navigation","Hover & swipe","Detachable previews","Notch gestures","Global hotkey","Drag to Island","Closed cover gestures"}),
        new("Layout","Position & size","Dock","Place the dock on a screen edge or move it freely.","position posisi pindah atas bawah kanan kiri monitor size resize ukuran"),
        new("Visibility","Show & hide","Dock","Choose when the dock hides or shows automatic previews.","hide show tuck sembunyi tutup youtube fullscreen maximized hover preview",new[]{"Automatic tucking","Hide exceptions","Manual tuck shortcut","Hide or show the island","Automatic activity"}),
        new("Ambience","Dock lighting","Dock","Choose where the light appears and how it follows your music.","ambient ambience cahaya warna glow spread softness light reach closed dock tertutup подсветка свечение",new[]{"Closed dock lighting","Ambient light"}),
        new("Motion","Motion & speed","Dock","Adjust animation and choose a balance between smoothness and power.","animation animasi motion fps speed performance ram cpu hemat reduce",new[]{"Motion","Animation performance"}),
        new("WindowAppearance","Settings window","App","Choose a light or dark theme, transparency and spacing for this settings window.","settings theme window menu color light dark цвет меню настроек тема светлая темная тёмная",new[]{"Window theme","Window appearance"}),
        new("Notifications","Notifications","App","Choose which alerts appear, their sounds and quiet hours.","notifications notif pemberitahuan alert sound bunyi quiet hours downloads other apps claude telegram windows",new[]{"Notifications","Other apps","Notification presentation","Downloads","Completion sound"}),
        new("Application","General","App","Manage startup, updates, local data and app preferences.","general startup update version install storage data reset backup mulai windows"),
        new("Widgets","Widgets","Features","Find the settings for each dock widget.","widgets features plugins modules notes tasks timer music weather"),
        new("Content","Widget selection","Features","Turn widgets on or off and change their order on the panels.","widget tools urutan notes task timer focus stopwatch pomodoro"),
        new("Notes","Notes","Features","Write a quick draft and keep saved notes in your local library.","notes note saved draft library заметки записи черновик"),
        new("Tasks","Tasks","Features","Create tasks and track what is done.","tasks todo checklist задачи список дела"),
        new("Timers","Timers & stopwatch","Features","Set focus sessions and use timers and the stopwatch.","timer stopwatch focus pomodoro таймер секундомер",new[]{"Timer","Focus sessions","Stopwatch"}),
        new("Audio","Music & sound","Features","Customize music controls, lyrics and the audio visualizer.","music spotify volume musik suara waveform spectrum lirik lyrics audio microphone"),
        new("Clipboard","Clipboard","Features","Control whether copied text and images are saved on this device.","clipboard salin copy paste capture history riwayat gambar",new[]{"Clipboard history"}),
        new("Weather","Weather","Features","Choose your city and see the weather in Celsius or Fahrenheit.","weather cuaca kota city temperature suhu rain hujan"),
        new("System","Clock & system","Features","Choose the clock format and computer information shown in widgets.","clock jam tanggal date battery cpu ram network storage system"),
        new("Launch","Shortcuts","Features","Choose your favorite apps, folders and websites.","apps launch aplikasi shortcuts explorer"),
        new("AI","Naby AI","Features","Connect an AI provider and manage companion preferences.","ai naby hermes gemini live voice provider api key chat model token")
    };
    private static SettingsCategory Category(string id)=>SettingsCategories.FirstOrDefault(c=>c.Id==id)??SettingsCategories[0];
    private static string CategoryTitle(string id)=>Localization.Text(Category(id).Title);
    private static string CategoryId(string title)=>SettingsCategories.FirstOrDefault(c=>c.Title==title || Localization.Text(c.Title)==title)?.Id??title;
    private static string SettingsRoot(string id)=>id switch{"Clipboard"=>"Content","Notifications"=>"Application",_=>id};
    private static string FriendlySection(string title)=>title switch
    {
        "Shape"=>"Dock shape","Color & material"=>"Dock background & tint","Settings material"=>"Settings window","Surface"=>"Glass reflections","Controls & outlines"=>"Buttons & input fields","Typography"=>"Text weight","Detachable previews"=>"Separate widget windows","Hover & swipe"=>"Mini panel on hover","Closed cover gestures"=>"Album cover controls","Animation performance"=>"Smoothness & power","Display"=>"Screen","Closed island"=>"Compact dock","Open panel"=>"Expanded dock","Enabled widgets & order"=>"Your widgets","Notch gestures"=>"Mouse actions","Global hotkey"=>"Open dock shortcut","Automatic activity"=>"Automatic previews","Clipboard history"=>"Clipboard capture","Completion sound"=>"Timer & focus sound","Closed media display"=>"While music plays",_=>title
    };
    private void FilterSettingsCategory(string id)
    {
        var definition=Category(id);
        var excluded=id switch{"Content"=>new[]{"Clipboard history"},"Application"=>Category("Notifications").Sections!,_=>Array.Empty<string>()};
        foreach(var section in page.Children.OfType<Border>().Where(b=>b.Tag is string).ToArray())
        {
            string key=(string)section.Tag;
            if(definition.Sections is {} allowed && !allowed.Contains(key) || excluded.Contains(key))page.Children.Remove(section);
        }
        if(definition.Sections!=null)foreach(var note in page.Children.OfType<TextBlock>().Skip(1).ToArray())page.Children.Remove(note);
        var retained=page.Children.OfType<Border>().Select(b=>b.Tag as string).ToHashSet();settingExamples.RemoveAll(scene=>!retained.Contains(scene.Section));
    }
    private void WeatherSettingsPage()
    {
        page.Children.Add(new Border{Child=new WeatherView(app,true),Background=SettingsTheme.Card,BorderBrush=SettingsTheme.LineSoft,BorderThickness=new(.7),CornerRadius=new(14),Padding=new(16),Margin=new(0,0,0,16)});
    }
}
