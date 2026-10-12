# Original application analysis

Analysis date: 2026-10-13. This report is static: no supplied executable, batch file, installer or updater was launched. Text strategy files were treated as data. Generated inventories remain under the ignored `artifacts/` directory.

## Evidence set

The supplied material contains 49 files totaling 54,540,580 bytes: six EXEs, ten DLLs, seven XML documentation files, three batch files, three configuration files, seventeen strategy text files, two ChromeDrivers, one empty bet-history CSV, one ChromeDriver license and the Inno Setup uninstall data.

| Component | Version/hash | Signature |
|---|---|---|
| Setup `Baccarat(Evolution).exe` | Product 2.0; SHA-256 `5DF5A1AC8AEEAAAD4D683ABA9D0CF90050CF347615842D349BCFCCD357EA7824` | Not signed |
| Installed `Baccarat (Evolution).exe` | 2.0.0.0; SHA-256 `B051ADE2E179154614BD566ADC35C5C3672A04F899B891FEBAFBF0CD4045ECB1` | Not signed |
| `Updater.exe` | 1.0.0.0; SHA-256 `B922D8740243F80A7E66C3D1116203C6D4765863A4E488A6BAF5CCCC7203278F` | Not signed |
| Uninstaller | Inno Setup; SHA-256 `9CDA256A755F970AD4BE914B5A6AD24E882EE174FB5411EDB56DA3BF543E194A` | Not signed |
| Root ChromeDriver | likely 89.0.4389.23 from embedded version strings; SHA-256 `37C78987CD4BD112F15A22E22CF42ECF315DC4179B4AC8332E3B0A9D167A4D91` | Not signed |
| Drivers ChromeDriver | likely 117.0.5938.92 from embedded version strings; SHA-256 `0111588C7828F874B2E20AC0C835860BD7809AF83F55B84704CF7386F07D278E` | Not signed |

Microsoft framework DLLs and Newtonsoft.Json have valid Authenticode signatures. Selenium, WindowsInput, Random.Org, WebSocket4Net and SuperSocket binaries are unsigned. Exact-hash web searches returned no public results for the setup, version 2 application, updater or newer ChromeDriver. Absence of a search result is not a clean verdict.

The setup and client strategy downloads have Internet-zone markers. The markers do not retain a source URL. The supplied per-user settings contain empty `UserName` and `Password` values, so this copy exposes no saved credential. The original application directory is excluded from Git to avoid publishing binaries or client data.

## Installation behavior

The setup is an Inno Setup executable and installs under `C:\Program Files (x86)\Wetalkbet\Baccarat(Evolution)`. Uninstall metadata accounts for the installed binaries, dependencies, strategies, batch files, data directory, desktop shortcut, a standard uninstall key, and `Software\Wetalkbet\Baccarat(Evolution)`. No service or scheduled-task artifact was found in the supplied installed-state data. The text `Startup:` appears in generic installer/uninstaller data, but it is not sufficient evidence of an autorun entry.

The application is .NET Framework 4.7.2 and declares CLR flags `ILONLY | 32BITREQUIRED`. Version 2 retains the earlier protected/obfuscated architecture: malformed/protected metadata warnings, encrypted strings/resources, dynamic-method/reflection machinery and native `LoadLibrary`/`GetProcAddress` calls. Static method bodies include placeholders, so protected main-application behavior cannot be completely reconstructed from ordinary IL.

## Network and server integration

Server integration is certain. Startup metadata shows separate HTTP and WebSocket authentication clients. The application has sign-in, sign-up, sign-out, saved-token checks, cookie validation, service validation, WebSocket send/receive and periodic timer behavior. Its static configuration fields include `url`, `ws_url`, `bucket_url`, `api_key`, `store_id`, `service_id`, `token_prefix`, `platform`, `company`, `version`, `product` and `exe`.

The values are passed through an obfuscated string decoder, so the actual HTTP, WebSocket and update hosts are not present as readable strings. They have not been recovered and must not be guessed. The updater receives its base URL as a command-line argument from the protected main application.

The main application references HttpClient, HttpWebRequest, WebClient, WebSocket4Net, JSON, TLS, cookies and cryptography. It also contains methods named `SendTip`, `Withdraw`, `TwoFactorCheck`, `GetCoinAddresses` and `SendBalance`. These names show intended financial/account workflows, but protected bodies prevent claiming their exact request formats or destinations.

