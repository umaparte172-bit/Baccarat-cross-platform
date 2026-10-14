using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Baccarat.Desktop;

internal static class WorkspaceUi
{
    internal static StackPanel Page(string eyebrow, string title, string description)
    {
        var page = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        page.Children.Add(Text(eyebrow, 12, LegacyTheme.Gold, FontWeight.Bold));
        page.Children.Add(Text(title, 28, LegacyTheme.Text, FontWeight.Bold));
        page.Children.Add(Text(description, 14, LegacyTheme.Muted));
        return page;
    }

    internal static Border Card(string heading, Control content)
    {
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(Text(heading, 13, LegacyTheme.Gold, FontWeight.Bold));
        body.Children.Add(content);
        return new Border
        {
            Background = LegacyTheme.Panel,
            BorderBrush = LegacyTheme.FieldBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20),
            Child = body
        };
    }

    internal static Border Metric(string label, TextBlock value, IBrush? accent = null)
    {
        var body = new StackPanel { Spacing = 7 };
        body.Children.Add(Text(label, 11, LegacyTheme.Muted, FontWeight.Bold));
        value.FontSize = 22;
        value.FontWeight = FontWeight.Bold;
        value.Foreground = accent ?? LegacyTheme.Text;
        body.Children.Add(value);
        return new Border
        {
            Background = LegacyTheme.Field,
            BorderBrush = LegacyTheme.FieldBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Child = body
        };
    }

    internal static StackPanel Row(double spacing = 12) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = spacing
    };

    internal static Button Action(string label, IBrush? fill = null, IBrush? foreground = null) => new()
    {
        Content = label,
        MinHeight = 42,
        Padding = new Thickness(18, 10),
        Background = fill ?? LegacyTheme.Field,
        Foreground = foreground ?? LegacyTheme.Text,
        BorderBrush = LegacyTheme.Blue,
        BorderThickness = new Thickness(1),
        FontWeight = FontWeight.SemiBold
    };

    internal static Border HistoryRow(string text, IBrush? accent = null) => new()
    {
        Background = LegacyTheme.Field,
        BorderBrush = accent ?? LegacyTheme.FieldBorder,
        BorderThickness = new Thickness(3, 0, 0, 0),
        Padding = new Thickness(12, 9),
        Child = Text(text, 14)
    };

    internal static TextBlock Text(string value, double size, IBrush? color = null, FontWeight? weight = null) => new()
    {
        Text = value,
        FontSize = size,
        Foreground = color ?? LegacyTheme.Text,
        FontWeight = weight ?? FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap
    };
}
