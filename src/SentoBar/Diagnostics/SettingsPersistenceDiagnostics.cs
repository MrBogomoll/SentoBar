using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SentoBar;

internal static class SettingsPersistenceDiagnostics
{
    internal static async Task Run(App app,Action<string,bool> check)
    {
        const string failure="Not saved · changes are active for this session only. Choose Retry save.";
        SettingsWindow Open(string category)
        {
            app.ShowSettings(category);
            var window=Application.Current.Windows.OfType<SettingsWindow>().Single();
            window.UpdateLayout();
            return window;
        }
        static T Find<T>(SettingsWindow window,string id) where T:FrameworkElement
            =>Diagnostics.LogicalDescendants(window).OfType<T>().First(control=>UI.Identity(control)==id);
        static void Toggle(CheckBox control,bool value)
        {
            control.IsChecked=value;
            control.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
        static async Task<bool> WaitFor(Func<bool> condition)
        {
            for(int attempt=0;attempt<100 && !condition();attempt++)await Task.Delay(35);
            return condition();
        }

        app.Settings.CoverAmbient=true;
        app.Settings.PanelNavigationStyle="Dots";
        app.ChangeSettings();
        var window=Open("Ambience");
        var status=Find<TextBlock>(window,"Settings status");
        Toggle(Find<CheckBox>(window,"Ambient light"),false);
        check("Automatic save confirms the changed value on disk",
            await WaitFor(()=>status.Text.StartsWith(Localization.Format("Saved · {0}",""),StringComparison.Ordinal)) &&
            app.SettingsSaveSucceeded && !app.Store.Load<Settings>("settings").CoverAmbient);

        string file=Path.Combine(app.Store.DirectoryPath,"settings.json");
        byte[] before=File.ReadAllBytes(file);
        using(var locked=File.Open(file,FileMode.Open,FileAccess.Read,FileShare.None))
        {
            Toggle(Find<CheckBox>(window,"Ambient light"),true);
            check("A locked file reports an unsaved change and offers retry",
                await WaitFor(()=>status.Text==Localization.Text(failure)) && app.SettingsNeedSave &&
                Find<Button>(window,"Retry save").Visibility==Visibility.Visible);
            window.OpenCategory("Home");
            check("Navigating keeps the failed save visible",status.Text==Localization.Text(failure));
            window.Close();
        }
        check("The failed save leaves the last settings file intact",File.ReadAllBytes(file).SequenceEqual(before));
        window=Open("Controls");
        var retry=Find<Button>(window,"Retry save");
        check("Reopening Settings still offers retry",retry.Visibility==Visibility.Visible && app.SettingsNeedSave);
        retry.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check("Retry writes the pending change after the file is unlocked",
            app.SettingsSaveSucceeded && !app.SettingsNeedSave && app.Store.Load<Settings>("settings").CoverAmbient);

        var navigation=Find<ComboBox>(window,"Panel navigation style");
        navigation.SelectedItem="Icons";
        window.Close();
        check("Closing Settings immediately commits the pending choice",app.Store.Load<Settings>("settings").PanelNavigationStyle=="Icons");

        window=Open("Ambience");
        Toggle(Find<CheckBox>(window,"Ambient light"),false);
        check("Exit fixture still has a queued change",Find<TextBlock>(window,"Settings status").Text==Localization.Text("Saving changes…"));
        // Diagnostics.Run exits the application immediately after reporting. A second process loads this profile.
    }

    internal static void CheckReload(App app,Action<string,bool> check)
    {
        check("New process loads the saved navigation choice",app.Settings.PanelNavigationStyle=="Icons");
        check("New process loads the last switch change made just before exit",!app.Settings.CoverAmbient);
        check("Profile loaded without storage recovery warnings",app.Store.Warning==null);
    }
}
