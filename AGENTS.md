# AGENTS.md

Rules for coding agents (and humans) working on Apex Triggers. `CLAUDE.md` points here; keep all rules in this file.

## What this is

A Windows tray utility for the Flydigi Apex 5 controller. It hands the pad to Steam Input (so Steam sees a
native Apex 5 with gyro and paddles) and, in parallel with Steam, sets the adaptive trigger effects per game
over the pad's vendor HID interface. Space Station is not needed.

## Layout

| Path | What |
| --- | --- |
| `src/ApexTriggers.Core` | No UI. HID transport (`Hid/`), wire protocol (`Protocol/`), `PadService` (connect, handover, send), `TriggerController` (which preset is in force), Steam library reader, game monitor, `config.json` |
| `src/ApexTriggers.App` | WinForms tray app: `TrayApp`, `MainForm`, `TriggerPanel`, `AddGameForm`, `SettingsForm`, custom-painted controls in `Controls/` |
| `docs/screenshots` | README images |

Not in the repository (local only, git-ignored): `artifacts/` (spec and UI mockups) and `src/ApexTriggers.Tester`
(the stage-0 hardware tester). Don't add them to the solution or to commits.

## Build and run

```powershell
dotnet build ApexTriggers.slnx
dotnet run --project src/ApexTriggers.App
```

Target: `net10.0-windows`. If `dotnet` has no SDK on PATH, a user-local SDK may live in `%USERPROFILE%\.dotnet`
(set `DOTNET_ROOT` and `PATH` to it). Config lives in `%APPDATA%\ApexTriggers\config.json`, logs in
`%APPDATA%\ApexTriggers\logs\packets.log`. Only one instance runs; a second launch shows the first one's window.

## Protocol rules — these protect real hardware

- **Command whitelist.** Only commands in `Packet.AllowedCommands` (1, 16, 17, 18, 81, 82) can be built — the
  `Packet` constructor throws otherwise. Never add flash writes (166, 171), chip upgrade mode (31), factory reset
  (253) or anything else that persists or bricks. A new command needs a stated reason and must be harmless.
- **One packet per trigger.** Side 3 ("both") is ACKed and ignored by the pad.
- **All 82s before any 81** when setting both triggers (`ApexPad.SetTriggersAsync`). Measured: an 82 on one
  side reset the other side's 81 effect when sent after it.
- **ACK success byte is raw `[6]`**: `04 5A A5 <cmd> 01 00 <success> <echo…>`. Command 18 has no success byte.
  An ACK only means "parsed"; whether an effect works is checked by feel.
- **The handover flag (command 17) follows the user's choice.** Set it on connect when handover is enabled; change
  it otherwise only because the user asked for it (a button, a setting), never as a side effect.
- **Don't stream effects.** Each 81/82 re-seats the trigger motors. UI changes are debounced (sent once the
  slider is released and idle), sends are latest-wins, identical effects are not resent.
- **Share the device.** Open HID with `FILE_SHARE_READ | FILE_SHARE_WRITE`; never send Steam's acquire command
  (`0x1C`). Replies are broadcast to every reader, so match by command id and drain stale input first.
- **Don't hold the handle idle.** An open handle receives a ~1000 Hz input stream; `PadService` closes it after a
  few idle seconds.
- **Game detection only reads the process list** (`PROCESS_QUERY_LIMITED_INFORMATION`). Never read game memory,
  inject, or open processes with more rights — anti-cheat.
- Trigger mode names: modes 2 and 3 are swapped between the SDK and Space Station's UI. Code uses the UI names
  (`Recoil` = wire mode 2, `Sniper` = wire mode 3); wire numbers live only in `TriggerEffect`.

Protocol reference: [OpenFlydigi PROTOCOL.md](https://github.com/mkaliaha/openflydigi/blob/main/PROTOCOL.md),
[SDL_hidapi_flydigi.c](https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_flydigi.c).

## UI rules

- Forms are built in code; no designer files. `WFO1000` is suppressed for that reason.
- The app runs in WinForms dark color mode, which repaints stock `Button` and `CheckBox` its own way. Use
  `FlatButton` and `DarkCheck`, not the stock controls.
- Colors and fonts come from `Theme` (taken from the mockups). When painting, clear with `Theme.Back(this)`,
  not `Parent.BackColor` — a transparent parent paints black.
- Strings live in `src/ApexTriggers.App/Resources/Strings.resx` (English) and `Strings.ru.resx` (Russian); the
  `Strings` class is generated at build time. Every key exists in both files; no user-visible text in code.
  Core stays language-free (mode and parameter names are App resources).
- Everything with a click handler must be focusable and work from the keyboard, with an accessible name.

## Code style

- Match the surrounding code: naming, idioms, comment density.
- Comments explain *why* (a measured pad behavior, a Windows quirk), not what the next line does.
- Nullable is on; keep the build at zero warnings.
- Hardware-facing changes: say in the PR how it was tested on a real pad (connection type, firmware).

## Commits

Short imperative subject, body explains why. Don't commit `artifacts/`, the tester, build output or local config.
