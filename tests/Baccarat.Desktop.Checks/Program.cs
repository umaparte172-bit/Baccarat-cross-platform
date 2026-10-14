using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Baccarat.Desktop;
using Baccarat.Desktop.Views;
using Baccarat.Core.Authentication;
using Baccarat.Core.Strategies;
using Baccarat.Infrastructure.Authentication;
using Baccarat.Infrastructure.Security;

AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var strategyDefinition = new StrategyDefinition(
    "UI Fixture",
    null,
    new StrategyRule("PLAYER", AmountRuleKind.Set, [1m]),
    new StrategyRule("WINNER", AmountRuleKind.ResetAmount, [2m]),
    new StrategyRule("WINNER", AmountRuleKind.MultiplyAmount, [2m]),
    null,
    null,
    null,
    null,
    0);
var window = new MainWindow(initialStrategy: strategyDefinition);
window.Show();
window.UpdateLayout();
Button FindButton(string name) => window.GetVisualDescendants().OfType<Button>()
    .Single(b => Equals(b.Content, name));
void Click(string name) => FindButton(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
bool HasText(string value) => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains(value) == true);
if (window.GetVisualDescendants().OfType<TabControl>().Any())
    throw new Exception("The tab navigation must not be displayed");
if (HasText("Session tracker") || window.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "OPEN SESSION")))
    throw new Exception("The workspace must be hidden before authentication");
if (!FindButton("SIGN IN").IsEnabled || !FindButton("SIGN UP").IsEnabled ||
    !window.GetVisualDescendants().OfType<CheckBox>().Any(box => Equals(box.Content, "REMEMBER ME")))
    throw new Exception("The SignIn screen was not shown");
if (!window.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "REMEMBER ME")).IsEnabled)
    throw new Exception("Remember Me must be available in local demo mode");
Click("SIGN UP");
window.UpdateLayout();
if (!HasText("NAME :") || !HasText("PHONE NO :") || !HasText("COUNTRY :"))
    throw new Exception("The SignUp screen was not shown");
Click("SIGN UP");
window.UpdateLayout();
if (!HasText("Name, phone and country are required."))
    throw new Exception("Registration validation was not shown in the account UI");
Click("SIGN IN");
window.UpdateLayout();
if (!window.GetVisualDescendants().OfType<CheckBox>().Any(box => Equals(box.Content, "REMEMBER ME")))
    throw new Exception("SignIn navigation did not restore its screen");
Click("SIGN UP");
window.UpdateLayout();
var account = window.GetVisualDescendants().OfType<AccountView>().Single();
var fields = account.GetVisualDescendants().OfType<TextBox>().ToArray();
if (fields.Length != 5) throw new Exception("The registration form fields were not shown");
fields[0].Text = "Demo User";
fields[1].Text = DemoAuthenticationService.Email;
fields[2].Text = DemoAuthenticationService.Password;
fields[3].Text = "0000000000";
fields[4].Text = "Test";
Click("SIGN UP");
window.UpdateLayout();
if (!HasText("Baccarat workspace") || !HasText("WORKSPACE HOME") ||
    !window.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "OPEN SESSION")))
    throw new Exception("Local demo registration did not open the workspace home");
if (window.GetVisualDescendants().OfType<AccountView>().Any() || window.GetVisualDescendants().OfType<TabControl>().Any())
    throw new Exception("The sign-in form and tab bar must be hidden after authentication");
if (Grid.GetColumn(FindButton("SIGN OUT")) != 1 ||
    window.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "Back to home")))
    throw new Exception("Sign Out must be in the upper-right header, with no Back button on Home");

Click("OPEN SESSION");
window.UpdateLayout();
var back = FindButton("Back to home");
if (DockPanel.GetDock(back) != Dock.Top || back.BorderThickness.Left != 1 || back.Margin.Left != 24 ||
    !window.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "SIGN OUT")))
    throw new Exception("Back to home must match workspace actions and Sign Out must remain available");
