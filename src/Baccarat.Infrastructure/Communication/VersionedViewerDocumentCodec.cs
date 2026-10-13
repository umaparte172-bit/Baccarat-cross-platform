using System.Text.Json;
using System.Text.Json.Serialization;
using Baccarat.Core.Communication;
using Baccarat.Core.Strategies;

namespace Baccarat.Infrastructure.Communication;

public sealed class ViewerDocumentFormatException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class VersionedViewerDocumentCodec
{
    public const int MaximumDocumentBytes = 256 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };

    public byte[] Encode(ViewerDocument document)
    {
        var errors = ViewerDocumentValidator.Validate(document);
        if (errors.Count != 0) throw new ViewerDocumentFormatException(string.Join(" ", errors));

        var payload = document.Payload switch
        {
            SessionSnapshotPayload value => JsonSerializer.SerializeToElement(new SessionPayloadDocument(
                value.RoundCount, value.PlayerCount, value.BankerCount, value.TieCount), Options),
            StrategySnapshotPayload value => JsonSerializer.SerializeToElement(new StrategyPayloadDocument(
                value.StrategyName, value.Profit, value.ObservedRounds, value.Bets,
                value.State.ToString(), value.Recommendation), Options),
            CommunicationStatusPayload value => JsonSerializer.SerializeToElement(
                new StatusPayloadDocument(value.State, value.Message), Options),
            _ => throw new ViewerDocumentFormatException("Unsupported viewer payload type.")
        };

        var encoded = JsonSerializer.SerializeToUtf8Bytes(new EnvelopeDocument(
            document.EnvelopeVersion.ToString(),
            document.DocumentType,
            document.DocumentVersion.ToString(),
            document.DocumentId,
            document.Sequence,
            document.CreatedAt,
            payload), Options);
        if (encoded.Length > MaximumDocumentBytes)
            throw new ViewerDocumentFormatException($"Viewer documents cannot exceed {MaximumDocumentBytes} bytes.");
        return encoded;
    }

    public ViewerDocument Decode(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty) throw new ViewerDocumentFormatException("The viewer document is empty.");
        if (value.Length > MaximumDocumentBytes)
            throw new ViewerDocumentFormatException($"Viewer documents cannot exceed {MaximumDocumentBytes} bytes.");

        try
        {
            using var parsed = JsonDocument.Parse(value.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            RejectDuplicateProperties(parsed.RootElement);
            var envelope = JsonSerializer.Deserialize<EnvelopeDocument>(value, Options) ??
                throw new ViewerDocumentFormatException("The viewer document does not contain an envelope.");
            var envelopeVersion = DocumentVersion.Parse(envelope.ProtocolVersion);
            var documentVersion = DocumentVersion.Parse(envelope.DocumentVersion);
            ViewerPayload payload = envelope.DocumentType switch
            {
                ViewerDocumentContract.SessionSnapshotType => DecodeSession(envelope.Payload),
                ViewerDocumentContract.StrategySnapshotType => DecodeStrategy(envelope.Payload),
                ViewerDocumentContract.StatusType => DecodeStatus(envelope.Payload),
                _ => throw new ViewerDocumentFormatException($"Unsupported document type '{envelope.DocumentType}'.")
            };
            var document = new ViewerDocument(
                envelopeVersion,
                envelope.DocumentType,
                documentVersion,
                envelope.DocumentId,
                envelope.Sequence,
                envelope.CreatedAt,
                payload);
            var errors = ViewerDocumentValidator.Validate(document);
            if (errors.Count != 0) throw new ViewerDocumentFormatException(string.Join(" ", errors));
            return document;
        }
        catch (JsonException exception)
        {
            throw new ViewerDocumentFormatException("The viewer document is not valid supported JSON.", exception);
        }
        catch (FormatException exception)
        {
            throw new ViewerDocumentFormatException(exception.Message, exception);
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new ViewerDocumentFormatException($"Duplicate JSON property '{property.Name}' is not allowed.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
        }
    }

    private static SessionSnapshotPayload DecodeSession(JsonElement payload)
    {
        var value = payload.Deserialize<SessionPayloadDocument>(Options) ??
            throw new ViewerDocumentFormatException("The session payload is missing.");
        return new(value.RoundCount, value.PlayerCount, value.BankerCount, value.TieCount);
    }

    private static StrategySnapshotPayload DecodeStrategy(JsonElement payload)
    {
        var value = payload.Deserialize<StrategyPayloadDocument>(Options) ??
            throw new ViewerDocumentFormatException("The strategy payload is missing.");
        if (!Enum.TryParse<StrategyEngineState>(value.State, false, out var state))
            throw new ViewerDocumentFormatException($"Unsupported strategy state '{value.State}'.");
        return new(value.StrategyName, value.Profit, value.ObservedRounds, value.Bets, state, value.Recommendation);
    }

    private static CommunicationStatusPayload DecodeStatus(JsonElement payload)
    {
        var value = payload.Deserialize<StatusPayloadDocument>(Options) ??
            throw new ViewerDocumentFormatException("The status payload is missing.");
        return new(value.State, value.Message);
    }

    private sealed record EnvelopeDocument(
        [property: JsonPropertyName("protocol_version")] string ProtocolVersion,
        [property: JsonPropertyName("document_type")] string DocumentType,
        [property: JsonPropertyName("document_version")] string DocumentVersion,
        [property: JsonPropertyName("document_id")] Guid DocumentId,
        [property: JsonPropertyName("sequence")] long Sequence,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("payload")] JsonElement Payload);

    private sealed record SessionPayloadDocument(
        [property: JsonPropertyName("round_count")] int RoundCount,
        [property: JsonPropertyName("player_count")] int PlayerCount,
        [property: JsonPropertyName("banker_count")] int BankerCount,
        [property: JsonPropertyName("tie_count")] int TieCount);

    private sealed record StrategyPayloadDocument(
        [property: JsonPropertyName("strategy_name")] string StrategyName,
        [property: JsonPropertyName("profit")] decimal Profit,
        [property: JsonPropertyName("observed_rounds")] int ObservedRounds,
        [property: JsonPropertyName("bets")] int Bets,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("recommendation")] string Recommendation);

    private sealed record StatusPayloadDocument(
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("message")] string Message);
}
