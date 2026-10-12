# Phase 2 static recovery report

Phase completed: 2026-10-13. Source: installed version 2 client with SHA-256 `B051ADE2E179154614BD566ADC35C5C3672A04F899B891FEBAFBF0CD4045ECB1`. No supplied program was executed.

## Outcome

The version 2 client is a protected .NET Framework 4.7.2 WinForms application. Ordinary metadata remains readable, but the protector replaces application method bodies with small stubs and restores behavior at runtime. Static recovery therefore produced a reliable structural specification, startup branch, configuration call sites, and workflow inventory, but not the original event-handler logic or plaintext service contracts.

The reproducible analyzer is `tools/phase2_metadata.py`. Its ignored JSON output is `artifacts/phase2-metadata.json`.

## Application structure

| Type | Type token | Base | Fields | Methods | On-disk method result |
|---|---:|---|---:|---:|---|
| `Baccarat.Main` | `0x02000006` | `System.Windows.Forms.Form` | 177 | 146 | All 146 stub-like |
| `Baccarat.SignIn` | `0x0200003B` | `System.Windows.Forms.Form` | 11 | 9 | All 9 stub-like |
| `Baccarat.SignUp` | `0x0200003C` | `System.Windows.Forms.Form` | 16 | 7 | All 7 stub-like |

### Recoverable controls

| Control type | Main | SignIn | SignUp |
|---|---:|---:|---:|
| Button | 6 | 2 | 2 |
| CheckBox | 1 | 2 | 1 |
| ComboBox | 7 | 0 | 0 |
| DataGridView | 1 | 0 | 0 |
| Label | 45 | 2 | 5 |
| Panel | 12 | 1 | 1 |
| PictureBox | 1 | 1 | 1 |
| RadioButton | 2 | 0 | 0 |
| TextBox | 14 | 2 | 5 |

`Baccarat.Main` also declares thirteen `OpenQA.Selenium.IWebElement` fields, nine `System.Data.DataTable` fields, a `System.Timers.Timer`, a `Random.Org.Random` instance, twenty-eight strings, twenty-three integers and other session state. These field types support the previously identified combined UI, browser-automation, strategy and session roles.

Control field names are obfuscated. Labels, layout assignments and event subscriptions occur in runtime-restored bodies; they are not present in the static constructor bodies. Accordingly, mapping individual obfuscated controls to visible captions would be speculation. The supplied SignUp screenshot independently confirms five inputs corresponding to name, email, password, phone and country.

## Startup and offline-screen evidence

`Baccarat.Program.Main` is method token `0x060001AF`, RVA `0x36E0`. Its surviving IL:

1. Initializes HTTP authentication using `url`, `api_key`, `store_id`, `service_id` and `platform`.
2. Initializes WebSocket authentication using `ws_url` and the same identifiers.
3. Calls `Baccarat.WS_Authentication.CheckToken` (`0x060001C8`).
4. If true, constructs method token `0x06000026`, the `Baccarat.Main` constructor.
5. If false, constructs method token `0x060001B7`, the `Baccarat.SignIn` constructor.

This proves the main form is the post-token screen and identifies the exact branch relevant to a disposable VMware inspection patch. It does not prove that the main form will remain stable without the runtime-restored session state.

## Authentication and remote-operation bodies

The following on-disk bodies contain only no-ops plus a constant/null return or `ret`:

| Component | Tokens / methods |
|---|---|
| HTTP authentication | `Initialize` `0x06000004`, `CheckToken` `0x06000005`, `Signin` `0x06000006`, `SignUp` `0x06000007`, `Signout` `0x06000008`, `ValidateCookies` `0x06000009` |
| WebSocket authentication | `Initialize` `0x060001C7`, `CheckToken` `0x060001C8`, `Signin` `0x060001C9`, `SignUp` `0x060001CA`, `Signout` `0x060001CB`, `ValidateServices` `0x060001CC`, `SendData` `0x060001D1` |
| Financial/account helpers | `SendTip` `0x06000165`, `Withdraw` `0x06000166`, `TwoFactorCheck` `0x06000167`, `GetCoinAddresses` `0x06000168` |
| Local cryptography helpers | `Encrypt` `0x06000178`, `Decrypt` `0x06000179` |

