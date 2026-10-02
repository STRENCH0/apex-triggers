# Contributing to Apex Triggers

Thanks for helping out! Bug reports, hardware test results, translations and code are all welcome.

## Reporting a bug

Please include:

- What you did, what you expected, what happened.
- Controller model and **main firmware** (*Settings → Controller*), and the connection: **dongle** or **cable**.
- Whether Steam was running and the pad was handed to Steam Input.
- The packet log: `%APPDATA%\ApexTriggers\logs\packets.log` (it holds only packets and app events — no personal data).
- For "the trigger doesn't feel right": the mode and slider values, and which trigger.

Hardware results are especially useful — "Sniper works on firmware 7.0.4.5 over the dongle" is a great issue.

## Development setup

- Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), any editor
  (Visual Studio, Rider or VS Code with C# Dev Kit).
- ```powershell
  dotnet build ApexTriggers.slnx
  dotnet run --project src/ApexTriggers.App
  ```
- A real Apex 5 is needed for anything that touches the pad. Quit Space Station and stop its service while testing.

The project layout, protocol rules and UI conventions are in **[AGENTS.md](AGENTS.md)** — read it before your
first change. It is written for coding agents and humans alike, and the rules there are the review checklist.

## Ground rules for code that talks to the pad

These protect people's controllers; PRs that break them won't be merged.

1. Only whitelisted commands (`Packet.AllowedCommands`). No flash writes, no firmware/upgrade commands, no
   factory reset — ever. Adding a command needs a reason and evidence that it's harmless.
2. Never toggle the Steam handover flag automatically.
3. Don't stream effects: debounce UI input, keep sends latest-wins, don't resend identical effects.
4. Open the device shared and never send Steam's acquire command.
5. Read the process list only; never open game processes with more than limited query rights.

## Pull requests

- One topic per PR; keep the build at **zero warnings**.
- Match the surrounding code; comments explain *why*.
- Describe how you tested. For hardware-facing changes: firmware, connection, and what you felt on the triggers.
- UI changes: add a before/after screenshot.
- Don't commit build output, local config or logs.

## Translations

UI strings live in `src/ApexTriggers.App/Resources/Strings.resx` (English) and `Strings.ru.resx` (Russian).
To add a language, add `Strings.<culture>.resx` with every key translated, and add it to the language switch in
Settings and the tray menu.

## License

By contributing you agree that your contributions are licensed under the [MIT License](LICENSE).
