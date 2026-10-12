# Static-analysis evidence and limits

Input SHA-256: DBB6F05548E0B3388EABFEEB5F356BD65361126726E86AB67A714B0FC3FD3990.

Confirmed from initial metadata: .NET Framework 4.7.2 target, WinForms references, Selenium/WebDriver, WindowsInput, HTTP and WebSocket dependencies, 322 TypeDef rows, and five ManifestResource rows. PE machine is I386; this alone does not establish whether managed execution requires a 32-bit process. Check CLR flags before deciding architecture requirements.

The initial parser emitted `stream is too small: wanted: 0xffff found: 0x3570`. Treat malformed/protected metadata carefully. High entropy and obfuscated names are evidence of protection or compression, not proof of malware or a specific protector.

Several on-disk method bodies contain only return stubs. Runtime restoration is a hypothesis supported by reflection/dynamic-method references, not a verified execution trace. Static stubs do not establish actual authentication behavior.

Global's static initializer loads numeric offsets into an obfuscated routine before assigning url, ws_url and other configuration fields. Their values and the service contracts have not been recovered.

Names including Withdraw, SendTip and GetCoinAddresses are present. Names alone do not establish what those methods actually do.

Corrections to the earlier chat: embedded dependencies have not yet been demonstrated; no failed server response has been observed; the root cause of the sign-in exception is unknown. The exception proves only that the sign-in path accessed a null reference. A reimplementation is the intended engineering approach, but compatibility layers or a Windows VM have not been tested and cannot be ruled out solely from static metadata.

This project is a behavior-based reconstruction effort. It should not be described as a formally isolated clean-room process because implementation and reverse-engineering work are being done together.

## Resource extraction milestone

The bounded extractor successfully read all five resources. CLR flags are 0x20003, including ILONLY and 32BITREQUIRED: this executable requires a 32-bit CLR process. Resource lengths are 119462, 3072, 256, 314762 and 30094 bytes. The manifest at artifacts/resources/manifest.json records individual SHA-256 hashes and prefixes. None begins with a plain PE `MZ` header; their actual encodings remain unresolved. The original executable was not launched or modified.
