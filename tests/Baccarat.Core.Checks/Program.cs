using Baccarat.Core;
using Baccarat.Core.Authentication;

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