The constant `true` returns in protected authentication methods are not their real behavior. The observed failed signup and startup protection prove runtime behavior differs from these disk stubs.

## Configuration decoder trace

`Baccarat.Global..cctor` is token `0x0600017A`, RVA `0x35A4`. It calls string decoder `NASP55YLMI`, token `0x06000225`, with these integer inputs:

| Field | Decoder input |
|---|---:|
| `url` | 18936 |
| `ws_url` | 19002 |
| `bucket_url` | 19078 |
| `api_key` | 19200 |
| `store_id` | 19252 |
| `service_id` | 19304 |
| `token_prefix` | 19356 |
| `platform` | 19376 |
| `company` | 19416 |
| `version` | 19438 |
| `product` | 19448 |
| `exe` | 19490 |

`Baccarat.WS_Authentication..cctor` additionally calls the same decoder with `21386` for one protected static string.

The decoder is 14,780 bytes with 3,519 parsed IL instructions, flattened control flow, byte-array construction, XOR operations and an encrypted resource dependency. A direct static call is unsafe because executing it would first activate the protector. The decoder inputs and assignments are confirmed; their plaintext outputs remain unknown.

## Embedded resources

Version 2 contains five CLR resources:

| File | Size | SHA-256 | Entropy |
|---|---:|---|---:|
| `000.bin` | 3,144 | `15e76ec226edb888ef46122308beb1928d188d565d2855ca00ab84ae0f57bc26` | 7.9329 |
| `001.bin` | 132,991 | `5fca3b0b6bfc364323a1c2168e51e318643678fb30a5dba963db8e64c3df2f32` | 7.9988 |
| `002.bin` | 256 | `8b204ae6cca63f0ce03efab980eda794efb52fc6a9cd75b31431071b3938fab6` | 7.1254 |
| `003.bin` | 314,760 | `df177d6cdd5ec6308b1b35aa0fb87d8f6ead372f804aa94cf3f0356aad5fc547` | 7.9995 |
| `004.bin` | 28,556 | `036ec736eb97def2d58ade95ded66a9dd228535e7b80eaa1d05f136c2f8918db` | 7.9926 |

None has a standard PE, ZIP, gzip, PNG, JSON, XML or OLE signature. None decompresses directly as zlib, gzip, bzip2 or LZMA/XZ. Four have near-random entropy and all have low printable-text ratios. They are best classified as encrypted/protected opaque blobs. There is no static evidence that they contain ordinary embedded dependency assemblies.

The classifier is `tools/classify_resources.py`; ignored results are stored in `artifacts/phase2-resource-classification.json`.

## Recoverable workflow state machines

Async type names survive even though their `MoveNext` implementations are protected:

| Type token | State machine |
|---:|---|
| `0x02000013` | `<InitialBet>d__93` |
| `0x02000015` | `<AutoBet>d__94` |
| `0x02000017` | `<PlaceBet>d__95` |
| `0x02000018` | `<DummyBet>d__96` |
| `0x02000025` | `<UpdateSessionInfo>d__131` |
| `0x02000027` | `<SendBalance>d__133` |

Additional readable method names in metadata include file/session operations and browser control described in the consolidated application report. Names establish feature presence, not exact algorithms.

## Confidence and remaining evidence needs

| Result | Confidence |
|---|---|
| Forms, field types, control counts and method tokens | Confirmed from metadata |
| Startup token branch to Main versus SignIn | Confirmed from surviving IL |
| Configuration fields, decoder token and integer inputs | Confirmed from surviving IL |
| Resources are not recognized standard containers/compression | Confirmed by bounded classifiers |
| Resources are encrypted/protector data | High-confidence classification, not a decoded format specification |
| Exact UI layout, labels and event-to-handler mapping | Not recoverable from static disk bodies |
| Plaintext endpoints, headers, request bodies and response schemas | Not recovered |
| Exact strategy and betting algorithms | Not recovered from protected bodies |

Further recovery requires controlled dynamic observation in the user's VMware environment: runtime memory inspection after protection initialization, screenshots of the main form, and sanitized network capture. Static analysis on the host has reached the honest boundary of the protected image.

