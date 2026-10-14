using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Baccarat.Desktop;

internal static class LegacyTheme
{
    internal static readonly IBrush Window = Brush.Parse("#19294B");
    internal static readonly IBrush Panel = Brush.Parse("#122342");
    internal static readonly IBrush Field = Brush.Parse("#203965");
    internal static readonly IBrush FieldBorder = Brush.Parse("#68748B");
    internal static readonly IBrush Gold = Brush.Parse("#E9A43A");
    internal static readonly IBrush Blue = Brush.Parse("#416DE1");
    internal static readonly IBrush Green = Brush.Parse("#12F45C");
    internal static readonly IBrush Ink = Brush.Parse("#111A31");
    internal static readonly IBrush Text = Brush.Parse("#F4F6FA");
    internal static readonly IBrush Muted = Brush.Parse("#A7B3C7");

    internal static void AddStyles(Styles styles)
    {
        styles.Add(new Style(x => x.OfType<Button>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(0)),
                new Setter(TemplatedControl.BackgroundProperty, Panel),
                new Setter(TemplatedControl.ForegroundProperty, Text),
                new Setter(TemplatedControl.BorderBrushProperty, Blue),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(18, 11))
            }
        });
        styles.Add(new Style(x => x.OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(0)),
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
