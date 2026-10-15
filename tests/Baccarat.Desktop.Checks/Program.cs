using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Baccarat.Core;
using Baccarat.Core.Automation;
using Baccarat.Core.Strategies;
using Baccarat.Desktop;
using Baccarat.Desktop.Views;
using Baccarat.Infrastructure.Automation;
using Baccarat.Infrastructure.Authentication;
using Baccarat.Infrastructure.Strategies;

AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var strategyDefinition = new StrategyDefinition(
    "UI Fixture", null,
    new StrategyRule("PLAYER", AmountRuleKind.Set, [1m]),
    new StrategyRule("WINNER", AmountRuleKind.ResetAmount, [2m]),
    new StrategyRule("WINNER", AmountRuleKind.MultiplyAmount, [2m]),
    null, null, null, null, 0);
var controller = new AutomationController(new SimulatedAutomationTableAdapter());
var window = new MainWindow(initialStrategy: strategyDefinition, automationController: controller);
window.Show();
window.UpdateLayout();
CheckButtonAlignment(window);
Button FindButton(string name) => window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, name));
void Click(string name)
{
    var button = FindButton(name);
    if (!button.IsEnabled) throw new Exception($"Cannot click disabled action: {name}");
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    window.UpdateLayout();
    CheckButtonAlignment(window);
}
bool HasText(string value) => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains(value) == true);
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
void CheckButtonAlignment(Window host)
{
    foreach (var button in host.GetVisualDescendants().OfType<Button>().Where(b => b.GetType() == typeof(Button) && b.IsVisible))
    {
        Check(button.HorizontalContentAlignment == HorizontalAlignment.Center &&
            button.VerticalContentAlignment == VerticalAlignment.Center,
            $"Button '{button.Content}' must center content on both axes");
        var presenter = button.GetVisualDescendants().OfType<ContentPresenter>()
            .Single(p => p.Name == "PART_ContentPresenter");
        Check(presenter.HorizontalContentAlignment == HorizontalAlignment.Center &&
            presenter.VerticalContentAlignment == VerticalAlignment.Center,
            $"Button '{button.Content}' template must preserve centered alignment");
        Check(button.Padding.Left == button.Padding.Right && button.Padding.Top == button.Padding.Bottom,
            $"Button '{button.Content}' must have symmetric padding");
    }
}
void CheckNoAccountGate()
{
    Check(!window.GetVisualDescendants().OfType<TabControl>().Any(), "No tab navigation should be shown");
    Check(!window.GetVisualDescendants().OfType<AccountView>().Any(), "V1 must not show an account screen");
    Check(!window.GetVisualDescendants().OfType<Button>().Any(b =>
        b.Content is "SIGN IN" or "SIGN UP" or "SIGN OUT"), "V1 must not expose authentication actions");
}
CheckNoAccountGate();
Check(HasText("WORKSPACE HOME") && HasText("No account or activation required"), "Launch must open Home directly");
Check(!window.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "Back to home")), "Home must not have a Back button");
Click("OPEN SESSION");
var back = FindButton("Back to home");
Check(DockPanel.GetDock(back) == Dock.Top && back.BorderThickness.Left == 1 && back.Margin.Left == 24, "Back button layout incorrect");
Check(window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Session tracker").HorizontalAlignment == HorizontalAlignment.Center,
    "Workspace headings must be centered");
Check(HasText("TABLE WORKSPACE") && HasText("RECORD TABLE RESULT") && HasText("ROUND HISTORY"), "Session sections missing");
Check(!FindButton("Undo last").IsEnabled && !FindButton("Export CSV").IsEnabled, "Empty session actions must be disabled");
Click("+ Player");
Click("+ Banker");
Click("+ Tie");
Check(HasText("3 rounds"), "Recording did not update UI");
Click("Undo last");
Click("Undo last");
Check(HasText("1 rounds") && FindButton("Export CSV").IsEnabled, "Undo/export state did not update");

