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
    ISecretStore Secrets,
    IDiagnosticSink Diagnostics,
    NetworkClientOptions Network)
{
    public static ApplicationServices CreateDefault()
    {
        var network = NetworkClientOptions.Disconnected;
        if (network.Validate().Count != 0)
            throw new InvalidOperationException("Default network configuration is invalid.");

        return new(
            new OfflineAuthenticationService(),
            new VolatileSecretStore(),
            new RedactingDiagnosticSink(_ => { }),
            network);
    }
}
