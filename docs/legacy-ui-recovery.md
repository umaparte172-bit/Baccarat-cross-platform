# Legacy UI recovery

## Evidence boundary

The supplied version 2 executable was not executed. Static metadata confirms three Windows Forms (`Main`, `SignIn`, and `SignUp`) and their control-type counts, but the protected constructors contain runtime-restored stubs. Captions, coordinates, fonts, colors, event wiring, and the Main form's exact grouping are therefore not recoverable from ordinary IL.

The supplied SignIn and SignUp screenshots are the visual references currently available. SignIn establishes two fields, Remember Me and Show Password checkboxes, a green SIGN IN button, and an outlined SIGN UP button. SignUp establishes five fields, a password visibility checkbox, a green SIGN UP button, and an outlined SIGN IN button. Both share the navy window and inset panel, centered WETALKBET header, square blue inputs, and gold uppercase labels. The Account workspace now provides distinct layouts with working navigation between them while retaining the existing validation/session boundary.

The public introduction-video channel could not be reached from the analysis environment. No video was downloaded, and no unseen Main layout was guessed.

## Implemented

- Shared legacy palette and square control styling.
- Cross-platform WETALKBET header asset derived from the user-supplied screenshot.
- Evidence-matched SignIn and SignUp layouts, including their different field counts and panel heights.
- SignIn ↔ SignUp navigation, password visibility, authentication feedback, and authenticated SIGN OUT behavior.
- The Remember Me checkbox is shown as in the original screenshot; persistent sign-in behavior remains dependent on a verified authentication service.
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

The WETALKBET header asset is a visual reconstruction from the supplied screenshot, not the original embedded bitmap. The authentication layouts appear inside the replacement's Account workspace; reproducing the original top-level window chrome and exact appearance still requires visual comparison on the target machines.

## Evidence required for exact Main-form parity

Provide clear screenshots or short screen recordings of the original Main form at its normal size, preferably showing every tab or state. Each capture should include the whole application window and should avoid real credentials, balances, account identifiers, or betting history. Useful states are initial load, strategy selection, connected/disconnected status, initial bet, auto bet, session details, and the viewer. These images can be supplied from the isolated VMware machine; the original executable still does not need to run on the development computer.
