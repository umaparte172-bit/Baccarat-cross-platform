namespace Baccarat.Core.Authentication;

public sealed record SignInRequest(string Email, string Password);

public sealed record AuthenticatedSession(string UserId, string AccessToken, DateTimeOffset? ExpiresAt);

public enum AuthenticationOutcome
{
    Authenticated,
    ValidationFailed,
    Rejected,
    EmptyResponse,
    MalformedResponse,
    Unavailable,
    UnexpectedError
}

public sealed record AuthenticationResult(
    AuthenticationOutcome Outcome,
    string Message,
    AuthenticatedSession? Session = null)
{
    public bool IsAuthenticated => Outcome == AuthenticationOutcome.Authenticated && Session is not null;
}

public interface IAuthenticationService
{
    Task<AuthenticationResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default);
    Task SignOutAsync(AuthenticatedSession session, CancellationToken cancellationToken = default);
}

