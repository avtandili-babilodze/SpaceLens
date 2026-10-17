using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Threading;
using SpaceLens.ViewModels;

namespace SpaceLens.Views;

public partial class AssistantView : UserControl
{
    public AssistantView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is AssistantViewModel previous)
            {
                previous.Messages.CollectionChanged -= OnMessagesChanged;
            }

            if (e.NewValue is AssistantViewModel current)
            {
                current.Messages.CollectionChanged += OnMessagesChanged;
            }
        };
    }

    /// <summary>Keeps the newest message in view once layout has measured it.</summary>
    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Conversation.ScrollToEnd());
}
