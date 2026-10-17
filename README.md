# Pixel 🐾 — a desktop AI cat

An original black pixel-art cat that lives on your Windows desktop, reacts to what you do, and answers questions
about anything on your screen using a **local** AI model that can see it. Nothing leaves your PC except the
one-time brain download and web searches you approve.

## Get started

1. **Download** `Pixel-…-win-x64.zip` from the [latest release](https://github.com/sreevathson1530/pixel-cat/releases/latest).
2. **Unzip** it somewhere permanent (for example `Documents\Pixel`) and double-click **`Pixel.exe`**.
   Windows may say *"Windows protected your PC"* because the app isn't code-signed — click **More info → Run anyway**.
3. The cat appears and starts wandering. Press **`Ctrl + Win + Space`** and ask it anything.
4. The first time, Pixel offers to **download its brain** (Qwen3-VL-4B, 2.7 GB from Hugging Face, saved in
   `Documents\pixel`). Say yes, wait a few minutes, and it answers. If the download is interrupted, ask again and it
   picks up where it left off.

**You need:** Windows 10 (version 2004 or later) or Windows 11, 64-bit · about 4 GB of free RAM while it thinks ·
about 3.5 GB of disk space · the WebView2 runtime (built into Windows 11; on Windows 10 it comes with Microsoft Edge,
or get it [from Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/)).

The first answer after waking up takes up to a minute on a typical laptop; follow-up questions are much faster.
Pixel starts with Windows — turn that off from the tray icon. To remove it: tray → Quit, then delete the Pixel
folder, `%APPDATA%\Pixel` and `Documents\pixel`.

## Use it

| Do this | What happens |
|---|---|
| `Ctrl + Win + Space` | Open / close the chat panel beside the cat. Pixel reads your screen as it opens. |
| `Ctrl + Right Shift + Delete` | Pixel vanishes in a poof (chat saved, screen watching off, brain unloaded). Press again: it pops back, waves hi, and starts a fresh chat. |
| Drag the cat | It dangles. Let go gently and it stays put; **fling it** and it flies, bonks off the sides of the screen, bounces on the floor and sits there dizzy for a second. Either way it stays where it ends up until you **double-click** it. |
| Move the mouse back and forth over its head | A happy "meowww" with hearts. 💕 |
| Click the cat while it shows **!** | Asks Pixel about the error it spotted on your screen. |
| Tray icon (the cat's face) | Chat, Stay put, History, Notes, Settings, Connect Claude Code, Start with Windows, Quit. |

Ask in plain words: *"what's in this picture?"*, *"what does this error mean?"*, *"summarise this page"*,
*"remind me in 20 minutes to stand up"*, *"start a pomodoro"*, *"note that the API key rotates on Friday"*,
*"open VS Code"*, *"search the web for wpf layered windows"*.
Anything that copies, opens, searches or reminds you later shows a **Do it?** card first.

## The cat notices

- Typing → pulls a laptop out of its pocket and types; typing very fast → overheats (red + steam)
- Fast mouse nearby → crouches and pounces
- Errors in the active window (tracebacks, `npm ERR!`, compiler errors…) → runs over with a **!**
- Claude Code working / done / waiting for you → laptop, celebration, wave (tray → *Connect Claude Code*)
- Cursor, VS Code, Codex busy (best effort, by CPU activity)
- Full-screen video or game → hides in the corner and peeks
- Water every 60 min, stretch every 45 min (only while you're at the PC); Pomodoro with a countdown badge

## Brain (local model)

Pixel runs a [Qwen](https://huggingface.co/Qwen) model on your own PC with [llama.cpp](https://github.com/ggml-org/llama.cpp).
The default, downloaded on first use, is **Qwen3-VL-4B**: it gets a screenshot with every question (plus the
screen's text, read by Windows OCR), so it can describe pictures, photos and layouts as well as read code and errors.

Settings → **Brain** lists every `.gguf` model in `Documents\pixel` and `%USERPROFILE%\pixel-models`, labelled
*sees pictures* or *reads text*. A model sees pictures when its vision projector (`mmproj-….gguf`) sits next to it.
Bigger text-only models such as Qwen2.5-Coder 7B/14B are stronger at code but need 5–9 GB of free RAM.

The model loads when you ask something and unloads after 10 idle minutes. If memory is short Pixel says so instead
of hanging; while it reads a long screen you'll see *reading… 42%*, and the send button turns into **Stop**.
Each question takes a fresh look at the screen (Pixel's own windows are invisible to it). Switch the 👁 eye off
in the chat to ask without the screen.

## Privacy

Screenshots and screen text stay in memory only and are never written to disk or logs. Key presses are counted,
never recorded. Chat history (`%APPDATA%\Pixel\pixel.db`) holds only what you typed and Pixel's answers — browse
or delete it from the tray. The model server listens on a random loopback port with a per-launch key. The brain
download comes from one pinned Hugging Face revision and is checked against known SHA-256 fingerprints before use.

## Build from source

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install-dotnet.ps1   # .NET 10 SDK, per-user
powershell -ExecutionPolicy Bypass -File scripts\setup-llama.ps1      # llama.cpp engines into runtime\
scripts/dn test tests/Pixel.Core.Tests                                 # unit tests (from Git Bash)
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1          # self-contained build in dist\Pixel
```

Logs: `%APPDATA%\Pixel\pixel.log` and `llama-server.log`. Settings: `%APPDATA%\Pixel\settings.json`.

## License

Pixel is [MIT-licensed](LICENSE). It bundles third-party software under its own licenses — see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The Qwen model is Apache-2.0 and is downloaded from Hugging Face,
not shipped here.
