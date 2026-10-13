using System.Text.Json;
using Baccarat.Core.Authentication;
using Baccarat.Core.Security;

namespace Baccarat.Infrastructure.Authentication;

public sealed class SecretAuthenticationSessionStore(ISecretStore secrets) : IAuthenticationSessionStore
{
    private const string SessionKey = "authentication.session.v1";

    public async ValueTask SaveAsync(AuthenticatedSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var value = JsonSerializer.Serialize(session);
        await secrets.SaveAsync(SessionKey, value, cancellationToken);
    }

    public async ValueTask<AuthenticatedSession?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var value = await secrets.ReadAsync(SessionKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(value)) return null;

        try
        {
            return JsonSerializer.Deserialize<AuthenticatedSession>(value);
        }
        catch (JsonException)
        {
            await secrets.DeleteAsync(SessionKey, cancellationToken);
            return null;
        }
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default) =>
        secrets.DeleteAsync(SessionKey, cancellationToken);
}
