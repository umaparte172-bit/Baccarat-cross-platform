using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Baccarat.Core;
using Baccarat.Core.Authentication;
using Baccarat.Core.Automation;
using Baccarat.Core.Communication;
using Baccarat.Core.Configuration;
using Baccarat.Core.Strategies;
using Baccarat.Infrastructure.Authentication;
using Baccarat.Infrastructure.Automation;
using Baccarat.Infrastructure.Communication;
using Baccarat.Infrastructure.Diagnostics;
using Baccarat.Infrastructure.Security;
using Baccarat.Infrastructure.Strategies;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
var session = new Session();
Check(!session.Undo(), "Empty undo should be harmless");
session.Record(Outcome.Banker);
session.Record(Outcome.Player);
session.Record(Outcome.Tie);
Check(session.Rounds.Count == 3 && session.Count(Outcome.Banker) == 1, "Counts must match results");
Check(session.Undo(), "Undo should remove last result");
session.Record(Outcome.Banker);
Check(session.Rounds[^1].Number == 3 && session.Count(Outcome.Tie) == 0, "Undo must keep numbering consistent");
Check(session.ToCsv().Split('\n').Length == 4 && session.ToCsv().Contains("3,Banker,"), "CSV must include all rounds");
try { session.Record((Outcome)999); throw new Exception("Invalid result accepted"); }
catch (ArgumentOutOfRangeException) { }
session.Clear();
Check(session.Rounds.Count == 0, "Clear must empty results");
Console.WriteLine("PASS: record, counts, undo, numbering, export, invalid input and clear");

var martingale = new StrategyDefinition(
    "Fixture Martingale",
    null,
    new StrategyRule("PLAYER", AmountRuleKind.Set, [1m]),
    new StrategyRule("WINNER", AmountRuleKind.ResetAmount, [2m]),
    new StrategyRule("WINNER", AmountRuleKind.MultiplyAmount, [2m]),
    null,
    null,
    null,
    null,
    0);
var strategy = new StrategySessionEngine(martingale);
Check(strategy.Recommendation is { State: StrategyEngineState.Ready, Side: BetSide.Player, Stake: 1m },
    "A fixed initial selector must produce a ready recommendation");
var strategyLoss = strategy.Record(Outcome.Banker);
Check(strategyLoss is { Won: false, ProfitChange: -1m } &&
      strategy.Recommendation is { Side: BetSide.Banker, Stake: 2m },
    "A loss must apply the multiplier and resolve WINNER to the latest table winner");
var strategyWin = strategy.Record(Outcome.Banker);
Check(strategyWin is { Won: true, ProfitChange: 1.90m } && strategy.Profit == 0.90m &&
      strategy.Recommendation.Stake == 1m,
    "A Banker win must apply commission and reset the amount");
var strategyPush = strategy.Record(Outcome.Tie);
Check(strategyPush is { Won: null, ProfitChange: 0m } && strategy.Recommendation.Stake == 1m,
    "A tie must be a push and preserve the recommendation");
Check(strategy.Undo() && strategy.Rounds.Count == 2 && strategy.Profit == 0.90m,
    "Undo must restore the exact state before the latest strategy round");
Check(strategy.ToCsv().StartsWith("Round,Outcome,Selector", StringComparison.Ordinal) &&
      strategy.ToCsv().Contains("1,Banker,PLAYER,Player,1,-1,-1,True,False", StringComparison.Ordinal),
    "Strategy CSV must include settled round details using invariant numbers");
strategy.Reset();
Check(strategy.Rounds.Count == 0 && strategy.Profit == 0 && !strategy.Undo() &&
      strategy.Recommendation is { Side: BetSide.Player, Stake: 1m },
    "Reset must restore the complete initial strategy state");

var manualDefinition = martingale with
{
    Name = "Opaque fixture",
    Initial = new StrategyRule("OPAQUE SIGNAL", AmountRuleKind.Set, [5m]),
    AfterWin = new StrategyRule("OPAQUE SIGNAL", AmountRuleKind.Pattern, [2m])
};
var manualStrategy = new StrategySessionEngine(manualDefinition);
Check(manualStrategy.Recommendation.State == StrategyEngineState.AwaitingSide,
    "Unknown selectors must require an explicit side instead of being guessed");
manualStrategy.ChooseSide(BetSide.Player);
manualStrategy.Record(Outcome.Player);
Check(manualStrategy.Recommendation.State == StrategyEngineState.AwaitingSide,
    "Each opaque selector decision must be explicitly resolved");
manualStrategy.ChooseSide(BetSide.Banker);
Check(manualStrategy.Recommendation.State == StrategyEngineState.AwaitingStake,
    "Unrecovered amount algorithms must require an explicit stake");