## Updater behavior

`Updater.exe` is unobfuscated enough to reconstruct precisely. It accepts a comma-separated product name, executable filename and base URL. It then:

1. Opens `http://www.google.com` to test connectivity.
2. Downloads `<base-url>/version.txt` and compares it with the installed executable's file version.
3. On approval, kills processes selected by its process filter and then kills processes named after the target executable.
4. Creates a product-specific temporary directory.
5. Downloads `<base-url>/package.zip` with `WebClient.DownloadFile`.
6. Extracts the archive to a temporary `package` directory.
7. Grants the current Windows identity FullControl on the application directory.
8. Recursively copies downloaded files over the installed application.
9. Deletes the temporary extraction directory and ZIP.
10. Updates `HKCU\Software\Wetalkbet\<product>\Version` when that key already exists.
11. Starts the configured application executable.

No signature, certificate pin, package hash or manifest integrity check appears in the recovered updater IL. It enables a legacy `SecurityProtocol` bitmask containing SSL 3.0, TLS 1.0, TLS 1.1 and TLS 1.2. Compromise or misconfiguration of the encrypted update host could therefore turn the legitimate updater into a code-delivery path. This is a supply-chain weakness; it is not proof that the supplied package is currently malicious.

## Browser automation and local control

The application includes Selenium 3.141, WindowsInput and two ChromeDrivers. `StartChrome.bat` launches Chrome with remote-debugging port 9014 and profile `C:\selenium`; `StartChrome2.bat` uses port 9015 and `C:\selenium2`. `KillPort2.bat` finds the PID owning port 9015 and force-kills it. That script can terminate an unrelated process if it owns the port.

The main program can launch normal/headless Chrome, download a replacement ChromeDriver, extract ZIP files, invoke Windows input through `SendInput`, locate elements, manage windows and terminate ChromeDriver processes. Version 2 exposes async state-machine names for `InitialBet`, `AutoBet`, `PlaceBet`, `DummyBet`, `UpdateSessionInfo` and `SendBalance`, supporting the conclusion that it automates betting rather than merely recording outcomes.

The remote-debugging batch files do not specify a remote bind address. Nevertheless, automation profiles contain authenticated browser state and should be treated as sensitive local data.

## Strategies and data

Strategies are declarative JSON documents, not executable scripts. Their schema includes:

- strategy name and optional rule condition;
- initial, win and loss conditions;
- `bet_on`, amount operation and value;
- stop-profit and stop-loss values;
- consecutive-loss stop and pause behavior.

Installed strategies are Grinding, Ladder, Martingale, ODD Diff, Odd Even, ODD is God, ODD Looser, PATTERN and Sequence. The separate client folder omits Ladder. Six files are byte-identical to installed copies. Client Martingale changes the initial amount from 100 to 1. Client Sequence changes both its selection sequence and amount sequence. Treat the client variants as overrides requiring explicit import/selection, not as canonical installed defaults.

The CSV contains only this header and no betting records: `time,session_no,sno,bet_on,bet_amount,winner,win_status,balance`.

## Malware assessment

Static evidence does not justify calling this sample confirmed malware. The files and behaviors are coherent with a commercial betting automation application and its installer/updater. No supplied artifact establishes credential theft, persistence through a service or scheduled task, process injection, ransomware behavior, or a known malicious command-and-control host.

Risk remains high because the core executables are unsigned, the main code and endpoints are deliberately hidden, public hash reputation is absent, Chrome is automated with persistent profiles, credentials/tokens are handled, money-moving method names exist, processes are force-killed, and the updater executes unauthenticated downloaded code without package verification. Microsoft Defender was disabled in the analysis environment, so no local antivirus verdict is available. The safe classification is **unverified, security-sensitive software with a vulnerable update design**, not confirmed clean and not confirmed malware.

## Reimplementation implications

The cross-platform replacement should import the declarative strategy schema, keep browser/session profiles isolated, validate all configuration, and implement server adapters only after endpoints and contracts are verified. It should never reproduce the old updater. Releases should be signed, served over modern TLS, verified against a signed manifest or pinned public key, and installed without granting broad directory permissions. Credentials should use Windows Credential Manager or macOS Keychain rather than ordinary user settings.
