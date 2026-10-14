using Baccarat.Core.Authentication;

namespace Baccarat.Infrastructure.Authentication;

/// <summary>Public, local-only credentials for exercising the UI without a server.</summary>
public sealed class DemoAuthenticationService : IAuthenticationService
{
    public const string Email = "demo@baccarat.local";
    public const string Password = "Baccarat-Demo-2026!";

    public Task<AuthenticationResult> SignInAsync(
        SignInRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Matches(request.Email, request.Password)
            ? CreateSession()
            : new AuthenticationResult(AuthenticationOutcome.Rejected,
                "Demo credentials did not match. No server request was made."));
    }

    public Task<AuthenticationResult> SignUpAsync(
        SignUpRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Matches(request.Email, request.Password)
            ? CreateSession()
            : new AuthenticationResult(AuthenticationOutcome.Rejected,
                $"For this offline demo, use {Email} and the published demo password. No account was created."));
    }

    public Task SignOutAsync(AuthenticatedSession session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private static bool Matches(string email, string password) =>
        string.Equals(email, Email, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(password, Password, StringComparison.Ordinal);

    private static AuthenticationResult CreateSession() => new(
        AuthenticationOutcome.Authenticated,
        "Local demo access enabled. No account or server session was created.",
        new AuthenticatedSession("local-demo", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddHours(8)));
}
