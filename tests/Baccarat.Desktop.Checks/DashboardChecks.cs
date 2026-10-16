using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Baccarat.Core;
using Baccarat.Core.Strategies;
using Baccarat.Infrastructure.Strategies;
using Baccarat.Desktop;
using Baccarat.Desktop.ViewModels;
using Baccarat.Desktop.Views;

internal static class DashboardChecks
{
    internal static void Run(Action<Window> checkAlignment, string? screenshotDirectory)
    {
        var window = new DashboardWindow(persistenceEnabled: false);
        window.Show();
        Assert(window.Icon is not null, "Dashboard window must carry the product icon");
        Layout();
        var view = window.Dashboard;
        var workspace = view.Workspace;
        Button Button(string label) => window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label));
        T Named<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        bool Text(string text) => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains(text, StringComparison.Ordinal) == true);
        void Layout() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        void Click(string label)
        {
            var button = Button(label);
            Assert(button.IsEnabled, $"Disabled dashboard button: {label}");
            button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Layout();
            checkAlignment(window);
        }
        void Capture(string name)
        {
            if (screenshotDirectory is null) return;
            Layout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            using var frame = window.CaptureRenderedFrame() ?? throw new Exception("No rendered dashboard frame");
            Directory.CreateDirectory(screenshotDirectory);
            frame.Save(Path.Combine(screenshotDirectory, name + ".png"));
        }

        Assert(workspace.Selected is null && Text("No Session"), "Default dashboard must start empty");
        Assert(!window.GetVisualDescendants().OfType<AccountView>().Any(), "Default dashboard must not require sign-in");
        Capture("dashboard-empty");
        Click("New Session");
        var first = workspace.Selected!;
        Assert(workspace.Sessions.Count == 1 && first.Strategy.Name == "PATTERN", "New session must prioritize PATTERN");
        Assert(!Button("Undo").IsEnabled && !Button("Export CSV").IsEnabled && !Button("New Shoe").IsEnabled,
            "Empty/unfinished actions must be disabled");
        Assert(Text("Not calculated") && Text("Roadmaps · Current shoe"), "Roadmaps must be available independently of strategy calculations");
        Named<CheckBox>("BankerPair").IsChecked = true;
        Click("Banker");
        Click("Player");
        Click("Tie");
        var roadmaps = window.GetVisualDescendants().OfType<RoadmapsView>().Single();
        Assert(roadmaps.Snapshot.Beads.Count == 3 && roadmaps.Snapshot.BigRoad.Count == 2 &&
            roadmaps.Snapshot.BigRoad[^1].Ties == 1 && roadmaps.Snapshot.Beads[0].BankerPair,
            "Dashboard roads must reflect recorded results, pair flags and ties");
        Assert(first.Hands.Count == 3 && first.Hands[0].BankerPair && !first.Hands[1].BankerPair,
            "Pair flags must be captured per hand and cleared after recording");
        Assert(Text("Hand 3") && Text("33.3%"), "Heading and counts must update");
        Click("Undo");
        Assert(roadmaps.Snapshot.Beads.Count == 2 && roadmaps.Snapshot.BigRoad[^1].Ties == 0,
            "Undo must remove the tie overlay and bead");
        Assert(first.Count(Outcome.Tie) == 0 && Text("50.0%") && Button("Export CSV").IsEnabled, "Undo must update statistics");
        Assert(first.ToCsv().Contains("1,Banker,True,False", StringComparison.Ordinal), "Export must preserve pair flags");
        Click("New Shoe");
        Assert(roadmaps.Snapshot.Beads.Count == 0 && roadmaps.Snapshot.BigRoad.Count == 0,
            "New shoe must clear all roads");
        Assert(first.CurrentShoe == 2 && Text("Hand 0") && !Button("New Shoe").IsEnabled, "New shoe updates heading and blocks empty repeats");
        Click("Undo");
        Assert(roadmaps.Snapshot.Beads.Count == 2, "Undo of a new shoe must restore its roads");
        Assert(first.CurrentShoe == 1 && first.Hands.Count == 2, "Undo shoe boundary retains hands");

        var unit = Named<TextBox>("BaseUnit");
        unit.Text = "-1";
        Click("Apply settings");
        Assert(first.BaseUnit == 1 && Text("must be positive"), "Invalid draft must not change applied settings");
        unit.Text = "2.5";
        unit.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Layout();
        Assert(first.BaseUnit == 2.5m && !first.HasUnappliedSettings, "Enter must apply valid settings");
        Named<TextBox>("Setting_limit").Text = "3";
        Click("Apply settings");
        Named<ComboBox>("StrategySelector").SelectedItem = "Sequence";
        Layout();
        Named<TextBox>("Setting_sides").Text = "B,X";
        Click("Apply settings");
        Assert(Text("Check Sides"), "Invalid sequence side must be rejected");
        Named<TextBox>("Setting_sides").Text = "B,P,P,B";
        Named<TextBox>("Setting_stakes").Text = "1,2,3";
        Click("Apply settings");
        Assert(first.SidebarLabel.Contains("Sequence", StringComparison.Ordinal) && Text("Sequence · Shoe"), "Strategy labels must synchronize");
        Named<ComboBox>("StrategySelector").SelectedItem = "PATTERN";
        Layout();
        Assert(Named<TextBox>("Setting_limit").Text == "3", "Per-strategy fields must survive strategy switching");
        Named<TextBox>("BaseUnit").Text = "4"; // Deliberately un-applied draft survives a session switch.
        Named<CheckBox>("PlayerPair").IsChecked = true;
        Click("+ New session");
        var second = workspace.Selected!;
        Assert(second != first && second.Hands.Count == 0 && second.BaseUnit == 1, "Sessions must be independent");
        Click("Player");
        Named<ListBox>("SessionList").SelectedItem = first;
        Layout();
        Assert(workspace.Selected == first && first.Hands.Count == 2 && second.Hands.Count == 1, "Switching must preserve histories");
        Assert(Named<TextBox>("BaseUnit").Text == "4" && Named<CheckBox>("PlayerPair").IsChecked == true,
            "Switching must preserve drafts without applying them");
        Assert(first.BaseUnit == 2.5m && Text("Unapplied"), "Switching must not silently commit draft settings");
        Click("Apply settings");
        Capture("dashboard-pattern");
        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(s => s.Content is Control { Name: "DashboardContent" });
        scroll.Offset = new Vector(0, scroll.Extent.Height);
        Layout();
        Capture("dashboard-history");
        Assert(Text("Banker · BP") && Text("SHOE 1"), "History and statistics must remain accessible by scrolling");
        scroll.Offset = new Vector(0, 0);
        foreach (var definition in DashboardSession.Strategies)
        {
            Named<ComboBox>("StrategySelector").SelectedItem = definition.Name;
            Layout();
            foreach (var field in definition.Fields) _ = Named<TextBox>($"Setting_{field.Key}");
            Assert(Text($"{definition.Name} · Shoe"), "All strategy selections must update the header");
        }
        Capture("dashboard-odd-is-god");
        Click("☰");
        Assert(!Named<Border>("SessionSidebar").IsVisible, "Sidebar toggle must hide sidebar");
        window.Width = 800;
        window.Height = 600;
        Layout();
        Assert(window.Bounds.Width == 800 && window.Bounds.Height == 600, "Compact window size not applied");
        Assert(Button("Banker").Bounds.Width > 50 && Button("Tools").Bounds.Width > 50, "Compact controls must stay usable");
        Capture("dashboard-compact");
        Click("☰");
        Assert(Named<Border>("SessionSidebar").IsVisible, "Sidebar must reopen");
        window.Width = 1280;
        window.Height = 860;
        Click("Tools");
        Assert(!window.OwnedWindows.Any() && Text("Same session, same records"), "Tools must navigate inside the same window");
        Assert(Named<Border>("ToolsSidebar").IsVisible && Named<Border>("ToolsSidebar").HorizontalAlignment == HorizontalAlignment.Right,
            "Tools must open as a right-hand sidebar over the retained workspace");
        Capture("dashboard-tools-sidebar");
        Click("Close Tools");
        Assert(!Named<Border>("ToolsSidebar").IsVisible, "Tools sidebar must close without replacing the dashboard");
        Click("Tools");
        Capture("tools-menu");
        Click("Strategies");
        Capture("tools-strategies");
        var strategyView = window.GetVisualDescendants().OfType<StrategyView>().Single();
        var sharedDefinition = new StrategyDefinition("Shared fixture", null,
            new StrategyRule("PLAYER", AmountRuleKind.Set, new[] { 1m }),
            new StrategyRule("WINNER", AmountRuleKind.ResetAmount, new[] { 1m }),
            new StrategyRule("WINNER", AmountRuleKind.MultiplyAmount, new[] { 2m }), null, null, null, null, 0);
        strategyView.Load(sharedDefinition);
        Layout();
        Assert(first.Engine is not null && Text("Bet 1 on Player."), "Tools must load the selected session's engine");
        Click("Player result");
        Assert(first.Hands.Count == 3 && first.Engine!.Rounds.Count == 1 && first.Profit == 1,
            "Tool result must appear exactly once in the shared session");
        Click("Back to dashboard");
        Assert(Text("Bet 1 on Player.") && Text("Session P/L  1.00") && Text("Shared fixture · Shoe"),
            "Dashboard must display the existing engine's live recommendation and profit");
        Assert(!Named<ComboBox>("StrategySelector").IsEnabled, "Draft controls must not override imported rules");
        Click("Banker");
        Assert(first.Hands.Count == 4 && first.Engine!.Rounds.Count == 2 && first.Profit == 0, "Dashboard result must update the shared engine");
        Click("Tools");
        Click("Strategies");
        Assert(ReferenceEquals(strategyView, window.GetVisualDescendants().OfType<StrategyView>().Single()), "Tools must retain the same feature view and state");
        Assert(Text("Bet 2 on Banker."), "Dashboard changes must propagate into strategy tools");
        Click("Undo strategy round");
        Assert(first.Hands.Count == 3 && first.Engine!.Rounds.Count == 1 && first.Profit == 1, "Tool Undo must undo both histories");
        Click("Back to dashboard");
        Click("Undo");
        Assert(first.Hands.Count == 2 && first.Engine!.Rounds.Count == 0 && first.Profit == 0, "Dashboard Undo must undo both histories");
        Click("Tools");
        Click("Viewer");
        Capture("tools-viewer");
        Assert(Text("Rounds 2 · Player 1 · Banker 1 · Tie 0") && Text("Shared fixture · Profit 0.00"),
            "Viewer must receive the selected dashboard session and engine snapshots");
        Click("Tools");
        Click("Automation");
        Capture("tools-automation");
        Click("Connect simulation");
        Click("Prepare initial bet");
        Assert(Text("Approve 1 on Player."), "Shared engine must feed existing approval workflow");
        Click("Enable auto-bet");
        Click("Back to dashboard");
        Named<ListBox>("SessionList").SelectedItem = second;
        Layout();
        Click("Tools");
        Click("Viewer");
        Assert(Text("Rounds 1 · Player 1 · Banker 0 · Tie 0") && !Text("Shared fixture · Profit"), "Another session must not show the previous session's viewer state");
        Click("Tools");
        Click("Clear session…");
        Click("Cancel");
        Assert(second.Hands.Count == 1, "Cancel must preserve results");
        Click("Clear session…");
        Click("Clear selected session");
        Assert(second.Hands.Count == 0 && first.Hands.Count == 2, "Confirmed clear must affect only the selected session");
        Assert(window.Title == "Baccarat Workspace" && Text("Session 0 hands"), "Clear must return to the dashboard with visibly empty results");
        Named<ListBox>("SessionList").SelectedItem = first;
        Layout();
        Click("Tools");
        Click("Automation");
        Assert(!Button("Approve pending bet").IsEnabled,
            "Switching sessions must invalidate pending approval");
        Assert(Button("Enable auto-bet").IsEnabled, "Session switching must disable auto-bet");
        Click("Prepare initial bet");
        Click("Approve pending bet");
        Assert(Text("Accepted bets 1"), "Retained simulation must still operate after switching back");
        Click("Enable auto-bet");
        Click("Back to dashboard");
        Click("Banker");
        Click("Tools");
        Click("Automation");
        Assert(Text("Approve 2 on Banker.") && Button("Approve pending bet").IsEnabled,
            "Dashboard outcome must feed the existing auto-bet approval workflow");
        Click("Back to dashboard");
        Click("Undo");
        Click("Tools");
        Click("Automation");
        Assert(!Button("Approve pending bet").IsEnabled && first.Profit == 0,
            "Shared Undo must invalidate a pending bet and roll back settlement");
        Click("Tools");
        Click("Strategies");
        using var patternStream = new MemoryStream(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "pattern-strategy.json")));
        strategyView.Load(new StrategyFileLoader().LoadAsync(patternStream).GetAwaiter().GetResult());
        Layout();
        Click("Back to dashboard");
        Assert(!Button("Player").IsEnabled && Text("Choose Player or Banker"), "Unknown PATTERN selector must remain guarded on dashboard");
        Click("Tools");
        Click("Strategies");
        Click("Use Player");
        Click("Back to dashboard");
        Click("Player");
        Assert(first.Profit == 100 && !Button("Banker").IsEnabled, "Existing PATTERN manual behavior must be reused, not replaced");
        Capture("dashboard-integrated");
        Click("Tools");
        Click("Strategies");
        Click("Reset strategy");
        Assert(first.Hands.Count == 3 && first.Profit == 100 && first.Engine!.Rounds.Count == 0,
            "Strategy reset must retain previously recorded results and their settlement");
        Click("Back to dashboard");
        Click("Undo");
        Assert(first.Hands.Count == 2 && first.Profit == 0 && first.Engine!.Rounds.Count == 0,
            "Undo across a strategy-run boundary must not undo the new engine");
        Click("Tools");
        Click("Sing In / Up");
        Capture("tools-account-signin");
        Assert(window.GetVisualDescendants().OfType<AccountView>().Any(), "Original account feature must remain reachable in the same window");
        Click("SIGN UP");
        Capture("tools-account-signup");
        Assert(window.GetVisualDescendants().OfType<TextBox>().Count() == 5, "Original signup fields must be retained");
        Click("Back to dashboard");
        Click("Tools");
        Click("Strategies");
        Click("Use Player");
        Click("Tools");
        Click("Automation");
        Click("Prepare initial bet");
        Click("Back to dashboard");
        Click("New Shoe");
        Click("Tools");
        Click("Automation");
        Assert(!Button("Approve pending bet").IsEnabled, "New Shoe must cancel pending simulation approval");
        Click("Prepare initial bet");
        var pendingAtClose = Button("Approve pending bet");
        Assert(pendingAtClose.IsEnabled, "Close fixture must have a pending approval");
        window.Close();
        Assert(!pendingAtClose.IsEnabled, "Closing the single shell must invalidate simulation approval");
        Assert(!window.OwnedWindows.Any(), "Tools must never create separate application windows");

        var fresh = new DashboardWorkspace();
        fresh.CreateSession();
        try { fresh.Select(first); throw new Exception("Foreign session accepted"); }
        catch (ArgumentException) { }
        var session = fresh.Selected!;
        foreach (var invalid in new[] { "0", "-1", "NaN", "1,000", "1e10", "1000000001" })
        {
            session.BaseUnitDraft = invalid;
            Assert(!session.ApplySettings(out _), "Invalid amount accepted");
        }
        session.BaseUnitDraft = "1";
        session.SelectStrategy("Sequence");
        session.SetField("stakes", "1,,3");
        Assert(!session.ApplySettings(out _), "Empty stake-list entry accepted");
        session.SetField("stakes", string.Join(',', Enumerable.Repeat("1", 65)));
        Assert(!session.ApplySettings(out _), "Oversized stake list accepted");
        for (var i = 0; i < 10000; i++) Assert(session.Record(Outcome.Player), "Premature hand limit");
        Assert(!session.Record(Outcome.Banker) && session.Hands.Count == 10000, "Hand capacity must be bounded");
        session.Undo();
        Assert(session.Record(Outcome.Tie), "Undo must free capacity");
        for (var i = 1; i < 100; i++) Assert(fresh.CreateSession(), "Premature session limit");
        Assert(!fresh.CreateSession(), "Session capacity must be bounded");
        var videoWorkspace = new DashboardWorkspace();
        videoWorkspace.CreateSession();
        var video = videoWorkspace.Selected!;
        video.SetField("first", "P");
        Assert(video.ApplySettings(out _) && video.Engine?.Recommendation.Side == BetSide.Player,
            "PATTERN must expose only the user-selected opening side");
        video.Record(Outcome.Tie);
        Assert(video.Engine?.Recommendation.Side == BetSide.Player, "A tie must not advance the opening bet");
        video.Record(Outcome.Player);
        Assert(video.Engine?.Recommendation.State == StrategyEngineState.AwaitingSide,
            "PATTERN must not invent an unseen post-win transition");
        video.SelectStrategy("Martingale");
        video.SetField("first", "P");
        Assert(video.ApplySettings(out _) && video.Engine?.Recommendation.Side == BetSide.Player,
            "Selecting Martingale must activate a fresh run");
        video.Record(Outcome.Banker);
        Assert(video.Engine?.Recommendation.Side == BetSide.Banker && video.Engine.Recommendation.Stake == 2,
            "Martingale loss must use the observed winner and doubled stake");
        video.SelectStrategy("Sequence");
        video.SetField("sides", "B,P,P,B");
        video.SetField("stakes", "1,2,3");
        Assert(video.ApplySettings(out _) && video.Engine?.Recommendation.Side == BetSide.Banker,
            "Sequence must activate the configured first side");
        video.Record(Outcome.Player);
        Assert(video.Engine?.Recommendation.Side == BetSide.Player && video.Engine.Recommendation.Stake == 2,
            "Sequence loss must advance both observed indices");
        var sequenceEngine = video.Engine;
        Assert(video.ApplySettings(out _) && ReferenceEquals(sequenceEngine, video.Engine),
            "Reapplying unchanged settings must not silently reset a running strategy");
        video.BeginDraftRun();
        Assert(video.Engine is null && video.Hands.Count == 4,
            "Returning to drafts must preserve the previous settled hand history");
        video.SelectStrategy("Martingale");
        video.SetField("start", "3");
        video.SetField("multiplier", "2.5");
        Assert(video.ApplySettings(out _) && video.Engine?.Recommendation.Stake == 3,
            "Configured starting stake must reach the shared engine");
        video.Record(Outcome.Banker);
        Assert(video.Engine?.Recommendation.Stake == 7.5m, "Configured multiplier must apply after a loss");
        video.Record(Outcome.Banker);
        Assert(video.Engine?.Recommendation.Stake == 3, "A win must reset to configured starting stake");
        video.SetField("multiplier", "0");
        Assert(!video.ApplySettings(out _) && video.Engine?.Recommendation.Stake == 3,
            "Invalid multiplier must not replace the active engine");
        Console.WriteLine("PASS: single-window Tools, shared records/engine/undo, viewer isolation, approval cancellation, guarded PATTERN, clear, account access and dashboard checks");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
