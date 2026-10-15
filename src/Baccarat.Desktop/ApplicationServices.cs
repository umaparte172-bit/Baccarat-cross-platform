using Baccarat.Core.Automation;
using Baccarat.Core.Configuration;
using Baccarat.Core.Diagnostics;
using Baccarat.Infrastructure.Automation;
using Baccarat.Infrastructure.Communication;
using Baccarat.Infrastructure.Diagnostics;

namespace Baccarat.Desktop;

public sealed record ApplicationServices(
    AutomationController Automation,
    ViewerCommunicationModule ViewerCommunication,
    IDiagnosticSink Diagnostics,
    NetworkClientOptions Network)
{
    public static ApplicationServices CreateDefault()
    {
        var network = NetworkClientOptions.Disconnected;
        if (network.Validate().Count != 0)
            throw new InvalidOperationException("Default network configuration is invalid.");

        return new(
            new AutomationController(new SimulatedAutomationTableAdapter()),
            new ViewerCommunicationModule(new LoopbackViewerMessageTransport()),
            new RedactingDiagnosticSink(_ => { }),
            network);
    }
}
