# Static network recovery checkpoint — 2026-10-16

The user now requests both original accounts and live-table integration. This
reopens network work after the earlier independent/offline scope. No credentials
were submitted and the original executable was not run.

## New recovery evidence

`tools/recover_static_strings.py` interprets only the audited string decoder's
data operations in Python, with an instruction limit, bounded arrays and an
explicit call allowlist. It does not load the CLR image or invoke native methods.
The input is pinned to SHA-256
`b051ade2e179154614bd566adc35c5c3672a04f899b891febafbf0cd4045ecb1`.

It recovers a 28,556-byte table containing 582 validated length-prefixed UTF-16
strings. It stops at the unknown void helper `m5rkLafepqV0jHLI0E` after table
initialization; that helper is not executed or assumed harmless. The output
table is structurally validated before reading any recovered strings.

| Offset | Recovered evidence |
| --- | --- |
| 18936 | HTTP base: `https://buyer-api.otbx.com/api/` |
| 19002 | Authentication WebSocket: `wss://buyer-api.otbx.com/ws/products` |
| 19078 | Download base: `https://downloads.wetalkbet.com/products/baccarat_evolution` |
| 118, 274, 450 | Route strings: `jwt/get-profile`, `sign-in`, `sign-up` |
| 32, 188, 292 | Cookie identifiers: `otbx_rt`, `Cookie`, `set-cookie` |
| 250, 264 | Sign-in-associated field strings: `email`, `pwd` |
| 350–414 | Signup-associated strings: `name`, `country`, `phone`, `password`, `sign_up_platform` |
| 20526, 20622 | WebSocket message names: `check-token-request`, `signin-with-account-req` |
| 21056, 21106 | Response names: `check-token-res`, `signin-with-account-res` |
| 21386 | Local WebSocket candidate: `ws://127.0.0.1:8080` |
| 22290 | Browser destination candidate: `https://www.roobet.com/` |

Embedded API/service identifiers are deliberately excluded from reports and
production configuration. String proximity supports candidate field/route
associations, not proof of the HTTP verb, nesting, header placement or success
conditions. Protected authentication method bodies remain unrecovered.

The live-table strings include absolute XPath selectors rooted at `//*[@id='root']`,
`PLACE YOUR BETS`, `WAIT FOR NEXT GAME`, `PLAYER`, `BANKER`, `TIE`, and `data-role=`.
Startup batch files identify browser debugging ports 9014/9015 and distinct
profiles. This supports browser-based table interaction. It does not establish
which selector belongs to each action, which frames to enter, current provider
compatibility, chip values or placement acknowledgements. The recovered Roobet
URL is historical evidence, not a confirmed client choice or current contract.

## Current API response

On 2026-10-16, a credential-free HTTPS HEAD followed by GET to the exact recovered
base `/api/` returned HTTP 200 with `text/html`. The body is a parking page titled
`otbx.com`, stating that the domain may be for sale, and references an
`assets.abovedomains.com` parking script. No redirects were followed and no TLS
checks were disabled. The script was not executed.

This is not a successful authentication response. Do not send user credentials
or the recovered embedded identifiers to that endpoint. It also does not prove
the service can never return or that every possible route was tested. A working,
verified operator endpoint is needed to restore original server accounts.

## Remaining work

1. Recover the protected method bodies or equivalent protocol evidence to
   establish HTTP verbs, exact payloads, cookies, response validation and socket
   lifecycle. The decoded string table is a substantial advance over prior
   reports that said endpoints were unknown; it is not a complete protocol.
2. Establish a live operator-controlled account endpoint. Client changes cannot
   restore the old service's account database or turn its parking page into it.
3. Recover the browser workflow and selector-to-action mapping, then validate
   against the actual table before enabling live result ingestion or placement.

Production still uses offline authentication, local Viewer transport and the
simulation adapter. No success is fabricated and no casino action was issued.
