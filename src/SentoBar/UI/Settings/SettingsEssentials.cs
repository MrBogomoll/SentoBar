using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
namespace SentoBar;

public sealed partial class SettingsWindow
{
    private Expander? widgetNavigation;

    private void BuildNavigation(StackPanel navigation)
    {
        var widgetChildren=new StackPanel{Margin=new(10,0,0,0)};
        string? group=null;
        foreach(var category in SettingsCategories)
        {
            if(category.Group!=group && category.Group is "Dock" or "App")
            {
                var heading=UI.Caption(category.Group,11,SettingsTheme.Quiet);
                heading.FontWeight=FontWeights.SemiBold;
                heading.Margin=new(14,14,0,3);
                navigation.Children.Add(heading);
            }
            group=category.Group;
            if(category.Id=="Widgets")
            {
                navigation.Children.Add(new Border{Height=1,Background=SettingsTheme.LineSoft,Margin=new(14,12,14,3)});
                widgetNavigation=new Expander{Header=NavigationContent("nav-tools",Localization.Text("Widgets")),Content=widgetChildren,IsExpanded=false,Margin=new(10,8,0,8)};
                UI.LocalizedName(widgetNavigation,"Widget settings group");ControlFeedback.SetEnabled(widgetNavigation,false);navigation.Children.Add(widgetNavigation);
            }
            var item=category;string title=item.Id=="Widgets"?"Widget overview":item.Title;
            var button=UI.CaptionButton(title,()=>Navigate(item.Id));button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Content=NavigationContent(CategoryIcon(item.Id),Localization.Text(title));button.Padding=new(16,11,16,11);button.Margin=new(0,3,0,3);button.Tag=new CornerRadius(24);button.FontSize=13;button.MinHeight=app.Settings.WindowCompact?34:46;AutomationProperties.SetHelpText(button,Localization.Text(item.Description));navigationButtons[item.Id]=button;
            if(item.Group=="Features")widgetChildren.Children.Add(button);else navigation.Children.Add(button);
        }
        RefreshNavigationMode();
    }
    private void RefreshNavigationMode()
    {
        if(widgetNavigation!=null && (current=="Widgets" || Category(current).Group=="Features"))widgetNavigation.IsExpanded=true;
        if(categorySelector!=null)
        {
            var titles=SettingsCategories.Where(c=>c.Id=="Widgets" || c.Group!="Features" || Category(current).Group=="Features").Select(c=>Localization.Text(c.Title)).ToArray();
            if(!(categorySelector.ItemsSource is string[] old && old.SequenceEqual(titles))){categorySelector.ItemsSource=titles;categorySelector.SelectedItem=CategoryTitle(current);}
        }
    }
}
