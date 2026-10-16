using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Baccarat.Core;
using Baccarat.Core.Authentication;
using Baccarat.Core.Automation;
using Baccarat.Core.Strategies;
using Baccarat.Desktop;
using Baccarat.Desktop.Views;
using Baccarat.Infrastructure.Automation;
using Baccarat.Infrastructure.Authentication;
using Baccarat.Infrastructure.Strategies;
using Baccarat.Infrastructure.Security;

var screenshotDirectory = Environment.GetEnvironmentVariable("BACCARAT_SCREENSHOT_DIR");
var builder = AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = screenshotDirectory is null });
if (screenshotDirectory is not null) builder.UseSkia();
builder.SetupWithoutStarting();
var strategyDefinition = new StrategyDefinition(
    "UI Fixture", null,
    new StrategyRule("PLAYER", AmountRuleKind.Set, [1m]),
    new StrategyRule("WINNER", AmountRuleKind.ResetAmount, [2m]),
    new StrategyRule("WINNER", AmountRuleKind.MultiplyAmount, [2m]),
    null, null, null, null, 0);
var controller = new AutomationController(new SimulatedAutomationTableAdapter());
// Successful authentication is injected test data, never a production server substitute.
var window = new MainWindow(initialStrategy: strategyDefinition, automationController: controller,
    authenticationService: new DemoAuthenticationService());
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
void CheckAuthenticatedWorkspace()
{
    Check(!window.GetVisualDescendants().OfType<TabControl>().Any(), "No tab navigation should be shown");
    Check(!window.GetVisualDescendants().OfType<AccountView>().Any(), "Account screen must be hidden after authentication");
    Check(Grid.GetColumn(FindButton("SIGN OUT")) == 1, "Sign Out must be in the upper-right header");
}
Check(window.GetVisualDescendants().OfType<AccountView>().Any() && !HasText("WORKSPACE HOME"), "Launch must show Sign In");
Click("SIGN IN");
Check(HasText("Email and password are required."), "Empty sign-in must be validated");
Click("SIGN UP");
Click("SIGN UP");
Check(HasText("Name, phone and country are required."), "Empty signup must be validated");
var registration = window.GetVisualDescendants().OfType<TextBox>().ToArray();
Check(registration.Length == 5, "Signup must expose all original form fields");
registration[0].Text = "UI Test";
registration[1].Text = DemoAuthenticationService.Email;
registration[2].Text = DemoAuthenticationService.Password;
registration[3].Text = "0000000000";
registration[4].Text = "Test";
Click("SIGN UP");
CheckAuthenticatedWorkspace();
Check(HasText("WORKSPACE HOME"), "Successful signup must open Home");
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
CheckAuthenticatedWorkspace();

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

// Both sign-out and close must cancel pending simulation approval.
Click("Back to home");
Click("OPEN STRATEGIES");
Click("Use Player");
Click("Back to home");
Click("OPEN AUTOMATION");
Click("Prepare initial bet");
Check(controller.Snapshot.State == AutomationRunState.AwaitingApproval, "Close fixture missing approval");
Click("SIGN OUT");
Check(window.GetVisualDescendants().OfType<AccountView>().Any() && !HasText("WORKSPACE HOME"), "Sign-out must restore Sign In");
Check(controller.Snapshot.State == AutomationRunState.Paused && !controller.Snapshot.AutoBetEnabled,
    "Sign-out must pause simulation and disable auto-bet");
Check(controller.ApprovePendingAsync().GetAwaiter().GetResult().Outcome != AutomationActionOutcome.Accepted,
    "Sign-out must cancel approval");
var credentials = window.GetVisualDescendants().OfType<TextBox>().ToArray();
Check(string.IsNullOrEmpty(credentials[1].Text), "Password must be cleared after authentication");
credentials[0].Text = DemoAuthenticationService.Email;
credentials[1].Text = "wrong-password";
Click("SIGN IN");
Check(!HasText("WORKSPACE HOME") && HasText("did not match"), "Rejected sign-in must not expose workspace");
credentials[1].Text = DemoAuthenticationService.Password;
Click("SIGN IN");
CheckAuthenticatedWorkspace();
Click("OPEN AUTOMATION");
Click("Prepare initial bet");
window.Close();
Check(controller.Snapshot.State == AutomationRunState.Paused && !controller.Snapshot.AutoBetEnabled,
    "Close must cancel pending simulation and disable auto-bet");
Check(controller.ApprovePendingAsync().GetAwaiter().GetResult().Outcome != AutomationActionOutcome.Accepted,
    "Closed workspace must not retain an approvable bet");
window = new MainWindow(authenticationSessions: new SecretAuthenticationSessionStore(new VolatileSecretStore()));
window.Show();
window.UpdateLayout();
Check(!HasText("WORKSPACE HOME") && HasText("Wetalkbet server integration is not configured"),
    "Default launch must show honest server status and Sign In");
