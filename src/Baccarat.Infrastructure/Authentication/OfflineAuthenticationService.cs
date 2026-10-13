using Baccarat.Core.Authentication;

namespace Baccarat.Infrastructure.Authentication;

public sealed class OfflineAuthenticationService : IAuthenticationService
{
    public Task<AuthenticationResult> SignInAsync(
        SignInRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AuthenticationResult(
            AuthenticationOutcome.Unavailable,
            "The original authentication contract has not yet been recovered. Offline tools remain available."));

    public Task<AuthenticationResult> SignUpAsync(
        SignUpRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AuthenticationResult(
            AuthenticationOutcome.Unavailable,
            "The original registration contract has not yet been recovered. Offline tools remain available."));

    public Task SignOutAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
