using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
namespace SentoBar;
public sealed partial class OverlayWindow
{
    private readonly PeekWheelAccumulator panelWheel=new();
    private Point? panelDown;
    private bool panelSwiped;
    private static bool PanelGestureBlocked(DependencyObject? source)
    {for(var node=source;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))if(node is ButtonBase or TextBoxBase or Slider or Thumb or ScrollBar or ComboBox or DurationPicker || node is FrameworkElement f && f.Tag as string is "file-drag" or "media-popover")return true;return false;}
    private bool CanPanelSwipe=>expanded && app.Settings.CompactLayout && app.Settings.PanelSwipe && !commandOpen && !assistantOpen;
    internal bool RoutePanelWheel(int delta,bool horizontal,DependencyObject? source,bool? scrollContent=null)
    {
        if(!CanPanelSwipe)return false;
        if(!horizontal && (scrollContent??Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
            return ScrollPanelContent(delta,source);
        int direction=panelWheel.Push(horizontal?delta:-delta,Environment.TickCount64);
        if(direction!=0)CyclePage(direction,true);
        return true;
    }

    private bool ScrollPanelContent(int delta,DependencyObject? source)
    {
        for(var node=source;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
        {
            // A duration field uses the wheel to edit its current number.
            if(node is DurationPicker picker){picker.ScrollPart(source,delta);return true;}
            if(node is TextBoxBase editor)
            {
                if(editor.Template?.FindName("PART_ContentHost",editor) is ScrollViewer content && content.ScrollableHeight>1)
                {
                    content.ScrollToVerticalOffset(content.VerticalOffset-delta*.35);
                    return true;
                }
                continue;
            }
            if(node is not ScrollViewer viewer || viewer.ScrollableHeight<=1)continue;
            // At the task list boundary, a very short panel may still have room to scroll outside it.
            if(viewer==taskViewport && (delta<0?viewer.ScrollableHeight-viewer.VerticalOffset<=.5:viewer.VerticalOffset<=.5))continue;
            if(viewer==clipboardScroll)ScrollClipboardWheel(delta);
            else viewer.ScrollToVerticalOffset(viewer.VerticalOffset-delta*.35);
            return true;
        }
        return true;
    }

    private void InitializePanelSwipe()
    {
        root.PreviewMouseLeftButtonDown+=(_,e)=>{panelDown=null;panelSwiped=false;if(CanPanelSwipe && !PanelGestureBlocked(e.OriginalSource as DependencyObject))panelDown=e.GetPosition(root);};
        root.PreviewMouseMove+=(_,e)=>
        {
            if(panelDown==null || !CanPanelSwipe || e.LeftButton!=MouseButtonState.Pressed)return;
            var delta=e.GetPosition(root)-panelDown.Value;
            if(Math.Abs(delta.Y)>Math.Abs(delta.X) && Math.Abs(delta.Y)>10){panelDown=null;return;}
            if(Math.Abs(delta.X)<app.Settings.SwipeDistance)return;
            panelDown=null;panelSwiped=true;CyclePage(delta.X<0?1:-1);e.Handled=true;
        };
        root.PreviewMouseLeftButtonUp+=(_,e)=>{panelDown=null;if(panelSwiped){panelSwiped=false;e.Handled=true;}};
        root.LostMouseCapture+=(_,_)=>panelDown=null;
    }
}
