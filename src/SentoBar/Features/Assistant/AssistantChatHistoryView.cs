using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SentoBar;

internal sealed partial class AssistantView
{
    private Button BuildChatHistoryButton()
    {
        var button=IconAction("Chat history","clock",()=>{});
        var menu=new ContextMenu{Placement=PlacementMode.Bottom,MinWidth=232,MaxWidth=320};
        menu.Style=(Style)FindResource("AssistantChatHistoryMenu");
        UI.LocalizedName(menu,"Chat history");
        menu.Opened+=(_,_)=>PopulateChatHistory(menu);
        AssistantControls.AttachMenu(button,menu);
        return button;
    }
    private void PopulateChatHistory(ContextMenu menu)
    {
        menu.Items.Clear();
        Add("New chat",()=>SwitchChat(ai.NewChat));
        menu.Items.Add(new Separator());
        foreach(var chat in ai.ChatHistory)
        {
            bool current=chat.Id==ai.ActiveChatId;
            var header=new StackPanel();
            var title=UI.Text(chat.Title,12);title.TextWrapping=TextWrapping.Wrap;header.Children.Add(title);
            header.Children.Add(UI.Caption(chat.Updated.ToLocalTime().ToString("g",Localization.Culture),10,UI.Muted));
            var item=new MenuItem{Header=header,IsCheckable=true,IsChecked=current};
            item.Click+=(_,_)=>{if(!current)SwitchChat(()=>ai.OpenChat(chat.Id));};
            menu.Items.Add(item);
        }
        if(ai.ChatHistory.Count==0)menu.Items.Add(new MenuItem{Header=Localization.Text("No saved chats yet"),IsEnabled=false});
        if(ai.ActiveChatId is {} id)
        {
            menu.Items.Add(new Separator());
            Add("Delete current chat…",()=>
            {
                if(Confirm("Delete this saved chat?"))SwitchChat(()=>ai.DeleteChat(id),confirmDraft:false);
            });
        }
        void Add(string label,Action action)
        {
            var item=new MenuItem{Header=Localization.Text(label)};UI.LocalizedName(item,label);
            item.Click+=(_,_)=>action();menu.Items.Add(item);
        }
    }
    private bool Confirm(string text)=>
        MessageBox.Show(Window.GetWindow(this),Localization.Text(text),Localization.Text("Sento"),MessageBoxButton.YesNo,
            MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes;
    private void SwitchChat(Func<bool> change,bool confirmDraft=true)
    {
        if(confirmDraft && (input.Text.Length>0 || ai.DraftFiles.Count>0) &&
            !Confirm("Switch chats and discard the unsent draft?"))return;
        if(!change())return;
        input.Clear();followLatest=true;restoreScroll=true;FocusComposer();Refresh();
    }
}
