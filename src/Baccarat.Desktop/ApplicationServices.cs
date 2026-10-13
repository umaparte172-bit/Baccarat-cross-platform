using Baccarat.Core.Authentication;
using Baccarat.Core.Configuration;
using Baccarat.Core.Diagnostics;
using Baccarat.Core.Security;
using Baccarat.Infrastructure.Authentication;
using Baccarat.Infrastructure.Diagnostics;
using Baccarat.Infrastructure.Security;

namespace Baccarat.Desktop;

public sealed record ApplicationServices(
    IAuthenticationService Authentication,
    IAuthenticationSessionStore AuthenticationSessions,
    ISecretStore Secrets,
    IDiagnosticSink Diagnostics,
    NetworkClientOptions Network)
{
    public static ApplicationServices CreateDefault()
    {
        var network = NetworkClientOptions.Disconnected;
        if (network.Validate().Count != 0)
            throw new InvalidOperationException("Default network configuration is invalid.");

        var secrets = new VolatileSecretStore();
        return new(
            new OfflineAuthenticationService(),
            new SecretAuthenticationSessionStore(secrets),
            secrets,
            new RedactingDiagnosticSink(_ => { }),
            network);
    }
}