manualStrategy.SetManualStake(7m);
Check(manualStrategy.Recommendation is { State: StrategyEngineState.Ready, Stake: 7m },
    "A manual stake must make an opaque amount rule executable");

var sequenceDefinition = martingale with
{
    Name = "Sequence fixture",
    Initial = new StrategyRule("PLAYER", AmountRuleKind.Set, [1m, 2m]),
    AfterLoss = new StrategyRule("PLAYER", AmountRuleKind.NextSequence, [2m]),
    StopProfit = 5m,
    StopLoss = 10m
};
var sequenceStrategy = new StrategySessionEngine(sequenceDefinition);
sequenceStrategy.Record(Outcome.Banker);
Check(sequenceStrategy.Recommendation.Stake == 2m, "Next Sequence must advance through the initial sequence");
sequenceStrategy.Record(Outcome.Banker);
Check(sequenceStrategy.Recommendation.State == StrategyEngineState.Stopped,
    "A completed sequence must stop instead of inventing a rollover rule");

var pausedDefinition = martingale with
{
    Name = "Pause fixture",
    PauseAfterConsecutiveLosses = 1,
    PauseRounds = 2
};
var pausedStrategy = new StrategySessionEngine(pausedDefinition);
pausedStrategy.Record(Outcome.Banker);
Check(pausedStrategy.Recommendation.State == StrategyEngineState.Paused,
    "The configured consecutive-loss trigger must pause betting");
Check(!pausedStrategy.Record(Outcome.Player).WasBet && !pausedStrategy.Record(Outcome.Banker).WasBet,
    "Paused rounds must be observed without placing a wager");
Check(pausedStrategy.Recommendation.State == StrategyEngineState.Ready,
    "Betting must resume after the configured number of observed rounds");

var stoppedDefinition = martingale with { Name = "Stop fixture", StopProfit = 1m };
var stoppedStrategy = new StrategySessionEngine(stoppedDefinition);
stoppedStrategy.Record(Outcome.Player);
Check(stoppedStrategy.Recommendation.State == StrategyEngineState.Stopped,
    "The strategy must stop when its profit limit is reached");

var customPayoutStrategy = new StrategySessionEngine(martingale, new BaccaratPayoutRules(2m, 1m));
Check(customPayoutStrategy.Record(Outcome.Player).ProfitChange == 2m,
    "Configured payout rules must control settlement without changing strategy logic");
var boundedDefinition = martingale with
{
    Name = "Bounded fixture",
    Initial = new StrategyRule("PLAYER", AmountRuleKind.Set, [StrategyDefinitionValidator.MaximumSupportedAmount]),
    AfterLoss = new StrategyRule("PLAYER", AmountRuleKind.MultiplyAmount, [2m])
};
var boundedStrategy = new StrategySessionEngine(boundedDefinition);
boundedStrategy.Record(Outcome.Banker);
Check(boundedStrategy.Recommendation.State == StrategyEngineState.Stopped,
    "Stake multiplication beyond the supported limit must stop safely");
Console.WriteLine("PASS: strategy recommendations, settlement, progression and guarded opaque rules");

var selectedChips = ChipSelection.SelectExact(16m, [1m, 5m, 10m], 10);
Check(selectedChips.SequenceEqual([10m, 5m, 1m]),
    "Chip selection must produce an exact minimal-click combination");
try
{
    _ = ChipSelection.SelectExact(3m, [2m], 10);
    throw new Exception("Impossible chip combination accepted");
}
catch (InvalidOperationException)
{
}

var automationPolicy = new AutomationSafetyPolicy(
    100m,
    100m,
    20,
    3,
    TimeSpan.Zero,
    TimeSpan.Zero,
    true);
var simulationAdapter = new SimulatedAutomationTableAdapter([1m, 5m, 10m, 25m]);
var automation = new AutomationController(simulationAdapter, automationPolicy);
automation.SetAutoBet(true);
Check(!automation.Snapshot.AutoBetEnabled,
    "Auto-bet must not enable while the adapter is disconnected");
Check((await automation.ConnectAsync()).Outcome == AutomationActionOutcome.Accepted &&
      automation.Snapshot is { State: AutomationRunState.Ready, IsSimulation: true },
    "Controlled automation must connect through its table adapter");
automation.SetAutoBet(true);
var playerRecommendation = new BetRecommendation(
    "PLAYER", 16m, BetSide.Player, StrategyEngineState.Ready, "Bet 16 on Player.");
Check((await automation.AutoBetAsync(playerRecommendation)).Outcome == AutomationActionOutcome.ApprovalRequired &&
      simulationAdapter.AcceptedRequests.Count == 0,
    "Auto-bet must wait for explicit approval under the default safety policy");