credentials = window.GetVisualDescendants().OfType<TextBox>().ToArray();
credentials[0].Text = "test@example.com";
credentials[1].Text = "test-password";
Click("SIGN IN");
Check(HasText("contract has not yet been recovered") && !HasText("WORKSPACE HOME"), "Offline service must never fake successful authentication");
Click("OPEN OFFLINE WORKSPACE");
Check(HasText("WORKSPACE HOME"), "Offline tools must remain accessible without pretending to authenticate");
Click("OPEN STRATEGIES");
Check(HasText("No strategy loaded") && !FindButton("Player result").IsEnabled, "Fresh launch must be empty and safe");
Click("BACK TO SIGN IN");
Check(window.GetVisualDescendants().OfType<AccountView>().Any(), "Offline mode must return to Sign In");
window.Close();
Console.WriteLine("PASS: account entry, signup/signin fixtures, rejection, signout, offline access and all existing workspace checks");

// Both account forms share the button theme.
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

var rememberedSecrets = new VolatileSecretStore();
var rememberedSessions = new SecretAuthenticationSessionStore(rememberedSecrets);
void OpenAccountWindow(IAuthenticationService service, IAuthenticationSessionStore sessions)
{
    window = new MainWindow(authenticationService: service, authenticationSessions: sessions);
    window.Show();
    Dispatcher.UIThread.RunJobs();
    window.UpdateLayout();
}
void SignInFixture(bool remember, string password = DemoAuthenticationService.Password)
{
    var rememberBox = window.GetVisualDescendants().OfType<CheckBox>().Single(b => Equals(b.Content, "REMEMBER ME"));
    Check(rememberBox.IsEnabled, "Remember Me must be available with a configured session store");
    rememberBox.IsChecked = remember;
    var fields = window.GetVisualDescendants().OfType<TextBox>().ToArray();
    fields[0].Text = DemoAuthenticationService.Email;
    fields[1].Text = password;
    Click("SIGN IN");
    Check(string.IsNullOrEmpty(fields[1].Text), "Remember Me must not retain the password in the UI");
}
OpenAccountWindow(new DemoAuthenticationService(), rememberedSessions);
SignInFixture(true);
CheckAuthenticatedWorkspace();
Check(rememberedSessions.ReadAsync().GetAwaiter().GetResult() is not null, "Checked Remember Me must save a successful session");
Check(!rememberedSecrets.ReadAsync("authentication.session.v1").GetAwaiter().GetResult()!
    .Contains(DemoAuthenticationService.Password, StringComparison.Ordinal), "Stored session must not contain password");
window.Close();
// A new store wrapper and window simulate a fresh application instance.
OpenAccountWindow(new DemoAuthenticationService(), new SecretAuthenticationSessionStore(rememberedSecrets));
CheckAuthenticatedWorkspace();
Check(HasText("WORKSPACE HOME"), "Remembered valid session must restore Home");
Click("SIGN OUT");
Check(rememberedSessions.ReadAsync().GetAwaiter().GetResult() is null, "Sign Out must delete remembered session");
SignInFixture(false);
CheckAuthenticatedWorkspace();
Check(rememberedSessions.ReadAsync().GetAwaiter().GetResult() is null, "Unchecked Remember Me must not persist session");
window.Close();
OpenAccountWindow(new DemoAuthenticationService(), rememberedSessions);
Check(!HasText("WORKSPACE HOME"), "Unchecked session must not survive relaunch");
SignInFixture(true, "incorrect");
Check(!HasText("WORKSPACE HOME") && rememberedSessions.ReadAsync().GetAwaiter().GetResult() is null,
    "Rejected login must never be remembered");
window.Close();
rememberedSessions.SaveAsync(new AuthenticatedSession("expired-fixture", "test-token", DateTimeOffset.UtcNow.AddMinutes(-1)))
    .GetAwaiter().GetResult();
OpenAccountWindow(new DemoAuthenticationService(), rememberedSessions);
Check(!HasText("WORKSPACE HOME") && HasText("expired") && rememberedSessions.ReadAsync().GetAwaiter().GetResult() is null,
    "Expired remembered sessions must be rejected and cleared");
window.Close();
rememberedSessions.SaveAsync(new AuthenticatedSession("old-fixture", "test-token", DateTimeOffset.UtcNow.AddHours(1)))
    .GetAwaiter().GetResult();
OpenAccountWindow(new OfflineAuthenticationService(), rememberedSessions);
Check(!HasText("WORKSPACE HOME"), "Unconfigured server service must never restore an old token as a valid login");
Check(window.GetVisualDescendants().OfType<CheckBox>().Single(b => Equals(b.Content, "REMEMBER ME")).IsEnabled,
    "Remember Me must be selectable even while server integration is unavailable");
window.Close();
Console.WriteLine("PASS: Remember Me opt-in, restoration, password exclusion, rejection, expiry, sign-out and offline safety");
DashboardChecks.Run(CheckButtonAlignment, screenshotDirectory);
RoadmapViewChecks.Run(screenshotDirectory);
WorkspacePersistenceChecks.Run();
