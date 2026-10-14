using Baccarat.Core.Authentication;

namespace Baccarat.Infrastructure.Authentication;

/// <summary>Persists a session only when the user has opted into Remember Me.</summary>
public sealed class OptInAuthenticationSessionStore(IAuthenticationSessionStore inner) : IAuthenticationSessionStore
{
    public bool Remember { get; set; }

    public ValueTask SaveAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Remember ? inner.SaveAsync(session, cancellationToken) : inner.ClearAsync(cancellationToken);

    public async ValueTask<AuthenticatedSession?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var session = await inner.ReadAsync(cancellationToken);
        Remember = session is not null;
        return session;
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        Remember = false;
        return inner.ClearAsync(cancellationToken);
    }
}