var approvedAutomationBet = await automation.ApprovePendingAsync();
Check(approvedAutomationBet.Outcome == AutomationActionOutcome.Accepted &&
      simulationAdapter.AcceptedRequests.Single() is { Side: BetSide.Player, Stake: 16m, IsInitialBet: false } &&
      automation.Snapshot is { State: AutomationRunState.Ready, SessionStake: 16m, AcceptedBets: 1 },
    "Approved automation must select chips and execute the Player action exactly once");
Check((await automation.AutoBetAsync(playerRecommendation with { Stake = 10m })).Outcome ==
      AutomationActionOutcome.ApprovalRequired &&
      automation.CancelPending() &&
      (await automation.ApprovePendingAsync()).Outcome == AutomationActionOutcome.Skipped,
    "A strategy change must be able to invalidate stale approval without placing a bet");
automation.SetAutoBet(true);
Check((await automation.AutoBetAsync(playerRecommendation with { Stake = 90m })).Outcome ==
      AutomationActionOutcome.Rejected && automation.Snapshot.State == AutomationRunState.Stopped,
    "Cumulative session stake must stop automation before exceeding its safety limit");

var reconnectAdapter = new SimulatedAutomationTableAdapter { FailedConnectionAttemptsRemaining = 2 };
var reconnectAutomation = new AutomationController(reconnectAdapter, automationPolicy,
    (_, cancellationToken) => Task.Delay(TimeSpan.Zero, cancellationToken));
Check((await reconnectAutomation.ConnectAsync()).Outcome == AutomationActionOutcome.Accepted && reconnectAdapter.IsConnected,
    "Connection recovery must use bounded retries and succeed when the adapter recovers");

var uncertainAdapter = new SimulatedAutomationTableAdapter { DisconnectBeforePlacement = true };
var uncertainAutomation = new AutomationController(uncertainAdapter, automationPolicy);
await uncertainAutomation.ConnectAsync();
var bankerRecommendation = new BetRecommendation(
    "BANKER", 10m, BetSide.Banker, StrategyEngineState.Ready, "Bet 10 on Banker.");
Check((await uncertainAutomation.InitialBetAsync(bankerRecommendation)).Outcome ==
      AutomationActionOutcome.ApprovalRequired,
    "Initial bet must use the same approval gate");
Check((await uncertainAutomation.ApprovePendingAsync()).Outcome == AutomationActionOutcome.Indeterminate &&
      uncertainAdapter.AcceptedRequests.Count == 0 &&
      uncertainAutomation.Snapshot.State == AutomationRunState.Faulted,
    "A disconnect during placement must become indeterminate and must never retry the bet");
Check((await uncertainAutomation.RecoverAsync()).Outcome == AutomationActionOutcome.Accepted &&
      uncertainAutomation.Snapshot.State == AutomationRunState.Paused &&
      uncertainAdapter.AcceptedRequests.Count == 0,
    "Recovery must reconnect in a paused review state without replaying an uncertain bet");
Console.WriteLine("PASS: controlled automation, chip selection, approval, reconnect and no-replay recovery");

const string strategyJson = """
    {
      "strategy_name":"Fixture Sequence",
      "rule_condition":"5,3,5,1",
      "initial_condition":{"bet_on":"PLAYER","amount":{"type":"set","value":"1,2,3"}},
      "win_condition":{"bet_on":"WINNER","amount":{"type":"Reset Sequence","value":"2"}},
      "loss_condition":{"bet_on":"WINNER","amount":{"type":"Next Sequence","value":"2"}},
      "stop_profit":"10",
      "stop_loss":"5",
      "stop_consecutive_loss":{"action":"STOP BET","count":"4"},
      "pause_consecutive_loss":{"pause_bets":"2","count":"3"}
    }
    """;
var strategyLoader = new StrategyFileLoader();
await using var strategyStream = new MemoryStream(Encoding.UTF8.GetBytes(strategyJson));
var importedStrategy = await strategyLoader.LoadAsync(strategyStream);
Check(importedStrategy.Name == "Fixture Sequence" &&
      importedStrategy.Initial.Values.SequenceEqual([1m, 2m, 3m]) &&
      importedStrategy.AfterWin.AmountRule == AmountRuleKind.ResetSequence &&
      importedStrategy.AfterLoss.AmountRule == AmountRuleKind.NextSequence &&
      importedStrategy is
      {
          StopProfit: 10m,
          StopLoss: 5m,
          StopAfterConsecutiveLosses: 4,
          PauseAfterConsecutiveLosses: 3,
          PauseRounds: 2
      },
    "The recovered strategy JSON schema must map to a validated definition");

