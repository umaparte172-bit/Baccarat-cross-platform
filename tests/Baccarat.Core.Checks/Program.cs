using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Baccarat.Core;
using Baccarat.Core.Authentication;
using Baccarat.Core.Configuration;
using Baccarat.Infrastructure.Authentication;
using Baccarat.Infrastructure.Diagnostics;
using Baccarat.Infrastructure.Security;

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

var response = new Queue<AuthenticationResult?>();
var authentication = new StubAuthenticationService(response);
var controller = new AuthenticationController(authentication);

Check((await controller.SignInAsync("", "")).Outcome == AuthenticationOutcome.ValidationFailed,
    "Empty credentials must fail before transport");
Check(authentication.SignInCalls == 0, "Invalid input must not call authentication transport");
Check((await controller.SignInAsync("not-an-email", "secret")).Outcome == AuthenticationOutcome.ValidationFailed,
    "Malformed email must fail before transport");

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
