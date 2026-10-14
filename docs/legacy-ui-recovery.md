# Legacy UI recovery

## Evidence boundary

The supplied version 2 executable was not executed. Static metadata confirms three Windows Forms (`Main`, `SignIn`, and `SignUp`) and their control-type counts, but the protected constructors contain runtime-restored stubs. Captions, coordinates, fonts, colors, event wiring, and the Main form's exact grouping are therefore not recoverable from ordinary IL.

The supplied SignUp screenshot is the authoritative visual reference currently available. It establishes an approximately 834 × 683 form, the navy window and inset panel, centered WETALKBET header, square blue inputs, gold uppercase labels, five registration fields, password visibility control, bright-green primary action, and blue outlined secondary action. The replacement Account workspace now follows that evidence while preserving native Avalonia controls and the existing validation/session boundary.

The public introduction-video channel could not be reached from the analysis environment. No video was downloaded, and no unseen Main layout was guessed.

## Implemented

- Shared legacy palette and square control styling.
- Cross-platform WETALKBET header asset derived from the user-supplied screenshot.
- Evidence-matched registration layout: Name, Email, Password, Phone No, and Country.
- Show Password, SIGN UP, SIGN IN, authentication feedback, and authenticated SIGN OUT behavior.
- Original product title and branded application header.
- Uppercase workspace navigation with the recovered navy/gold visual language.
- Updated headless checks for the recovered account actions.

## Verification

- Debug solution build: zero warnings and zero errors.
- Isolated Release build for the modern target: zero warnings and zero errors.
- Isolated Release build for the macOS 11 compatibility target: zero warnings and zero errors.
- `dotnet format --verify-no-changes`: passed.
- Core behavioral checks: passed.
- Headless Avalonia startup, navigation, strategy, automation, Viewer, and Account checks: passed.

## Evidence required for exact Main-form parity

Provide clear screenshots or short screen recordings of the original Main form at its normal size, preferably showing every tab or state. Each capture should include the whole application window and should avoid real credentials, balances, account identifiers, or betting history. Useful states are initial load, strategy selection, connected/disconnected status, initial bet, auto bet, session details, and the viewer. These images can be supplied from the isolated VMware machine; the original executable still does not need to run on the development computer.
