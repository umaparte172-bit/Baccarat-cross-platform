using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Baccarat.Core;
using Baccarat.Core.Roadmaps;
using Baccarat.Desktop.Views;

internal static class RoadmapViewChecks
{
    internal static void Run(string? screenshotDirectory)
    {
        var view = new RoadmapsView();
        var window = new Window { Width = 1040, Height = 560, Content = view };
        window.Show();
        var sequence = "TTBBBBBBBBPPPPPPPPBBPBPPBBBPBBPPPBPT";
        var hands = sequence.Select((c, i) => new RoadHand(c == 'B' ? Outcome.Banker : c == 'P' ? Outcome.Player : Outcome.Tie,
            i % 7 == 0, i % 11 == 0)).ToArray();
        view.Update(RoadmapCalculator.Calculate(hands));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        if (view.Snapshot.Beads.Count != hands.Length) throw new Exception("Rendered road history missing hands");
        if (screenshotDirectory is not null)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            using var frame = window.CaptureRenderedFrame() ?? throw new Exception("No roadmap frame");
            frame.Save(Path.Combine(screenshotDirectory, "roadmaps-populated.png"));
        }
        view.Update(RoadmapCalculator.Calculate([]));
        Dispatcher.UIThread.RunJobs();
        if (view.Snapshot.BigRoad.Count != 0 || view.Snapshot.BigEye.Count != 0)
            throw new Exception("Roads retained entries from previous shoe");
        window.Close();
        Console.WriteLine("PASS: populated roadmap rendering and shoe reset");
    }
}
