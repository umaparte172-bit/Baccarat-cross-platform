using Baccarat.Core.Communication;

namespace Baccarat.Infrastructure.Communication;

public interface IViewerMessageTransport
{
    void SetReceiver(Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> receiver);
    ValueTask SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default);
}

public sealed class LoopbackViewerMessageTransport : IViewerMessageTransport
{
    private Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? receiver;

    public void SetReceiver(Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> receiver) =>
        this.receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));

    public ValueTask SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = receiver ?? throw new InvalidOperationException("No viewer message receiver is configured.");
        return target(message.ToArray(), cancellationToken);
    }
}

public sealed class ViewerCommunicationModule
{
    private readonly VersionedViewerDocumentCodec codec;
    private readonly IViewerMessageTransport transport;
    private readonly ViewerDocumentStore store;
    private readonly SemaphoreSlim publishGate = new(1, 1);
    private readonly SemaphoreSlim receiveGate = new(1, 1);
    private long sequence;

    public ViewerCommunicationModule(
        IViewerMessageTransport transport,
        VersionedViewerDocumentCodec? codec = null,
        ViewerDocumentStore? store = null)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.codec = codec ?? new VersionedViewerDocumentCodec();
        this.store = store ?? new ViewerDocumentStore();
        transport.SetReceiver(ReceiveAsync);
    }

    public event Action<ViewerDocument>? DocumentReceived;

    public IReadOnlyList<ViewerDocument> Documents => store.Documents;

    public async ValueTask PublishAsync(ViewerPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        await publishGate.WaitAsync(cancellationToken);
        try
        {
            var (type, version) = ContractFor(payload);
            var document = new ViewerDocument(
                ViewerDocumentContract.EnvelopeVersion,
                type,
                version,
                Guid.NewGuid(),
                Interlocked.Increment(ref sequence),
                DateTimeOffset.UtcNow,
                payload);
            await transport.SendAsync(codec.Encode(document), cancellationToken);
        }
        finally
        {
            publishGate.Release();
        }
    }

    public async ValueTask ReceiveAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        await receiveGate.WaitAsync(cancellationToken);
        try
        {
            var document = codec.Decode(message.Span);
            if (store.Apply(document) == ViewerDocumentApplyResult.Applied)
            {
                var handlers = DocumentReceived?.GetInvocationList() ?? [];
                foreach (Action<ViewerDocument> handler in handlers.Cast<Action<ViewerDocument>>())
                {
                    try { handler(document); }
                    catch { }
                }
            }
        }
        finally
        {
            receiveGate.Release();
        }
    }

    private static (string Type, DocumentVersion Version) ContractFor(ViewerPayload payload) => payload switch
    {
        SessionSnapshotPayload => (ViewerDocumentContract.SessionSnapshotType,
            ViewerDocumentContract.SessionSnapshotVersion),
        StrategySnapshotPayload => (ViewerDocumentContract.StrategySnapshotType,
            ViewerDocumentContract.StrategySnapshotVersion),
        CommunicationStatusPayload => (ViewerDocumentContract.StatusType, ViewerDocumentContract.StatusVersion),
        AutomationSnapshotPayload => (ViewerDocumentContract.AutomationSnapshotType,
            ViewerDocumentContract.AutomationSnapshotVersion),
        _ => throw new ArgumentException("Unsupported viewer payload type.", nameof(payload))
    };
}