await using var unknownFieldStream = new MemoryStream(Encoding.UTF8.GetBytes(strategyJson.Replace(
    "\"strategy_name\":\"Fixture Sequence\"",
    "\"strategy_name\":\"Fixture Sequence\",\"unexpected\":true",
    StringComparison.Ordinal)));
try
{
    await strategyLoader.LoadAsync(unknownFieldStream);
    throw new Exception("Unknown strategy field accepted");
}
catch (StrategyFormatException)
{
}

await using var duplicateStrategyFieldStream = new MemoryStream(Encoding.UTF8.GetBytes(strategyJson.Replace(
    "\"strategy_name\":\"Fixture Sequence\"",
    "\"strategy_name\":\"Fixture Sequence\",\"strategy_name\":\"Ambiguous\"",
    StringComparison.Ordinal)));
try
{
    await strategyLoader.LoadAsync(duplicateStrategyFieldStream);
    throw new Exception("Duplicate strategy field accepted");
}
catch (StrategyFormatException)
{
}

await using var oversizedStrategyStream = new MemoryStream(new byte[StrategyFileLoader.MaximumFileBytes + 1]);
try
{
    await strategyLoader.LoadAsync(oversizedStrategyStream);
    throw new Exception("Oversized strategy file accepted");
}
catch (StrategyFormatException)
{
}

var deeplyNestedStrategy = "{\"strategy_name\":\"Nested\",\"nested\":" +
    new string('[', 20) + "0" + new string(']', 20) + "}";
await using var deeplyNestedStrategyStream = new MemoryStream(Encoding.UTF8.GetBytes(deeplyNestedStrategy));
try
{
    await strategyLoader.LoadAsync(deeplyNestedStrategyStream);
    throw new Exception("Excessively nested strategy file accepted");
}
catch (StrategyFormatException)
{
}

var suppliedStrategyDirectory = Path.Combine("Original Application", "strategies");
if (Directory.Exists(suppliedStrategyDirectory))
{
    var suppliedFiles = Directory.GetFiles(suppliedStrategyDirectory, "*.txt", SearchOption.TopDirectoryOnly);
    Check(suppliedFiles.Length == 8, "The supplied strategy inventory changed unexpectedly");
    foreach (var suppliedFile in suppliedFiles)
    {
        await using var suppliedStream = File.OpenRead(suppliedFile);
        await strategyLoader.LoadAsync(suppliedStream);
    }
}
Console.WriteLine("PASS: strict strategy import and supplied schema compatibility");

var viewerCodec = new VersionedViewerDocumentCodec();
var viewerDocument = new ViewerDocument(
    ViewerDocumentContract.EnvelopeVersion,
    ViewerDocumentContract.SessionSnapshotType,
    ViewerDocumentContract.SessionSnapshotVersion,
    Guid.NewGuid(),
    1,
    new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero),
    new SessionSnapshotPayload(3, 1, 1, 1));
var encodedViewerDocument = viewerCodec.Encode(viewerDocument);
var decodedViewerDocument = viewerCodec.Decode(encodedViewerDocument);
Check(decodedViewerDocument == viewerDocument,
    "A supported viewer document must round-trip without losing its versions or payload");

var encodedViewerText = Encoding.UTF8.GetString(encodedViewerDocument);
try
{
    viewerCodec.Decode(Encoding.UTF8.GetBytes(encodedViewerText.Replace(
        "\"protocol_version\":\"1.0\"",
        "\"protocol_version\":\"2.0\"",
        StringComparison.Ordinal)));
    throw new Exception("Unsupported viewer protocol version accepted");
}
catch (ViewerDocumentFormatException)
{
}

try
{
    viewerCodec.Decode(Encoding.UTF8.GetBytes(encodedViewerText.Replace(
        "\"sequence\":1",
        "\"sequence\":1,\"sequence\":2",
        StringComparison.Ordinal)));
    throw new Exception("Duplicate viewer document property accepted");
}
catch (ViewerDocumentFormatException)
{
}

try
{
    viewerCodec.Decode(Encoding.UTF8.GetBytes(encodedViewerText.Replace(
        "\"document_version\":\"1.0\"",
        "\"document_version\":\"1.1\"",
        StringComparison.Ordinal)));
    throw new Exception("Unsupported viewer document version accepted");
}
catch (ViewerDocumentFormatException)
{
}

try
{
    viewerCodec.Decode(new byte[VersionedViewerDocumentCodec.MaximumDocumentBytes + 1]);
    throw new Exception("Oversized viewer document accepted");
}
catch (ViewerDocumentFormatException)
{
}

try
{
    viewerCodec.Decode(Encoding.UTF8.GetBytes(new string('[', 20) + "0" + new string(']', 20)));
    throw new Exception("Excessively nested viewer document accepted");
}
catch (ViewerDocumentFormatException)
{
}

