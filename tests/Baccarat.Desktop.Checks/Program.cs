using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Baccarat.Desktop;
using Baccarat.Desktop.Views;
using Baccarat.Core.Authentication;
using Baccarat.Core.Strategies;
using Baccarat.Infrastructure.Authentication;

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
if (FindButton("Undo last").IsEnabled) throw new Exception("Empty undo must be disabled");
Click("+ Player");
Click("+ Banker");
window.UpdateLayout();
if (!HasText("2 rounds")) throw new Exception("Recording did not update UI");
Click("Undo last");
window.UpdateLayout();
if (!HasText("1 rounds")) throw new Exception("Undo did not update UI");
if (!FindButton("Export CSV").IsEnabled) throw new Exception("Export should be enabled");
var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
if (!HasText("TABLE WORKSPACE") || !HasText("RECORD TABLE RESULT") || !HasText("ROUND HISTORY"))
    throw new Exception("The session workspace layout was not shown");
tabs.SelectedIndex = 1;
window.UpdateLayout();
if (!HasText("STRATEGY WORKSPACE") || !HasText("CURRENT RECOMMENDATION") || !HasText("STRATEGY HISTORY"))
    throw new Exception("The strategy workspace layout was not shown");
if (!HasText("Bet 1 on Player.")) throw new Exception("Initial strategy recommendation was not shown");
Click("Banker result");
window.UpdateLayout();
if (!HasText("Bet 2 on Banker.")) throw new Exception("Strategy loss progression was not shown");
if (!HasText("Profit -1.00")) throw new Exception("Strategy settlement total was not updated");
if (!FindButton("Undo strategy round").IsEnabled || !FindButton("Export strategy CSV").IsEnabled)
    throw new Exception("Strategy session undo and export must be enabled after settlement");
Click("Undo strategy round");
window.UpdateLayout();
if (!HasText("Profit 0.00") || !HasText("Bet 1 on Player."))
    throw new Exception("Strategy undo did not restore its initial state");

tabs.SelectedIndex = 2;
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

tabs.SelectedIndex = 1;
window.UpdateLayout();
Click("Banker result");
window.UpdateLayout();
tabs.SelectedIndex = 2;
window.UpdateLayout();
if (!HasText("Approve 2 on Banker.")) throw new Exception("Auto-bet did not prepare the next Banker action");
Click("Approve pending bet");
window.UpdateLayout();
if (!HasText("Simulation accepted 2 on Banker.") || !HasText("Accepted bets 2"))
    throw new Exception("Approved automatic Banker bet was not simulated");

tabs.SelectedIndex = 3;
window.UpdateLayout();
if (!HasText("VIEWER WORKSPACE") || !HasText("COMMUNICATION") || !HasText("DOCUMENT HISTORY"))
    throw new Exception("The viewer workspace layout was not shown");
if (!HasText("Protocol 1.0") || !HasText("Rounds 1 · Player 1 · Banker 0 · Tie 0"))
    throw new Exception("Viewer did not receive the versioned manual-session document: " +
        string.Join(" | ", window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)));
if (!HasText("UI Fixture · Profit -1.00"))
    throw new Exception("Viewer did not receive the latest versioned strategy document");
if (!HasText("Ready · Auto enabled · Simulation · Accepted 2 · Stake 3.00"))
    throw new Exception("Viewer did not receive the latest versioned automation document");

tabs.SelectedIndex = 4;
window.UpdateLayout();
if (!FindButton("SIGN IN").IsEnabled || !FindButton("SIGN UP").IsEnabled ||
    !window.GetVisualDescendants().OfType<CheckBox>().Any(box => Equals(box.Content, "REMEMBER ME")))
    throw new Exception("The SignIn screen was not shown");
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
if (tabs.SelectedIndex != 0 || !HasText("Session tracker"))
    throw new Exception("Local demo registration did not open the workspace");
if (tabs.ItemCount != 5) throw new Exception("All five workspace tabs must remain available");
tabs.SelectedIndex = 4;
window.UpdateLayout();
if (!HasText("Local demo access enabled") || !FindButton("SIGN OUT").IsVisible)
    throw new Exception("Local demo registration was not shown as an authenticated test session: " +
        string.Join(" | ", account.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)));
Click("SIGN OUT");
window.UpdateLayout();
Click("SIGN IN");
window.UpdateLayout();
fields = account.GetVisualDescendants().OfType<TextBox>().ToArray();
if (fields.Length != 2) throw new Exception("The sign-in fields were not shown");
fields[0].Text = DemoAuthenticationService.Email;
fields[1].Text = DemoAuthenticationService.Password;
Click("SIGN IN");
window.UpdateLayout();
if (tabs.SelectedIndex != 0) throw new Exception("Local demo sign-in did not reopen the workspace");
var demo = new DemoAuthenticationService();
var rejected = await demo.SignInAsync(new SignInRequest(DemoAuthenticationService.Email, "incorrect"));
if (rejected.Outcome != AuthenticationOutcome.Rejected || rejected.Session is not null)
    throw new Exception("Incorrect demo credentials must not create a session");
window.Close();
Console.WriteLine("PASS: headless desktop startup, session tools and account validation UI");
