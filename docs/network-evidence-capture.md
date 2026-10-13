# Network evidence required for Stage 3

The repository currently contains no `.pcap`, `.pcapng`, `.har`, `.saz`, proxy-flow export or application network log. Static analysis recovered the network components and protected configuration call sites, but the protector hides the plaintext hosts, routes and message bodies.

## Minimum useful evidence

Two files collected inside the disposable VMware guest are requested:

1. A Wireshark `.pcapng` covering one application launch and one dummy sign-in or sign-up attempt.
2. A decrypted HTTPS proxy export, preferably a Fiddler Classic `.saz`, covering the same attempt.

The packet capture establishes DNS, destination addresses, TLS negotiation, connection ordering and WebSocket upgrades. The decrypted proxy export is needed for URLs, methods, headers, cookies, JSON fields and response bodies. A packet capture alone normally cannot reveal those values because port 443 content is encrypted.

## Safety conditions

- Work only in the VMware snapshot, never on the analysis host.
- Use a disposable name, email, password and phone number. Do not use a real account or reused password.
- Do not place a bet, submit a withdrawal, send a tip or provide cryptocurrency addresses.
- Start capture immediately before launching the application and stop immediately after the failure appears.
- Record the guest clock time and the exact action attempted.
- Keep the original executable and installed directory unchanged; revert the snapshot after collection.

## Capture A: Wireshark

1. Start a new capture on the VMware network interface.
2. Launch the original application.
3. Perform exactly one dummy sign-in or sign-up attempt.
4. Wait for the success or failure dialog, then stop the capture.
5. Save the complete capture as `baccarat-auth-attempt.pcapng`.
6. Do not export only the final reset packet. The beginning of every TCP stream is required.

Useful display filters for checking the result:

```text
dns or tls.handshake.extensions_server_name or tcp.port == 443
```

```text
dns.a == 103.224.182.247
```

```text
tcp.stream == 34
```

The stream number may differ in a new capture.

## Capture B: decrypted HTTPS session

Fiddler Classic is suitable because the original application uses the Windows/.NET networking stack.

1. Create or revert to a disposable VMware snapshot.
2. Install Fiddler Classic in the guest from its official distribution.
3. In Fiddler, enable HTTPS traffic decryption and trust its generated root certificate **inside the disposable guest only**.
4. Start capture, launch the application and perform one dummy authentication attempt.
5. Stop capture immediately after the result dialog.
6. Save all sessions as `baccarat-auth-attempt.saz`.
7. Revert the VMware snapshot afterward to remove the temporary interception certificate.

If no application sessions appear, report that outcome and provide the `.pcapng`. Do not disable certificate validation or patch TLS checks. The absence may indicate that the client ignores the system proxy, uses certificate pinning, fails before HTTP, or uses only its WebSocket path.

## Accompanying note

Provide a short text file containing:

```text
Guest date/time and timezone:
Application action: Sign-in or Sign-up
Result shown by application:
Fiddler captured application sessions: Yes/No
Wireshark source IP:
Any VM proxy/VPN in use:
```

Do not include the dummy password in this note.

## What can be recovered

From the supplied evidence, Stage 3 can implement and test:

- HTTP and WebSocket hostnames;
- routes and HTTP methods;
- required headers, API identifiers and content types;
- cookie and bearer-token behavior;
- request JSON/form fields;
- success and failure response models;
- WebSocket upgrade headers and message envelopes when visible;
- timeout, reset and reconnect behavior observable across repeated captures.

Sensitive values will be replaced with placeholders before fixtures or documentation are committed. Raw captures remain ignored and will not be pushed to GitHub.

## Static-only boundary

If runtime observation of any kind is prohibited, exact network compatibility cannot be guaranteed from this protected executable. Continued static work may recover more implementation detail, but the current on-disk authentication bodies are stubs and their real strings and request construction are restored only at runtime. In that situation Stage 3 must remain an adapter implementation with unverified contract placeholders, not a claim of compatibility.

