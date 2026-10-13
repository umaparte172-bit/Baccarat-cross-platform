# Stage 7 viewer and communication report

## Outcome

Stage 7 adds a working, offline viewer and a replaceable communication module. Manual session changes and strategy-engine changes are encoded as versioned documents, passed through the communication boundary, validated on receipt and displayed in the Viewer tab.

This is a replacement application contract. It is not claimed to be compatible with the unavailable original server. No original executable was launched, no credentials are transmitted and no external connection is made by the default transport.

## Version model

The protocol uses two independent version fields:

- `protocol_version` identifies the outer envelope format.
- `document_version` identifies the selected payload schema.

The currently supported compatibility matrix is:

| Document type | Envelope | Document | Result |
| --- | --- | --- | --- |
| `session.snapshot` | 1.0 | 1.0 | Accepted |
| `strategy.snapshot` | 1.0 | 1.0 | Accepted |
| `communication.status` | 1.0 | 1.0 | Accepted |
| Any known type | Other | Any | Rejected |
| Any known type | 1.0 | Other | Rejected |
| Unknown type | Any | Any | Rejected |

Compatibility is exact by design. A future schema change must add an explicit version and conversion policy; it must not silently reinterpret a document.

## Components

- Core document records describe session snapshots, strategy snapshots and communication status without UI or network dependencies.
- The validator checks document identity, UTC timestamps, positive sequences, exact type/version pairs and payload invariants.
- The JSON codec rejects unknown fields, duplicate property names, malformed JSON, nesting deeper than 16 levels and documents larger than 256 KiB.
- The document store accepts only strictly increasing sequence numbers and retains a configurable bounded history (100 documents by default, maximum 10,000).
- The communication module creates identifiers and sequence numbers, serializes concurrent publication and receive processing, uses an injected transport, decodes received bytes and isolates viewer subscribers so one failed consumer cannot block the others.
- The loopback transport performs the complete encode/transmit/decode path locally. `IViewerMessageTransport` is the seam for a later evidenced WebSocket adapter.
- The desktop composition root supplies one communication module to the Session, Strategies and Viewer workspaces. Viewer callbacks are marshalled to the Avalonia UI thread before controls are updated.

## Live integration

The application publishes:

- a communication-ready status at startup;
- a session snapshot at startup and after record, undo or clear;
- a strategy snapshot after load, reset, undo, side/stake choice or recorded outcome.

The Viewer tab displays the active contract versions, latest communication state, latest session and strategy summaries, and ordered document history.

## Verification

Release build and automated checks verify:

- exact JSON round-trip behavior;
- rejection of unsupported envelope and document versions;
- rejection of duplicate JSON properties;
- duplicate and out-of-order sequence suppression;
- size, nesting-depth, unknown-type and history-capacity boundaries;
- ordered loopback delivery and subscriber-failure isolation;
- serialized delivery under concurrent publication;
- live Session-to-Viewer and Strategy-to-Viewer updates in a headless Avalonia run.

## Remaining evidence boundary

External WebSocket compatibility remains blocked on the legacy contract: endpoint, TLS expectations, authentication handshake, message framing, payload schemas, acknowledgements, heartbeat behavior and reconnection rules. These must come from sanitized VMware captures or other static evidence. Until then, the local transport remains the safe default and external actions remain disabled.
