using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Baccarat.Desktop;

internal static class WorkspaceUi
{
    internal static StackPanel Page(string eyebrow, string title, string description, bool centerHeading = false)
    {
        var page = new StackPanel { Margin = new Thickness(20, 8, 20, 20), Spacing = 16 };
        var heading = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        heading.Children.Add(Text(eyebrow, 11, LegacyTheme.Gold, FontWeight.Bold));
        heading.Children.Add(Text(title, 28, LegacyTheme.Text, FontWeight.SemiBold));
        var detail = Text(description, 13, LegacyTheme.Muted);
        detail.Margin = new Thickness(0, 2, 0, 0);
        heading.Children.Add(detail);
        if (centerHeading)
            foreach (var child in heading.Children.OfType<TextBlock>())
            {
                child.HorizontalAlignment = HorizontalAlignment.Center;
                child.TextAlignment = TextAlignment.Center;
            }
        page.Children.Add(heading);
        return page;
    }

    private static readonly BoxShadows CardShadow = new(new BoxShadow { OffsetX = 0, OffsetY = 1, Blur = 6, Color = Color.Parse("#14000000") });

    // Status label: a small coloured pill.
    internal static Border Pill(TextBlock text, IBrush color)
    {
        text.FontSize = 12;
        text.FontWeight = FontWeight.SemiBold;
        text.Foreground = Brushes.White;
        return new Border { Background = color, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3), HorizontalAlignment = HorizontalAlignment.Left, Child = text };
    }

    // Large highlighted statement with an accent bar (for the thing the user acts on).
    internal static Border Callout(Control content, IBrush accent) => new()
    {
        Background = Brush.Parse("#F3F7FD"),
        BorderBrush = accent,
        BorderThickness = new Thickness(4, 0, 0, 0),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(14, 10),
        Child = content
    };

    // Two cards side by side on wide windows (equal columns); children keep their own widths.
    internal static Grid Columns(Control left, Control right, double gap = 16)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = gap };
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    internal static WrapPanel WrapRow(double spacing = 10) => new() { Orientation = Orientation.Horizontal, ItemSpacing = spacing, LineSpacing = spacing };

    internal static Border Card(string heading, Control content)
    {
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(Text(heading, 11, LegacyTheme.Muted, FontWeight.Bold));
        body.Children.Add(content);
        return new Border
        {
            Background = LegacyTheme.Panel,
            BorderBrush = Brush.Parse("#E3E6EC"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 16),
            BoxShadow = CardShadow,
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
        MinHeight = 36,
        Padding = new Thickness(14, 7),
        CornerRadius = new CornerRadius(8),
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
        BorderBrush = accent ?? Brush.Parse("#C9D3E4"),
        BorderThickness = new Thickness(3, 0, 0, 0),
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(12, 7),
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