try
{
    viewerCodec.Decode(Encoding.UTF8.GetBytes(encodedViewerText.Replace(
        ViewerDocumentContract.SessionSnapshotType,
        "unsupported.snapshot",
        StringComparison.Ordinal)));
    throw new Exception("Unknown viewer document type accepted");
}
catch (ViewerDocumentFormatException)
{
}

var viewerStore = new ViewerDocumentStore(2);
Check(viewerStore.Apply(viewerDocument) == ViewerDocumentApplyResult.Applied &&
      viewerStore.Apply(viewerDocument) == ViewerDocumentApplyResult.DuplicateOrOutOfOrder,
    "Viewer state must reject duplicate or out-of-order sequences");
Check(viewerStore.Apply(viewerDocument with { DocumentId = Guid.NewGuid(), Sequence = 2 }) ==
      ViewerDocumentApplyResult.Applied &&
      viewerStore.Apply(viewerDocument with { DocumentId = Guid.NewGuid(), Sequence = 3 }) ==
      ViewerDocumentApplyResult.Applied &&
      viewerStore.Documents.Select(document => document.Sequence).SequenceEqual([2L, 3L]),
    "Viewer state must enforce its configured history capacity");
try
{
    _ = new ViewerDocumentStore(0);
    throw new Exception("Invalid viewer history capacity accepted");
}
catch (ArgumentOutOfRangeException)
{
}
var viewerTransport = new LoopbackViewerMessageTransport();
var viewerModule = new ViewerCommunicationModule(viewerTransport, viewerCodec, new ViewerDocumentStore(3));
var receivedViewerDocuments = new List<ViewerDocument>();
viewerModule.DocumentReceived += receivedViewerDocuments.Add;
viewerModule.DocumentReceived += _ => throw new InvalidOperationException("Subscriber fixture failure");
await viewerModule.PublishAsync(new CommunicationStatusPayload("offline", "Local viewer ready."));
await viewerModule.PublishAsync(new StrategySnapshotPayload(
    "Fixture", 1.25m, 3, 2, StrategyEngineState.Ready, "Bet 1 on Player."));
await viewerModule.PublishAsync(new AutomationSnapshotPayload(
    AutomationRunState.Ready, true, true, 3m, 2, "Simulation ready."));
Check(receivedViewerDocuments.Count == 3 &&
      receivedViewerDocuments[0].Sequence == 1 &&
      receivedViewerDocuments[1].Sequence == 2 &&
      receivedViewerDocuments[2].Sequence == 3 &&
      receivedViewerDocuments[1].Payload is StrategySnapshotPayload &&
      receivedViewerDocuments[2].Payload is AutomationSnapshotPayload,
    "The communication module must encode, transport, validate and deliver ordered documents");

var concurrentViewerTransport = new ConcurrentViewerMessageTransport();
var concurrentViewerModule = new ViewerCommunicationModule(concurrentViewerTransport);
var concurrentSequences = new List<long>();
concurrentViewerModule.DocumentReceived += document => concurrentSequences.Add(document.Sequence);
await Task.WhenAll(Enumerable.Range(0, 10).Select(index =>
    concurrentViewerModule.PublishAsync(new CommunicationStatusPayload("test", $"Message {index}")).AsTask()));
Check(concurrentViewerTransport.MaximumConcurrency == 1 &&
      concurrentSequences.SequenceEqual(Enumerable.Range(1, 10).Select(value => (long)value)),
    "Concurrent viewer publications must be serialized and delivered in sequence order");
Console.WriteLine("PASS: versioned viewer documents, compatibility rejection and ordered communication");

var response = new Queue<AuthenticationResult?>();
var authentication = new StubAuthenticationService(response);
var controller = new AuthenticationController(authentication);

Check((await controller.SignInAsync("", "")).Outcome == AuthenticationOutcome.ValidationFailed,
    "Empty credentials must fail before transport");
Check(authentication.SignInCalls == 0, "Invalid input must not call authentication transport");
Check((await controller.SignInAsync("not-an-email", "secret")).Outcome == AuthenticationOutcome.ValidationFailed,
    "Malformed email must fail before transport");
Check((await controller.SignInAsync(
        new string('a', AuthenticationController.MaximumEmailLength + 1), "secret")).Outcome ==
      AuthenticationOutcome.ValidationFailed,
    "Oversized credentials must fail before transport");
Check(authentication.SignInCalls == 0, "Oversized credentials must not call authentication transport");

response.Enqueue(new(AuthenticationOutcome.Rejected, "Invalid credentials."));
Check((await controller.SignInAsync("test@example.com", "secret")).Outcome == AuthenticationOutcome.Rejected,
    "Rejected authentication must remain rejected");
