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
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is AssistantViewModel oldModel) oldModel.Messages.CollectionChanged -= MessagesChanged;
            if (args.NewValue is AssistantViewModel newModel) newModel.Messages.CollectionChanged += MessagesChanged;
        };
    }
    private void MessagesChanged(object? sender, NotifyCollectionChangedEventArgs args) => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => ChatScroll.ScrollToEnd());
}
