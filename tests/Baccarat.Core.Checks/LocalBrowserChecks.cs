using System.Net;
using Baccarat.Core.Automation;
using Baccarat.Core.Strategies;
using Baccarat.Infrastructure.Automation;

internal static class LocalBrowserChecks
{
    internal static async Task RunAsync()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        using var host = new LocalSimulationServer();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = host.Address, Timeout = TimeSpan.FromSeconds(5) };
        Check((await client.GetStringAsync("/")).Contains("NO MONEY", StringComparison.Ordinal), "Local page must label simulation");
        Check((await client.GetAsync("state")).StatusCode == HttpStatusCode.NotFound, "No state API");
        Check(!(await client.GetStringAsync("app.js")).Contains("fetch("), "DOM page must not call application APIs");
        using (var foreign = new HttpRequestMessage(HttpMethod.Post, "deal"))
        {
            foreign.Headers.Add("Origin", "https://example.invalid");
            foreign.Headers.Add("X-Simulation-Round", "round-1");
            Check((await client.SendAsync(foreign)).StatusCode == HttpStatusCode.Forbidden, "Cross-origin command rejected");
        }
        client.DefaultRequestHeaders.Add("X-Simulation-Round", "round-1");
        Check((await client.PostAsync("deal", new StringContent("unexpected payload"))).StatusCode == HttpStatusCode.BadRequest,
            "Request bodies must be rejected");
        Check((await client.PostAsync("deal", null)).StatusCode == HttpStatusCode.NotFound, "No deal API");
        using (var wrongHost = new HttpRequestMessage(HttpMethod.Get, "state"))
        {
            wrongHost.Headers.Host = "example.invalid";
            Check(!(await client.SendAsync(wrongHost)).IsSuccessStatusCode, "Foreign host rejected");
        }
        var adapter = new SimulatedAutomationTableAdapter { LimitOneBetPerRound = true };
        await adapter.ConnectAsync();
        await adapter.SelectChipAsync(1);
        var bet = new AutomationBetRequest(Guid.NewGuid(), BetSide.Player, 1, [1], true);
        Check((await adapter.PlaceBetAsync(bet)).Status == BetPlacementStatus.Accepted, "Simulated placement acknowledgement");
        Check((await adapter.PlaceBetAsync(bet)).Status == BetPlacementStatus.Accepted && adapter.AcceptedRequests.Count == 1,
            "Operation idempotency");
        await adapter.SelectChipAsync(1);
        Check((await adapter.PlaceBetAsync(bet with { OperationId = Guid.NewGuid() })).Status == BetPlacementStatus.Rejected,
            "Only one simulated wager per round");
        await adapter.DisconnectAsync();
        await adapter.ConnectAsync();
        Check(adapter.CurrentRoundBet?.OperationId == bet.OperationId, "Reconnect preserves accepted wager without resubmission");
        adapter.BeginRound();
        Check(adapter.CurrentRoundBet is null, "Next round clears settled wager");
        Console.WriteLine("PASS: static simulator page, no action APIs, origin/host isolation and placement/reconnect idempotency");
    }
}
