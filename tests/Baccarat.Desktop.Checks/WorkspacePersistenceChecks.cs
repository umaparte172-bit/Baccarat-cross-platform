using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Baccarat.Core;
using Baccarat.Core.Roadmaps;
using Baccarat.Core.Strategies;
using Baccarat.Desktop;
using Baccarat.Desktop.ViewModels;
using Baccarat.Infrastructure.Persistence;

internal static class WorkspacePersistenceChecks
{
    public static void Run()
    {
        var workspace = new DashboardWorkspace();
        workspace.CreateSession();
        var session = workspace.Selected!;
        session.BankrollDraft = "100";
        Assert(session.ApplySettings(out _), "Apply bankroll");
        var definition = new StrategyDefinition("Persistence fixture", null,
            new("PLAYER", AmountRuleKind.Set, new[] { 10m }),
            new("BANKER", AmountRuleKind.Set, new[] { 20m }),
            new("PLAYER", AmountRuleKind.Set, new[] { 5m }), null, null, null, null, 0);
        session.LoadStrategy(definition);
        session.Record(Outcome.Player);
        session.Record(Outcome.Banker);
        Assert(session.NewShoe() && !session.NewShoe(), "No consecutive empty shoes");
        session.Record(Outcome.Tie);
        session.Engine!.ChooseSide(BetSide.Player);
        session.Engine.SetManualStake(30);
        session.BankerPairDraft = true;
        session.Record(Outcome.Banker);
        session.PlayerPairDraft = true;
        session.SelectStrategy("Sequence");
        session.SetField("sides", "P,B");
        session.BaseUnitDraft = "unfinished draft";
        var stats = DashboardStatistics.Calculate(session);
        Assert(session.Profit == -1 && session.Balance == 99 && stats.PeakBalance == 129 &&
            stats.Drawdown == 30 && stats.MaximumDrawdown == 30 && stats.StakeTotal == 80 &&
            stats.Wins == 2 && stats.Losses == 1 && stats.Pushes == 1 && stats.Streak == 1,
            "Exact decimal settlements and session statistics");
        var encoded = workspace.Serialize();
        var restoredWorkspace = DashboardWorkspace.Deserialize(encoded);
        var restored = restoredWorkspace.Selected!;
        static RoadmapSnapshot Roads(DashboardSession value) => RoadmapCalculator.Calculate(value.ShoeHands.Select(h =>
            new RoadHand(h.Round.Outcome, h.BankerPair, h.PlayerPair)));
        var originalRoads = Roads(session);
        var restoredRoads = Roads(restored);
        Assert(originalRoads.Beads.SequenceEqual(restoredRoads.Beads) && originalRoads.BigRoad.SequenceEqual(restoredRoads.BigRoad) &&
            originalRoads.BigEye.SequenceEqual(restoredRoads.BigEye) && originalRoads.Small.SequenceEqual(restoredRoads.Small) &&
            originalRoads.Cockroach.SequenceEqual(restoredRoads.Cockroach) && originalRoads.BankerAsk == restoredRoads.BankerAsk &&
            originalRoads.PlayerAsk == restoredRoads.PlayerAsk, "All current-shoe roads and Ask must rebuild identically after restart");
        Assert(encoded.SequenceEqual(restoredWorkspace.Serialize()) && restored.ToCsv() == session.ToCsv(), "Lossless archive roundtrip");
        Assert(restored.CurrentShoe == 2 && restored.ShoeHandCount == 2 && restored.PlayerPairDraft &&
            restored.GetField("sides") == "P,B" && restored.BaseUnitDraft == "unfinished draft", "Drafts, pairs and shoes persist");
        restored.Undo();
        Assert(restored.Profit == 29 && restored.Engine!.Recommendation.Side == BetSide.Player &&
            restored.Engine.Recommendation.Stake == 30, "Restored Undo retains pre-hand manual choice");
        restored.Undo();
        Assert(restored.ShoeHandCount == 0 && restored.CurrentShoe == 2, "Undo hand retains boundary");
        restored.Undo();
        Assert(restored.CurrentShoe == 1 && restored.Hands.Count == 2 && restored.Engine!.Rounds.Count == 2, "Undo empty shoe never undoes engine");
        restored.ResetStrategy();
        restored.Record(Outcome.Player);
        var afterReset = DashboardWorkspace.Deserialize(restoredWorkspace.Serialize()).Selected!;
        afterReset.Undo();
        afterReset.Undo();
        Assert(afterReset.Engine!.Rounds.Count == 0 && afterReset.Profit == 10, "Undo across reset boundary after restart");
        restored.Clear();
        Assert(DashboardWorkspace.Deserialize(restoredWorkspace.Serialize()).Selected!.Hands.Count == 0, "Cleared session restores");

        var observed = new DashboardWorkspace();
        observed.CreateSession();
        observed.Selected!.Clear();
        foreach (var outcome in new[] { Outcome.Player, Outcome.Tie, Outcome.Banker, Outcome.Tie, Outcome.Player }) observed.Selected.Record(outcome);
        Assert(DashboardStatistics.Calculate(observed.Selected).Chop == 3, "Ties ignored in chop");
        Assert(DashboardWorkspace.Deserialize(observed.Serialize()).Selected!.Engine is null, "Observations do not invent bets");

        foreach (var rules in new[]
        {
            definition with { PauseAfterConsecutiveLosses = 1, PauseRounds = 2 },
            definition with { StopLoss = 5 },
            definition with { AfterLoss = new("PATTERN", AmountRuleKind.Pattern, new[] { 2m }) }
        })
        {
            var engine = new StrategySessionEngine(rules);
            engine.Record(Outcome.Banker);
            var copy = StrategySessionEngine.RestoreArchive(engine.ExportArchive());
            Assert(copy.Recommendation == engine.Recommendation && copy.Profit == engine.Profit, "Paused/stopped/manual pending state restores");
            if (copy.Recommendation.State == StrategyEngineState.AwaitingSide)
            {
                copy.ChooseSide(BetSide.Banker);
                copy.SetManualStake(7.5m);
                copy = StrategySessionEngine.RestoreArchive(copy.ExportArchive());
                copy.Record(Outcome.Banker);
                Assert(copy.Profit == -2.875m, "Protected amount manual resolution persists without inventing a rule");
            }
        }

        void Reject(Action<JsonObject> change)
        {
            var node = JsonNode.Parse(encoded)!.AsObject();
            change(node);
            ExpectInvalid(() => DashboardWorkspace.Deserialize(Encoding.UTF8.GetBytes(node.ToJsonString())));
        }
        Reject(n => n["Version"] = 99);
        Reject(n => n["Extra"] = true);
        Reject(n => n.Remove("Selected"));
        Reject(n => n["Selected"] = Guid.NewGuid());
        Reject(n => n["Sessions"]![0]!["Hands"]![0]!["SessionProfit"] = 999);
        Reject(n => n["Sessions"]![0]!["ShoeStarts"]![1] = 0);
        Reject(n => n["Sessions"]![0]!["Engine"]!["Definition"]!["Initial"] = null);
        Reject(n => n["Sessions"]![0]!["Engine"]!["Definition"]!["Initial"]!["AmountRule"] = 999);
        Reject(n => n["Sessions"]![0]!["Engine"]!["Decisions"]![0]!["ManualSide"] = 999);
        Reject(n => n["Sessions"]![0]!["Engine"]!["Decisions"]!.AsArray().RemoveAt(0));
        Reject(n => n["Sessions"]![0]!["Engine"]!["Payouts"]!["BankerWinProfit"] = 1);
        ExpectInvalid(() => DashboardWorkspace.Deserialize(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(encoded).Replace("\"Version\":1", "\"Version\":1,\"Version\":1", StringComparison.Ordinal))));
        ExpectInvalid(() => DashboardWorkspace.Deserialize(new byte[DashboardWorkspace.MaximumArchiveBytes + 1]));