Click("Back to home");
Click("OPEN STRATEGIES");
Check(HasText("STRATEGY WORKSPACE") && HasText("CURRENT RECOMMENDATION") && HasText("STRATEGY HISTORY"), "Strategy sections missing");
Check(HasText("Bet 1 on Player."), "Initial strategy recommendation missing");
Click("Banker result");
Check(HasText("Bet 2 on Banker.") && HasText("Profit -1.00"), "Loss progression incorrect");
Check(FindButton("Undo strategy round").IsEnabled && FindButton("Export strategy CSV").IsEnabled, "Strategy tools disabled");
Click("Undo strategy round");
Check(HasText("Profit 0.00") && HasText("Bet 1 on Player."), "Undo did not restore strategy");

Click("Back to home");
Click("OPEN AUTOMATION");
Check(HasText("Controlled automation") && HasText("Local simulation is the only enabled adapter"), "Simulation boundary missing");
Click("Connect simulation");
Check(HasText("Simulation table connected."), "Simulation did not connect");
Click("Prepare initial bet");
Check(HasText("Approve 1 on Player."), "Initial bet must require approval");
Click("Approve pending bet");
Check(HasText("Simulation accepted 1 on Player.") && HasText("Accepted bets 1"), "Initial simulation failed");
Click("Enable auto-bet");
Click("Back to home");
Click("OPEN STRATEGIES");
Click("Banker result");
Click("Back to home");
Click("OPEN AUTOMATION");
Check(HasText("Approve 2 on Banker."), "Next recommendation did not reach approval");
Click("Approve pending bet");
Check(HasText("Simulation accepted 2 on Banker.") && HasText("Accepted bets 2"), "Banker simulation failed");

Click("Back to home");
Click("OPEN VIEWER");
Check(HasText("VIEWER WORKSPACE") && HasText("DOCUMENT HISTORY"), "Viewer sections missing");
Check(HasText("Protocol 1.0") && HasText("Rounds 1 · Player 1 · Banker 0 · Tie 0"), "Session snapshot incorrect");
Check(HasText("UI Fixture · Profit -1.00"), "Strategy snapshot incorrect");
Check(HasText("Ready · Auto enabled · Simulation · Accepted 2 · Stake 3.00"), "Automation snapshot incorrect");
CheckNoAccountGate();

// This fixture reproduces the supplied configuration, not its unknown protected algorithm.
using var patternStream = new MemoryStream(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "pattern-strategy.json")));
var pattern = new StrategyFileLoader().LoadAsync(patternStream).GetAwaiter().GetResult();
Click("Back to home");
Click("OPEN STRATEGIES");
window.GetVisualDescendants().OfType<StrategyView>().Single().Load(pattern);
window.UpdateLayout();
Check(FindButton("Use Player").IsEnabled && !FindButton("Player result").IsEnabled, "PATTERN must await a manual side");
Click("Use Player");
Check(HasText("Bet 100 on Player."), "PATTERN configured initial amount must be 100");
Click("Player result");
Check(HasText("Profit 100.00") && !FindButton("Player result").IsEnabled, "PATTERN win must await manual resolution");
Check(controller.Snapshot.State != AutomationRunState.AwaitingApproval, "Unresolved PATTERN must not prepare a bet");
Click("Use Banker");
Check(HasText("Enter a stake for amount rule 'Pattern'."), "Pattern amount must remain manual");
var stake = window.GetVisualDescendants().OfType<TextBox>().Single(t => Equals(t.Watermark, "Manual stake"));
stake.Text = "-2";
Click("Apply stake");
Check(HasText("Enter a positive stake") && !FindButton("Banker result").IsEnabled, "Invalid stake accepted");
stake.Text = "7.5";
Click("Apply stake");
Check(HasText("Bet 7.5 on Banker."), "Manual decimal stake not applied");
Check(controller.Snapshot.State == AutomationRunState.AwaitingApproval,
    "Auto-bet must wait for manual resolution, then prepare the now-ready recommendation");
