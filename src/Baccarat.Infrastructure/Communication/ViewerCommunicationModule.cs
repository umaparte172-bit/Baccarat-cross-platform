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

    public ValueTask PublishAsync(ViewerPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var (type, version) = ContractFor(payload);
        var document = new ViewerDocument(
            ViewerDocumentContract.EnvelopeVersion,
            type,
            version,
            Guid.NewGuid(),
            Interlocked.Increment(ref sequence),
            DateTimeOffset.UtcNow,
            payload);
        return transport.SendAsync(codec.Encode(document), cancellationToken);
    }

    public ValueTask ReceiveAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = codec.Decode(message.Span);
        if (store.Apply(document) == ViewerDocumentApplyResult.Applied)
            DocumentReceived?.Invoke(document);
        return ValueTask.CompletedTask;
    }

    private static (string Type, DocumentVersion Version) ContractFor(ViewerPayload payload) => payload switch
    {
        SessionSnapshotPayload => (ViewerDocumentContract.SessionSnapshotType,
            ViewerDocumentContract.SessionSnapshotVersion),
        StrategySnapshotPayload => (ViewerDocumentContract.StrategySnapshotType,
            ViewerDocumentContract.StrategySnapshotVersion),
        CommunicationStatusPayload => (ViewerDocumentContract.StatusType, ViewerDocumentContract.StatusVersion),
        _ => throw new ArgumentException("Unsupported viewer payload type.", nameof(payload))
    };
}