response.Enqueue(null);
Check((await controller.SignInAsync("test@example.com", "secret")).Outcome == AuthenticationOutcome.EmptyResponse,
    "Null service result must become an empty-response error");
response.Enqueue(new(AuthenticationOutcome.MalformedResponse, "Malformed response."));
Check((await controller.SignInAsync("test@example.com", "secret")).Outcome == AuthenticationOutcome.MalformedResponse,
    "Malformed response must be represented without throwing");
response.Enqueue(new(AuthenticationOutcome.Authenticated, "Connected.",
    new AuthenticatedSession("fixture-user", "fixture-token", null)));
Check((await controller.SignInAsync("test@example.com", "secret")).IsAuthenticated && controller.Session is not null,
    "Successful result must create a session");
await controller.SignOutAsync();
Check(controller.Session is null && authentication.SignOutCalls == 1, "Sign-out must clear the session");

response.Enqueue(new(AuthenticationOutcome.Authenticated, "Connected.",
    new AuthenticatedSession("fixture-user", "fixture-token", null)));
await controller.SignInAsync("test@example.com", "secret");
authentication.ThrowOnSignOut = true;
await controller.SignOutAsync();
Check(controller.Session is null && controller.LastResult.Outcome == AuthenticationOutcome.UnexpectedError,
    "Sign-out failure must retain no local session and must not throw");

authentication.ThrowOnSignIn = true;
Check((await controller.SignInAsync("test@example.com", "secret")).Outcome == AuthenticationOutcome.UnexpectedError,
    "Service exceptions must become controlled errors");
Console.WriteLine("PASS: authentication validation, rejection, empty/malformed responses, session and exception handling");

var sessionSecrets = new VolatileSecretStore();
var sessionStore = new SecretAuthenticationSessionStore(sessionSecrets);
var fixedTime = new FixedTimeProvider(new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero));
var lifecycleResponses = new Queue<AuthenticationResult?>();
var lifecycleService = new StubAuthenticationService(lifecycleResponses);
var lifecycle = new AuthenticationController(lifecycleService, sessionStore: sessionStore, timeProvider: fixedTime);

lifecycleResponses.Enqueue(new(AuthenticationOutcome.Authenticated, "Connected.",
    new AuthenticatedSession("fixture-user", "", fixedTime.GetUtcNow().AddHours(1))));
Check((await lifecycle.SignInAsync("test@example.com", "secret")).Outcome == AuthenticationOutcome.MalformedResponse,
    "Authenticated responses without a token must be rejected");

lifecycleResponses.Enqueue(new(AuthenticationOutcome.Authenticated, "Connected.",
    new AuthenticatedSession("fixture-user", "fixture-token", fixedTime.GetUtcNow().AddMinutes(-1))));
Check((await lifecycle.SignInAsync("test@example.com", "secret")).Outcome == AuthenticationOutcome.Expired,
    "Already-expired sessions must be rejected");

var savedSession = new AuthenticatedSession("fixture-user", "stored-token", fixedTime.GetUtcNow().AddHours(1));
await sessionStore.SaveAsync(savedSession);
var restoredController = new AuthenticationController(
    new StubAuthenticationService(new Queue<AuthenticationResult?>()),
    sessionStore: sessionStore,
    timeProvider: fixedTime);
Check((await restoredController.RestoreSessionAsync()).IsAuthenticated &&
      restoredController.Session?.AccessToken == "stored-token",
    "A valid protected session must be restored");
await restoredController.SignOutAsync();
Check(await sessionStore.ReadAsync() is null, "Sign-out must remove the stored session");

await sessionStore.SaveAsync(savedSession with { ExpiresAt = fixedTime.GetUtcNow().AddSeconds(-1) });
var expiredController = new AuthenticationController(
    new StubAuthenticationService(new Queue<AuthenticationResult?>()),
    sessionStore: sessionStore,
    timeProvider: fixedTime);
Check((await expiredController.RestoreSessionAsync()).Outcome == AuthenticationOutcome.Expired &&
      await sessionStore.ReadAsync() is null,
    "Expired stored sessions must be rejected and removed");
Console.WriteLine("PASS: authentication session validation, persistence, restoration, expiry and cleanup");

var registrationResponses = new Queue<AuthenticationResult?>();
var registrationService = new StubAuthenticationService(registrationResponses);
var registrationController = new AuthenticationController(registrationService);
Check((await registrationController.SignUpAsync("", "test@example.com", "secret", "123", "Testland")).Outcome ==
      AuthenticationOutcome.ValidationFailed,
    "Registration must require a name");