Click("Back to home");
Click("OPEN AUTOMATION");
Check(HasText("Approve 7.5 on Banker."), "Resolved PATTERN recommendation not propagated");
Click("Back to home");
Click("OPEN STRATEGIES");
Click("Tie result");
Check(HasText("Profit 100.00") && HasText("Bet 7.5 on Banker."), "Replacement tie-push default changed");
Click("Banker result");
Check(HasText("Profit 107.13"), "Banker commission did not appear in UI");
Click("Undo strategy round");
Check(HasText("Profit 100.00") && HasText("Bet 7.5 on Banker."), "Undo must restore manual decisions");
Click("Reset strategy");
Check(HasText("Profit 0.00") && FindButton("Use Player").IsEnabled && !FindButton("Export strategy CSV").IsEnabled, "Reset state incorrect");
Check(controller.Snapshot.State != AutomationRunState.AwaitingApproval, "Reset must invalidate approval");
Click("Back to home");
Click("OPEN VIEWER");
Check(HasText("PATTERN · Profit 0.00 · AwaitingSide"), "Reset snapshot not propagated");

// Exact settlement/export are checked separately from rounded display labels.
var patternEngine = new StrategySessionEngine(pattern);
patternEngine.ChooseSide(BetSide.Banker);
patternEngine.Record(Outcome.Banker);
patternEngine.ChooseSide(BetSide.Player);
patternEngine.SetManualStake(7.5m);
patternEngine.Record(Outcome.Banker);
Check(patternEngine.Profit == 87.5m && patternEngine.Recommendation.State == StrategyEngineState.AwaitingSide, "Manual loss settlement incorrect");
Check(patternEngine.ToCsv().Contains("1,Banker,PATTERN,Banker,100,95,95,True,True", StringComparison.Ordinal) &&
    patternEngine.ToCsv().Contains("2,Banker,PATTERN,Player,7.5,-7.5,87.5,True,False", StringComparison.Ordinal), "PATTERN CSV incorrect");

// Close replaces sign-out cleanup in V1.
Click("Back to home");
Click("OPEN STRATEGIES");
Click("Use Player");
Click("Back to home");
Click("OPEN AUTOMATION");
Click("Prepare initial bet");
Check(controller.Snapshot.State == AutomationRunState.AwaitingApproval, "Close fixture missing approval");
window.Close();
Check(controller.Snapshot.State == AutomationRunState.Paused && !controller.Snapshot.AutoBetEnabled,
    "Close must cancel pending simulation and disable auto-bet");
Check(controller.ApprovePendingAsync().GetAwaiter().GetResult().Outcome != AutomationActionOutcome.Accepted,
    "Closed workspace must not retain an approvable bet");
window = new MainWindow();
window.Show();
window.UpdateLayout();
CheckNoAccountGate();
Check(HasText("WORKSPACE HOME"), "Relaunch must not require stored credentials");
Click("OPEN STRATEGIES");
Check(HasText("No strategy loaded") && !FindButton("Player result").IsEnabled, "Fresh launch must be empty and safe");
window.Close();
Console.WriteLine("PASS: no-login startup/relaunch, session, strategies, PATTERN manual flow, simulation, viewer, reset, export data and close cleanup");

// Account screens remain outside the default offline workflow, but share the button theme.
var accountView = new AccountView(new OfflineAuthenticationService());
var accountWindow = new Window { Width = 900, Height = 760, Content = accountView };
accountWindow.Show();
accountWindow.UpdateLayout();
CheckButtonAlignment(accountWindow);
accountView.ShowSignUp();
accountWindow.UpdateLayout();
CheckButtonAlignment(accountWindow);
foreach (var button in accountWindow.GetVisualDescendants().OfType<Button>().Where(b => b.GetType() == typeof(Button)))
    Check(button.Padding.Top == 0 && button.Padding.Bottom == 0,
        "Fixed-height account buttons must reserve enough vertical space for their labels");
accountWindow.Close();
Console.WriteLine("PASS: horizontal and vertical button alignment across workspaces and account forms");
