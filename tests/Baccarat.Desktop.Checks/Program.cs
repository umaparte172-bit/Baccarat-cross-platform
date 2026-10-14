using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Baccarat.Desktop;
using Baccarat.Core.Authentication;
using Baccarat.Core.Strategies;

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
tabs.SelectedIndex = 1;
window.UpdateLayout();
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
if (!HasText("Controlled automation") || !HasText("Local simulation is the only enabled adapter"))
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
if (!HasText("Protocol 1.0") || !HasText("Rounds 1 · Player 1 · Banker 0 · Tie 0"))
    throw new Exception("Viewer did not receive the versioned manual-session document: " +
        string.Join(" | ", window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)));
if (!HasText("UI Fixture · Profit -1.00"))
    throw new Exception("Viewer did not receive the latest versioned strategy document");
if (!HasText("Ready · Auto enabled · Simulation · Accepted 2 · Stake 3.00"))
    throw new Exception("Viewer did not receive the latest versioned automation document");

tabs.SelectedIndex = 4;
window.UpdateLayout();
if (!FindButton("SIGN IN").IsEnabled || !FindButton("SIGN UP").IsEnabled)
    throw new Exception("Offline account must expose sign-in and registration validation");
Click("SIGN UP");
window.UpdateLayout();
if (!HasText("Name, phone and country are required."))
    throw new Exception("Registration validation was not shown in the account UI");
window.Close();
Console.WriteLine("PASS: headless desktop startup, session tools and account validation UI");
