using Avalonia.Threading;
using Baccarat.Core.Automation;
using Baccarat.Core.Diagnostics;
using Baccarat.Desktop;
using Baccarat.Desktop.ViewModels;

internal static class BrowserSimulationChecks
{
    internal static void Run()
    {
        if (Environment.GetEnvironmentVariable("BACCARAT_DOM_CHECKS") != "1")
        { Console.WriteLine("SKIP: interactive DOM checks require BACCARAT_DOM_CHECKS=1 and installed Chrome/Edge."); return; }
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        void Pump(Task task)
        {
            var until = DateTime.UtcNow.AddSeconds(30);
            while (!task.IsCompleted && DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            if (!task.IsCompleted) throw new TimeoutException("DOM check timed out");
            task.GetAwaiter().GetResult();
        }
        var workspace = new DashboardWorkspace();
        workspace.CreateSession();
        var session = workspace.Selected!;
        session.SelectStrategy("Martingale");
        Check(session.ApplySettings(out _), "Apply fixture");
        SessionTools? tools = null;
        using var context = tools = new SessionTools(session, NullDiagnosticSink.Instance, () => tools!.Refresh());
        Pump(context.StartBrowserAsync());
        void Click(string action) => Pump(context.TryDomActionAsync(action));
        Click("connect"); Click("initial");
        Check(context.Controller.Snapshot.State == AutomationRunState.AwaitingApproval, "DOM initial-bet approval");
        Click("approve"); Click("auto");
        Check(context.Controller.Snapshot.AcceptedBets == 1, "DOM placement accepted once");
        Click("deal");
        Check(session.Hands.Count == 1 && context.Controller.Snapshot.State == AutomationRunState.AwaitingApproval, "DOM deal and next auto recommendation");
        Click("pause"); Click("undo");
        Check(session.Hands.Count == 0, "DOM Undo");
        Click("player-pair"); Click("player");
        Check(session.Hands.Count == 1 && session.Hands[0].PlayerPair, "DOM pair and Player result");
        Click("banker"); Click("tie");
        Check(session.Hands.Count == 3, "DOM Banker/Tie results");
        Click("shoe"); Check(session.CurrentShoe == 2, "DOM New Shoe");
        Click("apply"); Check(session.Engine is not null, "DOM settings application");
        Click("clear"); Check(session.Hands.Count == 3, "DOM clear requires confirmation");
        Click("clear"); Check(session.Hands.Count == 0, "DOM confirmed clear");
        Click("player"); Click("clear-confirmed");
        Check(session.Hands.Count == 0, "Native confirmation routes a single confirmed DOM clear");
        Click("recover"); Click("enable-auto"); Click("enable-auto");
        Check(context.Controller.Snapshot.AutoBetEnabled, "Enable is idempotent");
        Click("disable-auto"); Click("disable-auto");
        Check(!context.Controller.Snapshot.AutoBetEnabled, "Disable is idempotent");
        context.Pause("Session inactive");
        try { Click("player"); } catch (InvalidOperationException) { }
        Check(session.Hands.Count == 0, "Inactive DOM cannot alter session");
        Pump(context.CloseBrowserAsync());
        Console.WriteLine("PASS: actual browser DOM connect, initial/approve/auto/deal, results/pairs, Undo, shoe, settings, confirmed clear and isolation");
    }
}