Check(registrationService.SignUpCalls == 0, "Invalid registration must not call the transport");
Check((await registrationController.SignUpAsync(
        new string('a', AuthenticationController.MaximumNameLength + 1),
        "test@example.com", "secret", "123", "Testland")).Outcome == AuthenticationOutcome.ValidationFailed,
    "Oversized registration details must fail before transport");
Check(registrationService.SignUpCalls == 0, "Oversized registration must not call the transport");
registrationResponses.Enqueue(new(
    AuthenticationOutcome.Authenticated,
    "Account created.",
    new AuthenticatedSession("new-user", "registration-token", null)));
Check((await registrationController.SignUpAsync(
    "Test User", "test@example.com", "secret", "123", "Testland")).IsAuthenticated,
    "A successful registration response must establish a session");
Console.WriteLine("PASS: registration validation and authenticated completion");

try
{
    using var _ = new HttpAuthenticationService(
        new Uri("http://example.invalid"), TimeSpan.FromSeconds(10), new FixtureAuthenticationProtocol());
    throw new Exception("Insecure authentication origin accepted");
}
catch (ArgumentException)
{
}

var transport = new ScriptedHttpMessageHandler();
transport.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
{
    Content = new StringContent("fixture-user|fixture-token|2026-10-13T01:00:00+00:00")
});
using var httpAuthentication = new HttpAuthenticationService(
    new Uri("https://auth.example.test/api/"),
    TimeSpan.FromSeconds(10),
    new FixtureAuthenticationProtocol(),
    handler: transport);
var httpResult = await httpAuthentication.SignInAsync(new SignInRequest("test@example.com", "secret"));
Check(httpResult.IsAuthenticated && transport.LastUri == new Uri("https://auth.example.test/api/fixture/sign-in"),
    "HTTP authentication must resolve protocol routes against the configured HTTPS origin");
Check(transport.LastBody?.Contains("secret", StringComparison.Ordinal) == true,
    "The fixture transport must receive the protocol-produced request body");

transport.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized));
Check((await httpAuthentication.SignInAsync(new SignInRequest("test@example.com", "wrong"))).Outcome ==
      AuthenticationOutcome.Rejected,
    "HTTP 401 must be normalized as rejected credentials");
transport.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
Check((await httpAuthentication.SignInAsync(new SignInRequest("test@example.com", "secret"))).Outcome ==
      AuthenticationOutcome.Unavailable,
    "HTTP 5xx must be normalized as service unavailable");

transport.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
{
    Content = new StringContent("registered-user|registered-token|2026-10-13T01:00:00+00:00")
});
Check((await httpAuthentication.SignUpAsync(new SignUpRequest(
    "Test User", "test@example.com", "secret", "123", "Testland"))).IsAuthenticated,
    "Registration must use the same confined HTTP transport and normalized session result");

transport.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK));
await httpAuthentication.SignOutAsync(new AuthenticatedSession("fixture-user", "signout-token", null));
Check(transport.LastAuthorization == "Bearer signout-token", "Sign-out must use the protocol-produced authorization header");

var foreignOriginTransport = new ScriptedHttpMessageHandler();
using var foreignOriginService = new HttpAuthenticationService(
    new Uri("https://auth.example.test/"),
    TimeSpan.FromSeconds(10),
    new ForeignOriginProtocol(),
    handler: foreignOriginTransport);
try
{
    await foreignOriginService.SignInAsync(new SignInRequest("test@example.com", "secret"));
    throw new Exception("Cross-origin authentication request accepted");
}
catch (InvalidOperationException)
{
    Check(foreignOriginTransport.CallCount == 0, "Cross-origin requests must be rejected before transmission");
}
Console.WriteLine("PASS: HTTPS authentication transport, response normalization and origin confinement");

Check(NetworkClientOptions.Disconnected.Validate().Count == 0 && !NetworkClientOptions.Disconnected.IsConfigured,
    "Disconnected configuration must be valid");
var insecureOptions = new NetworkClientOptions(
    new Uri("http://example.invalid"), new Uri("ws://example.invalid"), TimeSpan.FromMilliseconds(100));
Check(insecureOptions.Validate().Count == 3, "Insecure endpoints and invalid timeout must be rejected");

var secrets = new VolatileSecretStore();
await secrets.SaveAsync("session", "fixture-secret");
Check(await secrets.ReadAsync("session") == "fixture-secret", "Volatile secret store must round-trip in memory");
await secrets.DeleteAsync("session");
Check(await secrets.ReadAsync("session") is null, "Deleted secrets must not remain available");

var diagnosticLines = new List<string>();
var redactingSink = new RedactingDiagnosticSink(diagnosticLines.Add);
redactingSink.Write(Baccarat.Core.Diagnostics.DiagnosticLevel.Error, "fixture",
    "email=user@example.com password=hunter2 Authorization: Bearer abc.def token=xyz");
