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

tabs.SelectedIndex = 2;
window.UpdateLayout();
if (FindButton("Sign out").IsEnabled) throw new Exception("Offline account must not expose sign-out");
if (!FindButton("Sign in").IsEnabled || !FindButton("Create account").IsEnabled)
    throw new Exception("Offline account must expose sign-in and registration validation");
Click("Create account");
window.UpdateLayout();
if (!HasText("Name, phone and country are required."))
    throw new Exception("Registration validation was not shown in the account UI");
window.Close();
Console.WriteLine("PASS: headless desktop startup, session tools and account validation UI");