if (window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Session tracker").HorizontalAlignment != HorizontalAlignment.Center)
    throw new Exception("Workspace headings must be centered");
if (!HasText("TABLE WORKSPACE") || !HasText("RECORD TABLE RESULT") || !HasText("ROUND HISTORY"))
    throw new Exception("The session workspace layout was not shown");
if (FindButton("Undo last").IsEnabled) throw new Exception("Empty undo must be disabled");
Click("+ Player");
Click("+ Banker");
window.UpdateLayout();
if (!HasText("2 rounds")) throw new Exception("Recording did not update UI");
Click("Undo last");
window.UpdateLayout();
if (!HasText("1 rounds")) throw new Exception("Undo did not update UI");
if (!FindButton("Export CSV").IsEnabled) throw new Exception("Export should be enabled");

Click("Back to home");
Click("OPEN STRATEGIES");
window.UpdateLayout();
if (!HasText("STRATEGY WORKSPACE") || !HasText("CURRENT RECOMMENDATION") || !HasText("STRATEGY HISTORY"))
    throw new Exception("The strategy workspace layout was not shown");
if (!HasText("Bet 1 on Player.")) throw new Exception("Initial strategy recommendation was not shown");
Click("Banker result");
window.UpdateLayout();
if (!HasText("Bet 2 on Banker.") || !HasText("Profit -1.00"))
    throw new Exception("Strategy loss progression was not shown");
if (!FindButton("Undo strategy round").IsEnabled || !FindButton("Export strategy CSV").IsEnabled)
    throw new Exception("Strategy session undo and export must be enabled after settlement");
Click("Undo strategy round");
window.UpdateLayout();
if (!HasText("Profit 0.00") || !HasText("Bet 1 on Player."))
    throw new Exception("Strategy undo did not restore its initial state");

Click("Back to home");
Click("OPEN AUTOMATION");
window.UpdateLayout();
if (!HasText("Controlled automation") || !HasText("SIMULATION STATUS") ||
    !HasText("BET CONTROLS") || !HasText("Local simulation is the only enabled adapter"))
    throw new Exception("Controlled automation workspace was not shown");
Click("Connect simulation");
window.UpdateLayout();
if (!HasText("Simulation table connected.")) throw new Exception("Automation simulation did not connect");
Click("Prepare initial bet");
window.UpdateLayout();
if (!HasText("Approve 1 on Player.")) throw new Exception("Initial bet did not enter approval state");
Click("Approve pending bet");
window.UpdateLayout();
if (!HasText("Simulation accepted 1 on Player.") || !HasText("Accepted bets 1"))
    throw new Exception("Approved initial Player bet was not simulated");
Click("Enable auto-bet");

Click("Back to home");
Click("OPEN STRATEGIES");
window.UpdateLayout();
Click("Banker result");
window.UpdateLayout();
Click("Back to home");
Click("OPEN AUTOMATION");
window.UpdateLayout();
if (!HasText("Approve 2 on Banker.")) throw new Exception("Auto-bet did not prepare the next Banker action");
Click("Approve pending bet");
window.UpdateLayout();
if (!HasText("Simulation accepted 2 on Banker.") || !HasText("Accepted bets 2"))
    throw new Exception("Approved automatic Banker bet was not simulated");

Click("Back to home");
Click("OPEN VIEWER");
window.UpdateLayout();
if (!HasText("VIEWER WORKSPACE") || !HasText("COMMUNICATION") || !HasText("DOCUMENT HISTORY"))
    throw new Exception("The viewer workspace layout was not shown");
if (!HasText("Protocol 1.0") || !HasText("Rounds 1 · Player 1 · Banker 0 · Tie 0"))
    throw new Exception("Viewer did not receive the versioned manual-session document");
if (!HasText("UI Fixture · Profit -1.00"))
    throw new Exception("Viewer did not receive the latest versioned strategy document");
if (!HasText("Ready · Auto enabled · Simulation · Accepted 2 · Stake 3.00"))
    throw new Exception("Viewer did not receive the latest versioned automation document");

Click("Back to home");
Click("SIGN OUT");
window.UpdateLayout();
if (HasText("WORKSPACE HOME") || !HasText("EMAIL :") ||
    window.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "OPEN SESSION")))
    throw new Exception("Sign-out must hide every workspace and restore sign-in");
fields = account.GetVisualDescendants().OfType<TextBox>().ToArray();
if (fields.Length != 2) throw new Exception("The sign-in fields were not shown");
fields[0].Text = DemoAuthenticationService.Email;
fields[1].Text = DemoAuthenticationService.Password;
Click("SIGN IN");
window.UpdateLayout();
if (!HasText("WORKSPACE HOME")) throw new Exception("Local demo sign-in did not reopen the workspace");
var demo = new DemoAuthenticationService();
var rejected = await demo.SignInAsync(new SignInRequest(DemoAuthenticationService.Email, "incorrect"));
if (rejected.Outcome != AuthenticationOutcome.Rejected || rejected.Session is not null)
    throw new Exception("Incorrect demo credentials must not create a session");
window.Close();
var rememberedStore = new SecretAuthenticationSessionStore(new VolatileSecretStore());
var rememberedWindow = new MainWindow(authenticationSessions: rememberedStore);
rememberedWindow.Show();
rememberedWindow.UpdateLayout();
var remember = rememberedWindow.GetVisualDescendants().OfType<CheckBox>()
    .Single(box => Equals(box.Content, "REMEMBER ME"));
remember.IsChecked = true;
var rememberedFields = rememberedWindow.GetVisualDescendants().OfType<TextBox>().ToArray();
rememberedFields[0].Text = DemoAuthenticationService.Email;
rememberedFields[1].Text = DemoAuthenticationService.Password;
rememberedWindow.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "SIGN IN"))
    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
rememberedWindow.UpdateLayout();
if (!rememberedWindow.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "WORKSPACE HOME") ||
    await rememberedStore.ReadAsync() is null)
    throw new Exception("Remember Me did not save the local demo session");
rememberedWindow.Close();

var restoredWindow = new MainWindow(authenticationSessions: rememberedStore);
restoredWindow.Show();
Dispatcher.UIThread.RunJobs();
restoredWindow.UpdateLayout();
if (!restoredWindow.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "WORKSPACE HOME"))
    throw new Exception("Remember Me did not restore the local demo session");
restoredWindow.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "SIGN OUT"))
    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
restoredWindow.UpdateLayout();
if (await rememberedStore.ReadAsync() is not null ||
    restoredWindow.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "WORKSPACE HOME"))
    throw new Exception("Sign-out did not clear the remembered session");
restoredWindow.Close();
var transientStore = new SecretAuthenticationSessionStore(new VolatileSecretStore());
var transientWindow = new MainWindow(authenticationSessions: transientStore);
transientWindow.Show();
transientWindow.UpdateLayout();
var transientFields = transientWindow.GetVisualDescendants().OfType<TextBox>().ToArray();
transientFields[0].Text = DemoAuthenticationService.Email;
transientFields[1].Text = DemoAuthenticationService.Password;
transientWindow.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "SIGN IN"))
    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
transientWindow.UpdateLayout();
if (await transientStore.ReadAsync() is not null)
    throw new Exception("An unchecked Remember Me must not save a session");
transientWindow.Close();
Console.WriteLine("PASS: headless desktop startup, session tools and account validation UI");
