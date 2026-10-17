using System.Windows;
using System.Windows.Controls;
using SpaceLens.ViewModels;

namespace SpaceLens.Controls;

/// <summary>Picks the chat bubble template that matches a message's role.</summary>
public sealed class ChatMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? User { get; set; }

    public DataTemplate? Answer { get; set; }

    public DataTemplate? Notice { get; set; }

    public DataTemplate? Error { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => (item as ChatMessageViewModel)?.Role switch
    {
        ChatRole.User => User,
        ChatRole.Assistant => Answer,
        ChatRole.Error => Error,
        _ => Notice
    };
}
