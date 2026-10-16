using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Baccarat.Desktop;

internal static class LegacyTheme
{
    // Shared with the dashboard. The historical class name is kept because the
    // retained views use it, but every workspace now uses one light visual system.
    internal static readonly IBrush Window = DashboardUi.Surface;
    internal static readonly IBrush Panel = Brushes.White;
    internal static readonly IBrush Field = Brush.Parse("#F0F3F8");
    internal static readonly IBrush FieldBorder = DashboardUi.Line;
    internal static readonly IBrush Gold = Brush.Parse("#A66308");
    internal static readonly IBrush Blue = Brush.Parse("#1769CE");
    internal static readonly IBrush Green = Brush.Parse("#119C50");
    internal static readonly IBrush Ink = DashboardUi.Ink;
    internal static readonly IBrush Text = DashboardUi.Ink;
    internal static readonly IBrush Muted = DashboardUi.Muted;

    internal static void AddStyles(Styles styles)
    {
        styles.Add(new Style(x => x.OfType<Button>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(5)),
                new Setter(TemplatedControl.BackgroundProperty, Panel),
                new Setter(TemplatedControl.ForegroundProperty, Text),
                new Setter(TemplatedControl.BorderBrushProperty, Blue),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(18, 11))
            }
        });
        styles.Add(new Style(x => x.OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
                new Setter(TemplatedControl.BackgroundProperty, Field),
                new Setter(TemplatedControl.ForegroundProperty, Text),
                new Setter(TemplatedControl.BorderBrushProperty, FieldBorder),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.FontSizeProperty, 16d),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(4, 5))
            }
        });
        styles.Add(new Style(x => x.OfType<TabItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.ForegroundProperty, Text),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(18, 12))
            }
        });
    }

    internal static Image Brand(double width = 170, double height = 92)
    {
        using var stream = AssetLoader.Open(new Uri("avares://Baccarat.Desktop/Assets/wetalkbet-logo.png"));
        return new Image
        {
            Source = new Bitmap(stream),
            Width = width,
            Height = height,
            Stretch = Stretch.Uniform
        };
    }

    internal static TextBlock TextBlock(string text, double size, IBrush? foreground = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = foreground ?? Text,
        TextWrapping = TextWrapping.Wrap
    };
}
