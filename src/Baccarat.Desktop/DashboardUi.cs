using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Baccarat.Desktop;

internal static class DashboardUi
{
    internal static readonly IBrush Ink = Brush.Parse("#242833");
    internal static readonly IBrush Muted = Brush.Parse("#626875");
    internal static readonly IBrush Line = Brush.Parse("#DBDEE5");
    internal static readonly IBrush Surface = Brush.Parse("#F7F8FA");
    internal static TextBlock Text(string text, double size = 13, IBrush? color = null, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = color ?? Ink,
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap
    };
    internal static Button Button(string text, Action action, string? color = null)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 34,
            Padding = new Thickness(12, 6),
            CornerRadius = new CornerRadius(5),
            Background = color is null ? Brushes.White : Brush.Parse(color),
            Foreground = color is null ? Ink : Brushes.White,
            BorderBrush = color is null ? Line : Brush.Parse(color),
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 13
        };
        button.Click += (_, _) => action();
        return button;
    }
    internal static TextBox Input(string name, string hint) => new()
    {
        Name = name,
        Watermark = hint,
        MaxLength = 512,
        MinHeight = 32,
        FontSize = 13,
        Background = Brushes.White,
        Foreground = Ink,
        BorderBrush = Line,
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(8, 5)
    };
    internal static StackPanel Stack(params Control[] controls)
    {
        var stack = new StackPanel { Spacing = 10 };
        foreach (var control in controls) stack.Children.Add(control);
        return stack;
    }
    internal static Border Card(string title, Control content) => new()
    {
        Background = Brushes.White,
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(7),
        Padding = new Thickness(14),
        Child = Stack(Text(title, 12, Muted, true), content)
    };
    internal static IBrush OutcomeColor(Baccarat.Core.Outcome outcome) => Brush.Parse(outcome switch
    {
        Baccarat.Core.Outcome.Banker => "#CB3529",
        Baccarat.Core.Outcome.Player => "#1769CE",
        _ => "#138047"
    });
}
