using System.Windows;
using System.Windows.Controls;

namespace SpaceLens.Views;

public partial class DashboardView : UserControl
{
    public static readonly DependencyProperty IsNarrowProperty =
        DependencyProperty.Register(nameof(IsNarrow), typeof(bool), typeof(DashboardView), new PropertyMetadata(false));

    /// <summary>Below this width the figures move under the headline so nothing gets cut off.</summary>
    private const double NarrowWidth = 820;

    public DashboardView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => IsNarrow = e.NewSize.Width < NarrowWidth;
    }

    public bool IsNarrow
    {
        get => (bool)GetValue(IsNarrowProperty);
        set => SetValue(IsNarrowProperty, value);
    }
}
