using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Baccarat.Core;
using Baccarat.Core.Roadmaps;

namespace Baccarat.Desktop.Views;

public sealed class RoadmapsView : UserControl
{
    private readonly RoadGrid[] grids = [new(18, 12, RoadStyle.Bead), new(18, 24, RoadStyle.Ring),
        new(12, 16, RoadStyle.Ring), new(12, 16, RoadStyle.Dot), new(12, 16, RoadStyle.Slash)];
    private readonly ScrollViewer[] scrolls = new ScrollViewer[5];
    private readonly TextBlock bankerAsk = DashboardUi.Text("", 12);
    private readonly TextBlock playerAsk = DashboardUi.Text("", 12);
    public RoadmapSnapshot Snapshot { get; private set; } = RoadmapCalculator.Calculate([]);

    public RoadmapsView()
    {
        Name = "Roadmaps";
        var main = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*"), ColumnSpacing = 12 };
        main.Children.Add(Road("Bead Plate", 0));
        Add(main, Road("Big Road", 1), 1);
        var derived = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 10 };
        Add(derived, Road("Big Eye Boy", 2), 0);
        Add(derived, Road("Small Road", 3), 1);
        Add(derived, Road("Cockroach Pig", 4), 2);
        var ask = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        ask.Children.Add(DashboardUi.Text("Ask · If next is…", 12, bold: true));
        ask.Children.Add(bankerAsk); ask.Children.Add(playerAsk);
        ToolTip.SetTip(ask, "Ask order: Big Eye / Small / Cockroach. — means more history is needed. Colors describe road patterns, not the next winner.");
        var card = DashboardUi.Card("Roadmaps · Current shoe", DashboardUi.Stack(main, derived, ask));
        ((StackPanel)card.Child!).Spacing = 6;
        Content = card;
        Update(Snapshot);
    }

    private static void Add(Grid grid, Control child, int column) { Grid.SetColumn(child, column); grid.Children.Add(child); }

    private Control Road(string name, int index)
    {
        scrolls[index] = new ScrollViewer
        {
            Content = grids[index],
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Height = grids[index].Height + 18
        };
        scrolls[index].SizeChanged += (_, _) => grids[index].MinWidth = Math.Max(0, scrolls[index].Bounds.Width);
        return DashboardUi.Stack(DashboardUi.Text(name, 11, DashboardUi.Muted), new Border
        {
            BorderBrush = DashboardUi.Line,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = scrolls[index]
        });
    }

    public void Update(RoadmapSnapshot snapshot)
    {
        Snapshot = snapshot;
        IReadOnlyList<RoadCell>[] roads = [snapshot.Beads, snapshot.BigRoad, snapshot.BigEye, snapshot.Small, snapshot.Cockroach];
        for (var i = 0; i < grids.Length; i++) grids[i].Update(roads[i]);
        SetAsk(bankerAsk, "Banker", snapshot.BankerAsk);
        SetAsk(playerAsk, "Player", snapshot.PlayerAsk);
        // Run after layout so the extent reflects the newly added cells.
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var scroll in scrolls) scroll.Offset = new Vector(Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width), 0);
        }, DispatcherPriority.Loaded);
    }

    private static void SetAsk(TextBlock text, string side, RoadAsk ask)
    {
        text.Inlines!.Clear();
        text.Inlines.Add(new Run(side + ":  "));
        var colors = new[] { ask.BigEye, ask.Small, ask.Cockroach };
        var symbols = new[] { "○", "●", "/" };
        for (var i = 0; i < colors.Length; i++)
        {
            if (i > 0) text.Inlines.Add(new Run("     "));
            text.Inlines.Add(new Run(colors[i] is { } color ? $"{symbols[i]} {color}" : "—")
            {
                Foreground = colors[i] is null ? DashboardUi.Muted :
                    DashboardUi.OutcomeColor(colors[i] == RoadColor.Red ? Outcome.Banker : Outcome.Player)
            });
        }
    }
    private enum RoadStyle { Bead, Ring, Dot, Slash }

    private sealed class RoadGrid : Control
    {
        private readonly double size;
        private readonly int minimumColumns;
        private readonly RoadStyle style;
        private IReadOnlyList<RoadCell> cells = [];
        private Rect viewport;
        private int columns;
        public RoadGrid(double size, int minimumColumns, RoadStyle style)
        {
            this.size = size;
            this.minimumColumns = minimumColumns;
            this.style = style;
            Height = size * RoadmapCalculator.Rows;
            HorizontalAlignment = HorizontalAlignment.Left;
            ClipToBounds = true;
            EffectiveViewportChanged += (_, e) => { viewport = e.EffectiveViewport; InvalidateVisual(); };
            PointerMoved += (_, e) =>
            {
                var point = e.GetPosition(this);
                var cell = cells.FirstOrDefault(c => c.Column == (int)(point.X / size) && c.Row == (int)(point.Y / size));
                ToolTip.SetTip(this, cell is null ? null : $"Shoe hand {cell.Hand} · {cell.Outcome?.ToString() ?? cell.Color?.ToString() ?? "Leading ties"}" +
                    (cell.Ties > 0 ? $" · Ties {cell.Ties}" : "") + (cell.BankerPair ? " · Banker pair" : "") + (cell.PlayerPair ? " · Player pair" : ""));
            };
        }

        public void Update(IReadOnlyList<RoadCell> value)
        {
            cells = value;
            columns = Math.Max(minimumColumns, cells.Count == 0 ? 0 : cells.Max(c => c.Column) + 2);
            Width = columns * size;
            Avalonia.Automation.AutomationProperties.SetName(this, $"{cells.Count} road entries");
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var start = Math.Max(0, (int)(viewport.X / size));
            var displayColumns = Math.Max(columns, (int)Math.Ceiling(Bounds.Width / size));
            var end = Math.Min(displayColumns, viewport.Width > 0 ? (int)(viewport.Right / size) + 1 : displayColumns);
            var pen = new Pen(DashboardUi.Line, 0.6);
            for (var col = start; col <= end; col++) context.DrawLine(pen, new Point(col * size, 0), new Point(col * size, Height));
            for (var row = 0; row <= RoadmapCalculator.Rows; row++) context.DrawLine(pen, new Point(start * size, row * size), new Point(end * size, row * size));
            foreach (var cell in cells)
            {
                if (cell.Column < start || cell.Column >= end) continue;
                var center = new Point((cell.Column + 0.5) * size, (cell.Row + 0.5) * size);
                var radius = size * 0.32;
                var color = cell.Outcome is { } outcome ? DashboardUi.OutcomeColor(outcome) :
                    cell.Color == RoadColor.Red ? DashboardUi.OutcomeColor(Outcome.Banker) : DashboardUi.OutcomeColor(Outcome.Player);
                if (cell.Outcome is not null || cell.Color is not null)
                {
                    if (style == RoadStyle.Slash) context.DrawLine(new Pen(color, 2), center + new Vector(-radius, radius), center + new Vector(radius, -radius));
                    else context.DrawEllipse(style == RoadStyle.Ring ? null : color, new Pen(color, 1.8), center, radius, radius);
                    if (style == RoadStyle.Bead) Text(context, cell.Outcome == Outcome.Banker ? "B" : cell.Outcome == Outcome.Player ? "P" : "T", center, Brushes.White, 10);
                }
                if (cell.Ties > 0)
                {
                    var green = DashboardUi.OutcomeColor(Outcome.Tie);
                    context.DrawLine(new Pen(green, 2), center + new Vector(-radius, radius), center + new Vector(radius, -radius));
                    if (cell.Ties > 1) Text(context, cell.Ties.ToString(CultureInfo.InvariantCulture), center, green, 10);
                }
                if (cell.BankerPair) Pair(context, center + new Vector(-radius, -radius), Outcome.Banker);
                if (cell.PlayerPair) Pair(context, center + new Vector(radius, radius), Outcome.Player);
            }
        }

        private static void Pair(DrawingContext context, Point point, Outcome side) =>
            context.DrawEllipse(DashboardUi.OutcomeColor(side), new Pen(Brushes.White, 1), point, 2.8, 2.8);
        private static void Text(DrawingContext context, string value, Point center, IBrush color, double fontSize)
        {
            var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Typeface.Default, fontSize, color);
            context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        }
    }
}
