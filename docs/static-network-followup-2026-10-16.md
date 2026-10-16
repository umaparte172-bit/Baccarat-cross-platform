# Further static recovery — 2026-10-16

## Scope and result

Inspected the supplied installed v2 executable as data only. SHA-256:
`b051ade2e179154614bd566adc35c5c3672a04f899b891febafbf0cd4045ecb1`.
No original CLR code, native functions, browser automation, authentication requests
or betting actions were executed. No new server availability check was made.

**This pass recovered additional structural evidence, not a working protocol or
the protected business method bodies. Live integration remains incomplete.**

## Newly recorded evidence

The new audit identifies 65 network/JSON/browser member references. Relevant examples:

| Metadata token | Referenced member | What it establishes |
|---|---|---|
| `0x0A00017A` | `HttpMethod.get_Post` | POST is referenced by the assembly |
| `0x0A00017B` | `JsonConvert.SerializeObject` | JSON serialization is referenced |
| `0x0A00017F` | `HttpClient.SendAsync` | HTTP request sending is referenced |
| `0x0A000184` | `HttpClient.PostAsync` | A direct POST API is also referenced |
| `0x0A000187` | `HttpHeaders.GetValues` | Response/header value access exists |
| `0x0A000176` | `HttpClientHandler.set_UseCookies` | Cookie handling can be configured; the setting value is not recovered here |
| `0x0A0001A7` | `ITargetLocator.Frame` | Browser frame switching is referenced |
| `0x0A0001A8` | `By.XPath` | XPath lookup is referenced |
| `0x0A0001AE` | `By.CssSelector` | CSS lookup is referenced |
| `0x0A0001D9` | `IWebElement.Click` | Element clicking is referenced |
| `0x0A00024D` | `ChromeOptions.set_DebuggerAddress` | Attaching through browser debugging is supported by the referenced API |
| `0x0A000295` | `WebSocket.Send` | WebSocket transmission is referenced |
| `0x0A0002C4` | `WebSocket.add_MessageReceived` | WebSocket receive callbacks are referenced |

These are metadata references, **not a recovered call graph linking a particular
route, payload, selector or user action**. In particular, this does not prove that
`sign-up` uses POST with a specific JSON shape, or that a socket carries casino bets.

The `<PlaceBet>d__95` state machine retains `_betOn : System.String` and
`_amount : System.Int32`, with an async-task builder and task awaiter. This identifies
the internal parameter representation only: it does not establish currency units,
chip denominations, rounding, a network schema or placement acknowledgment rules.
`<AutoBet>d__94` retains `winStatus : System.String`. `Baccarat.WS_Authentication`
contains an HTTP client, a WebSocket4Net socket, timer and `lastMessageReceived`.
This is further evidence that account socket handling must not automatically be
treated as a casino result feed.

## Attempt to advance the protection initializer

The initializer `jcCPYVXWLf`, token `0x0600022B`, has 6,125 parsed instructions.
The bounded interpreter followed its entry path using only audited data operations:

- `MdswMdu9Vk = false`, verified in the static constructor.
- One helper returns true via `ldnull; ldnull; ceq; ret`.
- Another returns null via `ldnull; ret`.

After 29 steps it stopped at IL offset 14703 (`ldloca.s`). The next inspected
instructions convert a local address into `IntPtr` and call helper `0x06000287`,
which references `Marshal.ReadIntPtr`. The current interpreter has no symbolic
address/memory model and refuses this operation. It did not silently skip the call.

The assembly also imports `LoadLibrary` and `GetProcAddress`. Those imports alone
do not establish maliciousness and were not invoked.

This boundary is a limitation of the current recovery implementation, not proof
that static recovery cannot go further. A next static attempt would need a
validated symbolic address/memory model and analysis of the subsequent branches,
or an independently validated reconstruction of the resource transform. Neither
has been completed here. No new method-body plaintext was recovered in this pass.

## Reproduce and verification

```text
python tools/recover_network_evidence.py "Original Application/Wetalkbet (CProgram Files (x86))/Baccarat(Evolution)/Baccarat (Evolution).exe"
python -m unittest discover -s tests -p "test_*.py"
```

The probe pins the sample hash, uses the existing bounded interpreter and emits
metadata only, excluding embedded credentials and user settings. The new test
checks the exact stop boundary, referenced operations and placement field type.
All 18 static-analysis tests passed locally with the supplied sample. Sample-based
tests skip in CI when the private original binary is absent.

The previous [network report](static-network-recovery-2026-10-16.md) remains the
source for the 582 decoded strings and the earlier parking-page observation.
Those are prior findings, not new endpoint recovery in this pass.

## Deeper continuation (same date, subsequent request)

The earlier 29-step stop above is now historical. Tracing confirmed local 16 was
explicitly initialized to zero before its address was taken. A narrowly bounded
symbolic-local model now handles that address and the audited ReadIntPtr/Int32/Int64
and WriteIntPtr/Int32/Int64 wrappers, only at offset zero with zero values. It reads
and writes Python interpreter locals, never actual process memory. Foreign addresses,
nonzero offsets/values and uninitialized locals are rejected by regression tests.

The probe now reaches **94 interpreted steps**, stopping at helper `0x0600028D`
(`oK7ThRlrRgiEaXxw3Y`), which wraps `Marshal.AllocCoTaskMem`. No native allocator
was invoked or substituted. A bounded symbolic heap is the next unimplemented
component; further native/dynamic-code boundaries may follow. This is a genuine
advance in the static interpreter, **not recovery of the hidden betting protocol**.