Check(diagnosticLines.Count == 1 &&
      !diagnosticLines[0].Contains("user@example.com") &&
      !diagnosticLines[0].Contains("hunter2") &&
      !diagnosticLines[0].Contains("abc.def") &&
      !diagnosticLines[0].Contains("xyz"),
    "Diagnostics must redact email, password, bearer token and token assignments");
Console.WriteLine("PASS: configuration validation, volatile secrets and diagnostic redaction");

sealed class StubAuthenticationService(Queue<AuthenticationResult?> responses) : IAuthenticationService
{
    public int SignInCalls { get; private set; }
    public int SignOutCalls { get; private set; }
    public int SignUpCalls { get; private set; }
    public bool ThrowOnSignIn { get; set; }
    public bool ThrowOnSignOut { get; set; }

    public Task<AuthenticationResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default)
    {
        SignInCalls++;
        if (ThrowOnSignIn) throw new InvalidOperationException("fixture failure");
        return Task.FromResult(responses.Dequeue()!);
    }

    public Task SignOutAsync(AuthenticatedSession session, CancellationToken cancellationToken = default)
    {
        SignOutCalls++;
        if (ThrowOnSignOut) throw new InvalidOperationException("fixture failure");
        return Task.CompletedTask;
    }

    public Task<AuthenticationResult> SignUpAsync(
        SignUpRequest request,
        CancellationToken cancellationToken = default)
    {
        SignUpCalls++;
        return Task.FromResult(responses.Dequeue()!);
    }
}

sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => value;
}

sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    public Queue<HttpResponseMessage> Responses { get; } = new();
    public int CallCount { get; private set; }
    public Uri? LastUri { get; private set; }
    public string? LastBody { get; private set; }
    public string? LastAuthorization { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastUri = request.RequestUri;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        LastAuthorization = request.Headers.Authorization?.ToString();
        return Responses.Dequeue();
    }
}

class FixtureAuthenticationProtocol : IAuthenticationHttpProtocol
{
    public virtual HttpRequestMessage CreateSignInRequest(SignInRequest request) => new(
        HttpMethod.Post,
        "fixture/sign-in")
    {
        Content = new StringContent($"{request.Email}|{request.Password}", Encoding.UTF8, "text/plain")
    };

    public async ValueTask<AuthenticationResult> ReadSignInResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        var fields = (await response.Content.ReadAsStringAsync(cancellationToken)).Split('|');
        if (fields.Length != 3 || !DateTimeOffset.TryParse(fields[2], out var expiresAt))
            throw new FormatException("Invalid fixture response.");
        return new(
            AuthenticationOutcome.Authenticated,
            "Connected.",
            new AuthenticatedSession(fields[0], fields[1], expiresAt));
    }

    public HttpRequestMessage CreateSignUpRequest(SignUpRequest request) => new(
        HttpMethod.Post,
        "fixture/sign-up")
    {
        Content = new StringContent(
            $"{request.Name}|{request.Email}|{request.Password}|{request.Phone}|{request.Country}",
            Encoding.UTF8,
            "text/plain")
    };

    public ValueTask<AuthenticationResult> ReadSignUpResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default) =>
        ReadSignInResponseAsync(response, cancellationToken);

    public HttpRequestMessage CreateSignOutRequest(AuthenticatedSession session)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "fixture/sign-out");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return request;
    }

    public ValueTask ReadSignOutResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        response.EnsureSuccessStatusCode();
        return ValueTask.CompletedTask;
    }
}

sealed class ForeignOriginProtocol : FixtureAuthenticationProtocol
{
    public override HttpRequestMessage CreateSignInRequest(SignInRequest request) =>
        new(HttpMethod.Post, "https://other.example.test/sign-in");
}

sealed class ConcurrentViewerMessageTransport : IViewerMessageTransport
{
    private Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? receiver;
    private int active;
    private int maximumConcurrency;

    public int MaximumConcurrency => maximumConcurrency;

    public void SetReceiver(Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> value) =>
        receiver = value ?? throw new ArgumentNullException(nameof(value));

    public async ValueTask SendAsync(
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken = default)
    {
        var current = Interlocked.Increment(ref active);
        UpdateMaximum(current);
        try
        {
            await Task.Delay(5, cancellationToken);
            await (receiver ?? throw new InvalidOperationException("No receiver configured."))(
                message.ToArray(), cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref active);
        }
    }

    private void UpdateMaximum(int value)
    {
        while (true)
        {
            var current = maximumConcurrency;
            if (value <= current || Interlocked.CompareExchange(ref maximumConcurrency, value, current) == current)
                return;
        }
    }
}
