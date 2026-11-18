using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
namespace SentoBar;
public sealed partial class App
{
    private void InitializeAppBranding()
    {
        SetCurrentProcessExplicitAppUserModelID("MrBogomoll.SentoBar.RU");
        var icon=BitmapFrame.Create(new Uri("pack://application:,,,/Assets/island.ico"));icon.Freeze();
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{if(sender is Window window && window.Icon==null)window.Icon=icon;}));
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
}
