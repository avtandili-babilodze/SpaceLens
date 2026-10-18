using System.Windows;
using System.Windows.Input;
using SpaceLens.Core.Services.Interaction;

namespace SpaceLens.Views;

/// <summary>A themed confirmation. Destructive actions get a red button and never confirm on Enter.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(ConfirmationRequest request)
    {
        InitializeComponent();
        TitleText.Text = request.Title;
        MessageText.Text = request.Message;
        ConfirmButton.Content = request.ConfirmText;
        CancelButton.Content = request.CancelText;
        CancelButton.Visibility = request.ShowCancel ? Visibility.Visible : Visibility.Collapsed;

        DetailsList.ItemsSource = request.Details;
        DetailsPanel.Visibility = request.Details is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        FootnoteText.Text = request.Footnote;
        FootnoteText.Visibility = string.IsNullOrEmpty(request.Footnote) ? Visibility.Collapsed : Visibility.Visible;

        if (request.IsDestructive)
        {
            ConfirmButton.Style = (Style)FindResource("Button.Danger");
            Loaded += (_, _) => CancelButton.Focus();
        }
        else
        {
            ConfirmButton.IsDefault = true;
            Loaded += (_, _) => ConfirmButton.Focus();
        }

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
