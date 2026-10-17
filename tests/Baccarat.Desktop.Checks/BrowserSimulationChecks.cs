using System.Net;
using System.Text.Json;
using Avalonia.Threading;
using Baccarat.Core.Automation;
using Baccarat.Core.Diagnostics;
using Baccarat.Desktop;
using Baccarat.Desktop.ViewModels;

internal static class BrowserSimulationChecks
{
    internal static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        T Pump<T>(Task<T> task)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            if (!task.IsCompleted) throw new TimeoutException("Browser bridge check timed out");
            return task.GetAwaiter().GetResult();
        }
        var workspace = new DashboardWorkspace();
        workspace.CreateSession();
        var session = workspace.Selected!;
        session.SelectStrategy("Martingale");
        Check(session.ApplySettings(out _), "Apply simulation fixture");
        SessionTools? tools = null;
        using var context = tools = new SessionTools(session, NullDiagnosticSink.Instance, () => tools!.Refresh());
        var address = context.StartBrowser();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(address.GetLeftPart(UriPartial.Authority)), Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.Add("X-Simulation-Token", address.Fragment[1..]);
        JsonDocument State() => JsonDocument.Parse(Pump(client.GetStringAsync("/state")));
        Check(Pump(context.Controller.ConnectAsync().AsTask()).Outcome == AutomationActionOutcome.Accepted, "Connect local table");
        using var before = State();
        Check(!before.RootElement.GetProperty("canDeal").GetBoolean(), "No deal before placement approval");
        Check(Pump(context.Controller.InitialBetAsync(session.Engine!.Recommendation).AsTask()).Outcome == AutomationActionOutcome.ApprovalRequired,
            "Initial bet requires approval");
        Check(Pump(context.Controller.ApprovePendingAsync().AsTask()).Outcome == AutomationActionOutcome.Accepted, "Simulated placement accepted");
        context.Controller.SetAutoBet(true);
        using var accepted = State();
        Check(accepted.RootElement.GetProperty("canDeal").GetBoolean() && accepted.RootElement.GetProperty("chips").GetString()!.Contains('1'),
            "Desktop placement and chips reflected in browser state");
        var key = accepted.RootElement.GetProperty("roundKey").GetString()!;
        client.DefaultRequestHeaders.Add("X-Simulation-Round", key);
        Check(Pump(client.PostAsync("/deal", null)).StatusCode == HttpStatusCode.OK && session.Hands.Count == 1,
            "Browser deals and settles shared session");
        Check(Pump(client.PostAsync("/deal", null)).StatusCode == HttpStatusCode.Conflict && session.Hands.Count == 1,
            "Stale browser click must not deal twice");
        Check(context.Controller.Snapshot.State == AutomationRunState.AwaitingApproval, "Auto-bet prepares next round, never bypasses approval");
        using var after = State();
        Check(after.RootElement.GetProperty("cards").GetString()!.Contains("Player:", StringComparison.Ordinal), "Browser receives cards");
        Check(!after.RootElement.GetProperty("canDeal").GetBoolean(), "Next wager waits for approval");
        context.Pause("Switched session");
        using var inactive = State();
        Check(!inactive.RootElement.GetProperty("canDeal").GetBoolean(), "Inactive session cannot be dealt through old tab");
        session.Undo();
        context.Refresh();
        using var undone = State();
        Check(undone.RootElement.GetProperty("roundKey").GetString() != key, "Undo invalidates earlier browser commands");
        Console.WriteLine("PASS: desktop-to-browser initial placement, chips, approval, settlement, auto-bet, stale commands and session isolation");

        if (Environment.GetEnvironmentVariable("BACCARAT_BROWSER_PREVIEW") == "1")
        {
            Pump(context.Controller.ConnectAsync().AsTask());
            Pump(context.Controller.InitialBetAsync(session.Engine!.Recommendation).AsTask());
            Pump(context.Controller.ApprovePendingAsync().AsTask());
            context.Controller.SetAutoBet(true);
            Console.WriteLine("BROWSER_PREVIEW=" + address);
            var until = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
        }
    }
}
