using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Baccarat.Desktop;

internal static class WorkspaceUi
{
    internal static StackPanel Page(string eyebrow, string title, string description, bool centerHeading = false)
    {
        var page = new StackPanel { Margin = new Thickness(16), Spacing = 14 };
        var heading = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        heading.Children.Add(Text(eyebrow, 11, LegacyTheme.Gold, FontWeight.Bold));
        heading.Children.Add(Text(title, 24, LegacyTheme.Text, FontWeight.Bold));
        heading.Children.Add(Text(description, 13, LegacyTheme.Muted));
        if (centerHeading)
            foreach (var child in heading.Children.OfType<TextBlock>())
            {
                child.HorizontalAlignment = HorizontalAlignment.Center;
                child.TextAlignment = TextAlignment.Center;
            }
        page.Children.Add(heading);
        return page;
    }

    internal static Border Card(string heading, Control content)
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Text(heading, 12, LegacyTheme.Muted, FontWeight.Bold));
        body.Children.Add(content);
        return new Border
        {
            Background = LegacyTheme.Panel,
            BorderBrush = LegacyTheme.FieldBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(14),
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
            Background = LegacyTheme.Panel,
            BorderBrush = LegacyTheme.FieldBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(14),
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
        MinHeight = 34,
        Padding = new Thickness(12, 6),
        CornerRadius = new CornerRadius(5),
        Background = fill ?? LegacyTheme.Panel,
        Foreground = foreground ?? LegacyTheme.Text,
        BorderBrush = LegacyTheme.FieldBorder,
        BorderThickness = new Thickness(1),
        FontWeight = FontWeight.SemiBold,
        FontSize = 13,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    internal static Border HistoryRow(string text, IBrush? accent = null) => new()
    {
        Background = LegacyTheme.Panel,
        BorderBrush = accent ?? LegacyTheme.FieldBorder,
        BorderThickness = new Thickness(2, 0, 0, 0),
        Padding = new Thickness(10, 7),
        Child = Text(text, 13)
    };

    internal static TextBlock Text(string value, double size, IBrush? color = null, FontWeight? weight = null) => new()
    {
        Text = value,
        FontSize = size,
        Foreground = color ?? LegacyTheme.Text,
        FontWeight = weight ?? FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap
    };

    internal static IBrush Tone(string legacyColor) => legacyColor switch
    {
        "#E9EEF6" => LegacyTheme.Text,
        "#A8B5CA" or "#91A1B9" => LegacyTheme.Muted,
        "#E9BC72" => LegacyTheme.Gold,
        "#7BD88F" => LegacyTheme.Green,
        "#E48787" => Brush.Parse("#B42331"),
        _ => Brush.Parse(legacyColor)
    };
}
