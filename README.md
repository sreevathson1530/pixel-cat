# Pixel 🐾 — a desktop AI cat

An original black pixel-art cat that lives on your Windows desktop, reacts to what you do, and answers questions
about anything on your screen using a **local** Qwen2.5-Coder model. Nothing leaves your PC except web searches
you approve.

## Use it

| Do this | What happens |
|---|---|
| `Ctrl + Win + Space` | Open / close the chat panel beside the cat. Pixel reads your screen as it opens. |
| `Ctrl + Right Shift + Delete` | Pixel vanishes in a poof (chat saved, screen watching off, brain unloaded). Press again: back with a fresh chat. |
| Drag the cat | It dangles; drop it and it stays put until you **double-click** it. |
| Move the mouse back and forth over its head | Purring. 💕 |
| Click the cat while it shows **!** | Asks Pixel about the error it spotted on your screen. |
| Tray icon (the cat's face) | Chat, Stay put, History, Notes, Settings, Connect Claude Code, Start with Windows, Quit. |

Ask in plain words: *"what does this error mean?"*, *"remind me in 20 minutes to stand up"*, *"start a pomodoro"*,
*"note that the API key rotates on Friday"*, *"open VS Code"*, *"search the web for wpf layered windows"*.
Anything that copies, opens, searches or reminds you later shows a **Do it?** card first.

## The cat notices

- Typing → kneads a keyboard; typing very fast → overheats (red + steam)
- Scrolling → unrolls paper; fast mouse nearby → crouches and pounces
- Errors in the active window (tracebacks, `npm ERR!`, compiler errors…) → runs over with a **!**
- Claude Code working / done / waiting for you → laptop, celebration, wave (tray → *Connect Claude Code*)
- Cursor, VS Code, Codex busy (best effort, by CPU activity)
- Full-screen video or game → hides in the corner and peeks
- Water every 60 min, stretch every 45 min (only while you're at the PC); Pomodoro with a countdown badge

## Brain (local model)

Settings → **Brain** picks the model file and engine:

- `qwen2.5-coder-7b-instruct-q4_k_m.gguf` (default, `C:\Users\sreev\pixel-models`) — ~3× faster, needs ~5 GB free RAM.
- `qwen2.5-coder-14b-instruct-q4_k_m.gguf` (`Documents\pixel`) — smarter, needs ~9 GB free RAM.

The model loads when you ask something and unloads after 10 idle minutes. If memory is short Pixel says so instead
of hanging; while it reads a long screen you'll see *reading… 42%*, and the send button turns into **Stop**.
The model only sees screen **text** (Windows OCR), not images.

## Privacy

Screenshots and screen text stay in memory only and are never written to disk or logs. Key presses are counted,
never recorded. Chat history (`%APPDATA%\Pixel\pixel.db`) holds only what you typed and Pixel's answers — browse
or delete it from the tray. The model server listens on a random loopback port with a per-launch key.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install-dotnet.ps1   # .NET 10 SDK, per-user
powershell -ExecutionPolicy Bypass -File scripts\setup-llama.ps1      # llama.cpp engines into runtime\
scripts/dn test tests/Pixel.Core.Tests                                 # unit tests
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1          # self-contained build in dist\Pixel
```

Logs: `%APPDATA%\Pixel\pixel.log` and `llama-server.log`. Settings: `%APPDATA%\Pixel\settings.json`.
