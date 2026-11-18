using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SentoBar;

public sealed partial class OverlayWindow
{
    private readonly HashSet<string> shelfSelection=new(StringComparer.OrdinalIgnoreCase);
    private Border? shelfDropPreview;
    private string dropSignature="";
    private bool shelfDropBrowsing;
    private bool shortcutDropActive;
    private bool fileDragActive,dragWasExpanded,dragWasPeeking,dragWasNotice;
    private bool dragWasAssistantPeek;
    private string dragPreviousPeek="media";
    private void InitializeFileDrop()
    {
        AllowDrop=true;
        DragEnter+=(_,e)=>
        {
            if(!CanDropFiles(e)){e.Effects=DragDropEffects.None;return;}
            if(!fileDragActive)
            {
                if(WantsShortcutDrop(e.OriginalSource as DependencyObject))StartFileDrop(true);
                else BeginFileDrop();
            }
            ShowShelfDropPreview(e.Data.GetData(DataFormats.FileDrop) as string[]??Array.Empty<string>());e.Effects=DragDropEffects.Copy;e.Handled=true;
        };
        DragOver+=(_,e)=>{if(CanDropFiles(e)){e.Effects=DragDropEffects.Copy;e.Handled=true;}};
        DragLeave+=(_,e)=>{var p=e.GetPosition(this);if(!VisiblePoint(p))FinishFileDrop(false);};
        Drop+=(_,e)=>
        {
            if(!CanDropFiles(e))return;
            if(e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                if(fileDragActive?shortcutDropActive:WantsShortcutDrop(e.OriginalSource as DependencyObject))AddDroppedShortcuts(paths);
                else
                {
                    utilityMutating=true;try{app.Productivity.AddFiles(paths);}catch(Exception ex){app.Store.Log(ex);app.Notify(Localization.Text("Could not stage files"),ex.Message);}finally{utilityMutating=false;}
                    FinishFileDrop(true);
                }
                e.Effects=DragDropEffects.Copy;
            }
            e.Handled=true;
        };
    }
    private void BeginFileDrop()=>StartFileDrop(false);
    private void StartFileDrop(bool shortcuts)
    {
        if(userHidden || fileDragActive)return;
        fileDragActive=true;shortcutDropActive=shortcuts;dragWasExpanded=expanded;dragWasPeeking=peeking;dragPreviousPeek=peekId;
        dragWasAssistantPeek=assistantPeeking;
        if(assistantPeeking){HideAssistantPeek();assistantPeek.Visibility=Visibility.Collapsed;peeking=false;}
        dragWasNotice=noticeShowing;
        StopHoverTimers();EndPeekGesture();
        if(dragWasNotice){HideNotice();peeking=false;}
        if(shortcuts){if(peeking)PromotePeek();return;}
        // An expanded page stays mounted, including command/calendar editor state.
        if(!expanded){if(IsTucked)RevealTuckedPreview();if(!peeking)PreviewActivity();SelectPeek("files");PromotePeek();}
    }
    private bool WantsShortcutDrop(DependencyObject? source)
    {
        if(peeking)return !assistantPeeking && !noticeShowing && peekId=="shortcuts";
        if(!expanded || commandOpen || assistantOpen || VoiceRoomOpen)return false;
        if(app.Settings.CompactLayout)return activePage=="access";
        if(hoverPanelWidget=="shortcuts")return true;
        for(var node=source;node!=null;node=node is Visual visual?VisualTreeHelper.GetParent(visual):LogicalTreeHelper.GetParent(node))
            if(node is FrameworkElement {Tag:"shortcut-drop"})return true;
        return false;
    }
    private bool CanDropFiles(DragEventArgs e)=>!userHidden && e.Data.GetDataPresent(DataFormats.FileDrop) &&
        ((fileDragActive?shortcutDropActive:WantsShortcutDrop(e.OriginalSource as DependencyObject)) ||
         ToolOn("files") && (app.Settings.DropReveal || expanded && toolsMode=="files" || peeking && peekId=="files"));
    private void AddDroppedShortcuts(string[] paths)
    {
        bool restoreMini=peeking && peekId=="shortcuts";
        FinishFileDrop(true);
        if(app.Settings.Shortcuts.Count>=ShortcutService.MaximumShortcuts)
        {
            app.Notify(Localization.Text("Shortcuts"),Localization.Text("You can add up to 40 shortcuts."));
            return;
        }
        var additions=ShortcutService.FromDroppedPaths(paths,app.Settings.Shortcuts);
        if(additions.Length==0)return;
        app.Settings.Shortcuts.InsertRange(0,additions);
        app.ChangeSettings();
        if(restoreMini && !peeking){PreviewActivity();SelectPeek("shortcuts");}
    }
    private void ShowShelfDropPreview(string[] paths)
    {
        if(!fileDragActive)return;var signature=string.Join("\n",paths);if(shelfDropPreview!=null && dropSignature==signature)return;dropSignature=signature;
        var stack=new Grid{Width=144,Height=66,HorizontalAlignment=HorizontalAlignment.Center};
        for(int i=0;i<Math.Min(3,paths.Length);i++)
        {
            var tile=new Border{Width=54,Height=58,CornerRadius=new(7),Background=UI.Brush("#34363C"),Padding=new(5),Margin=new Thickness(i*32-64,0,0,0),HorizontalAlignment=HorizontalAlignment.Center,RenderTransform=new RotateTransform((i-1)*7)};
            var image=new Image{Stretch=Stretch.Uniform};tile.Child=image;stack.Children.Add(tile);_=LoadShelfThumbnail(paths[i],image);
        }
        var content=new StackPanel{VerticalAlignment=VerticalAlignment.Center};content.Children.Add(stack);
        var text=UI.Text(shortcutDropActive?Localization.Text("Drop to add shortcuts"):Localization.Format("Drop {0} items onto your shelf",paths.Length),12);
        text.TextAlignment=TextAlignment.Center;text.Margin=new(6,6,6,0);content.Children.Add(text);
        RemoveShelfDropPreview();
        shelfDropPreview=new Border{CornerRadius=new(10),BorderThickness=new(1),Child=content,IsHitTestVisible=false,Width=expanded?Math.Max(144,scroll.Width):PeekWidth,Height=124,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=expanded?new Thickness(0,app.Settings.Height+10,0,0):new Thickness(0)};
        shelfDropPreview.SetResourceReference(Border.BackgroundProperty,"PopupFill");
        shelfDropPreview.SetResourceReference(Border.BorderBrushProperty,"MenuBorder");
        if(expanded)visuals.Children.Add(shelfDropPreview);else peekViewport.Children.Add(shelfDropPreview);
    }
    private void RemoveShelfDropPreview()
    {
        if(shelfDropPreview?.Parent is Panel parent)parent.Children.Remove(shelfDropPreview);
        shelfDropPreview=null;
    }
    private void FinishFileDrop(bool accepted)
    {
        RemoveShelfDropPreview();dropSignature="";
        if(!fileDragActive)return;fileDragActive=false;
        if(shortcutDropActive){shortcutDropActive=false;return;}
        if(accepted){dragWasAssistantPeek=false;dragWasNotice=false;shelfDropBrowsing=true;if(peeking){SelectPeek("files");UpdatePeekDeck();}else ShowTools("files");return;}
        if(dragWasNotice){dragWasNotice=false;Collapse();OpenNotice();return;}
        if(dragWasExpanded)return;
        if(dragWasAssistantPeek){dragWasAssistantPeek=false;Collapse();PreviewAssistant();return;}
        else if(dragWasPeeking){SelectPeek(dragPreviousPeek);}
        else Collapse();
    }
    private ThumbnailCache shelfThumbnails=>app.Productivity.DisplayThumbnails;
}
