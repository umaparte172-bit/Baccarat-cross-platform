namespace Baccarat.Core.Authentication;

public sealed record SignInRequest(string Email, string Password);

public sealed record SignUpRequest(
    string Name,
    string Email,
    string Password,
    string Phone,
    string Country);

public sealed record AuthenticatedSession(string UserId, string AccessToken, DateTimeOffset? ExpiresAt);

public enum AuthenticationOutcome
{
    Authenticated,
    Expired,
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
    Task<AuthenticationResult> SignUpAsync(SignUpRequest request, CancellationToken cancellationToken = default);
    Task SignOutAsync(AuthenticatedSession session, CancellationToken cancellationToken = default);
}

public interface IAuthenticationSessionStore
{
    ValueTask SaveAsync(AuthenticatedSession session, CancellationToken cancellationToken = default);
    ValueTask<AuthenticatedSession?> ReadAsync(CancellationToken cancellationToken = default);
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

public sealed class NullAuthenticationSessionStore : IAuthenticationSessionStore
{
    public static NullAuthenticationSessionStore Instance { get; } = new();

    private NullAuthenticationSessionStore()
    {
    }

    public ValueTask SaveAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask<AuthenticatedSession?> ReadAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<AuthenticatedSession?>(null);

    public ValueTask ClearAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

