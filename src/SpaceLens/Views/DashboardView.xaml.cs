using System.Windows;
using System.Windows.Controls;

namespace SpaceLens.Views;

public partial class DashboardView : UserControl
{
    public static readonly DependencyProperty IsNarrowProperty =
        DependencyProperty.Register(nameof(IsNarrow), typeof(bool), typeof(DashboardView), new PropertyMetadata(false));

    /// <summary>Width at which the four cards switch to their compact layout.</summary>
    private const double NarrowWidth = 1000;

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
