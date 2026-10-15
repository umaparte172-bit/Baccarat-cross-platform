# Original Wetalkbet account integration: blocked, not complete

Checked 2026-10-15. The user explicitly requires original Wetalkbet server accounts,
not local accounts. This supersedes the earlier no-login request. The current
application now starts at restored Sign In/Sign Up screens. Successful
authentication shows the workspace with top-right Sign Out. The default service
reports unavailable; no working Wetalkbet server login or registration is claimed.
An explicit Open Offline Workspace option preserves the existing local tools.

## Evidence checked

- `F:/Signup.pcapng`: 73,884 bytes; SHA-256
  `aa47afb72028410e7480c5e1eba7e7a49b813c6a794aca626d4fbff47931aec2`.
- 259 packets; three TCP/TLS streams to `103.224.182.247:443`, with
  `buyer-api.otbx.com` in DNS and TLS SNI. Encrypted application-data records
  do not establish an HTTP success or failure result.
- No plaintext HTTP detected by the existing local parser. The capture contains
  Section Header, Interface Description, Enhanced Packet and Interface Statistics
  blocks, but no Decryption Secrets Block. No separate TLS keys or decrypted proxy
  export have been supplied. The parser is not a full TCP/TLS reassembly or
  process-attribution tool.
- Installed config files expose no API contract; both supplied user config files
  have empty UserName and Password settings.
- Static metadata proves HTTP/WebSocket authentication method names, but protected
  disk stubs do not provide routes, headers, serialization or response models.
- Only test implementations of `IAuthenticationHttpProtocol` exist in the repo.
  `HttpAuthenticationService` is generic transport, not a Wetalkbet integration.
  Demo authentication is not server registration.
- One credential-free HTTPS HEAD request to the captured hostname's root failed
  with curl/Schannel error 56: server closed abruptly, missing close_notify.
  No credentials were sent, redirects followed or TLS checks bypassed. This does
  not prove permanent shutdown or test an unknown signup route.

## Required to finish actual server authentication

The smallest next input is a decrypted capture of the failing signup request and
response, or an official API contract. Use [the VMware guide](network-evidence-capture.md).
A packet capture without TLS keys cannot establish an encrypted HTTP method,
path, body or error message.

Once evidenced, implement the Wetalkbet protocol codec, verify success/failure
fixtures, wire the account screen to that service, protect restored sessions and
test sign-out and expiry. Startup checks and WebSocket authentication must also
be validated. If the service is retired or refuses this app, its operator must
provide a working endpoint or restore it. Client code cannot create accounts in
an unavailable service. Do not send credentials to an unverified replacement host.

## Other unfinished original-app parity

PATTERN's automatic side/stake rules remain unrecovered, although its manual
workflow is tested. Other opaque selectors and Ladder still need rule evidence.
The Viewer protocol is local and automation uses a simulation, not a live table.
Exact Main-form workflow parity and interactive macOS acceptance remain open.
Passing replacement tests does not establish those missing features.

The follow-up restores account UI wiring and tests signup/signin success,
rejection and signout using injected test fixtures. Default production composition
does not use demo credentials or fake server success. The original EXE was not run.
All existing local-workspace checks still pass; this does not establish live parity.