        var directory = Path.Combine(Path.GetTempPath(), "baccarat-workspace-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "workspace.json");
            using (var store = new WorkspaceFileStore(path))
            {
                store.Save(encoded);
                try { using var competing = new WorkspaceFileStore(path); throw new Exception("Two writers acquired the same workspace"); }
                catch (IOException) { }
                store.Save(observed.Serialize());
                Assert(store.Read(backup: true)!.SequenceEqual(encoded), "Atomic replacement retains previous save");
                File.WriteAllText(path, "corrupt fixture");
            }
            var window = new DashboardWindow(workspacePath: path);
            window.Show();
            Assert(window.Dashboard.Workspace.Selected!.Profit == -1, "Valid backup is loaded after primary corruption");
            Assert(!Wait(window.SaveWorkspaceAsync()), "Recovery requires explicit confirmation");
            Assert(File.ReadAllText(path) == "corrupt fixture", "Invalid primary not overwritten automatically");
            Assert(Wait(window.SaveWorkspaceAsync(recover: true)), "Explicit recovery saves validated backup");
            Assert(Directory.GetFiles(directory, "*.rejected-*").Length == 1 &&
                File.ReadAllText(Directory.GetFiles(directory, "*.rejected-*")[0]) == "corrupt fixture", "Rejected original retained");
            window.Dashboard.Workspace.Selected!.Undo();
            window.Dashboard.RefreshSharedState();
            PumpUntil(() => DashboardWorkspace.Deserialize(File.ReadAllBytes(path)).Selected!.Profit == 29);
            Assert(Wait(window.SaveWorkspaceAsync()), "Debounced autosave saves shared edits");
            window.Close();
            Dispatcher.UIThread.RunJobs();
            var reopened = new DashboardWindow(workspacePath: path);
            reopened.Show();
            Assert(reopened.Dashboard.Workspace.Selected!.Profit == 29 && reopened.Dashboard.Workspace.Selected.Engine!.Recommendation.Stake == 30,
                "Application restart resumes exact engine/ledger state");
            reopened.Dashboard.Workspace.Selected.Record(Outcome.Player);
            reopened.Dashboard.RefreshSharedState();
            reopened.Close();
            PumpUntil(() => !reopened.IsVisible);
            Assert(DashboardWorkspace.Deserialize(File.ReadAllBytes(path)).Selected!.Profit == 59, "Closing flushes unsaved changes");

            // Disk failure must retain the previous file and refuse a silent close.
            var failurePath = Path.Combine(directory, "failure.json");
            var failing = new DashboardWindow(workspacePath: failurePath);
            failing.Show();
            failing.Dashboard.Workspace.CreateSession();
            failing.Dashboard.RefreshSharedState();
            Assert(Wait(failing.SaveWorkspaceAsync()), "Initial failure fixture save");
            var before = File.ReadAllBytes(failurePath);
            Directory.CreateDirectory(failurePath + ".bak");
            failing.Dashboard.Workspace.Selected!.Record(Outcome.Player);
            failing.Dashboard.RefreshSharedState();
            Assert(!Wait(failing.SaveWorkspaceAsync()) && before.SequenceEqual(File.ReadAllBytes(failurePath)), "Failed replacement preserves old file");
            failing.Close();
            PumpUntil(() => failing.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "Close without saving")));
            Assert(failing.IsVisible, "Failed save cancels closing");
            var discard = failing.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Close without saving"));
            discard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("PASS: shoes/statistics, exact restart/Undo, strict archives, backup recovery, single writer, atomic-save failure and close protection");
    }

    private static bool Wait(Task<bool> task) { PumpUntil(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void PumpUntil(Func<bool> done)
    {
        if (done()) return;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var check = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        check.Tick += (_, _) => { if (done()) stop.Cancel(); };
        check.Start();
        try
        {
            // Run the actual dispatcher loop so timers (including autosave) are tested,
            // rather than draining only already-queued jobs in the headless platform.
            Dispatcher.UIThread.MainLoop(stop.Token);
        }
        finally { check.Stop(); }
        if (!done()) throw new Exception("Workspace check timed out");
    }
    private static void ExpectInvalid(Action action)
    {
        try { action(); throw new Exception("Invalid workspace accepted"); }
        catch (InvalidDataException) { }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
