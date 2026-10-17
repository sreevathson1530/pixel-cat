# Pixel 🐾 — a desktop AI cat

An original black pixel-art cat that lives on your Windows desktop, reacts to what you do, and answers questions
about anything on your screen using a **local** AI model that can see it. Nothing leaves your PC except the
one-time brain download and web searches you approve.

- Ask about whatever is on screen — an error, a web page, a photo, your code — and get an answer in a second or two
- Reminders, a Pomodoro timer, notes, opening apps and links (always with a **Do it?** card first)
- It wanders around, types on a tiny laptop when you type, chases a fast mouse, meows when you pet it, and bounces
  off the sides of the screen when you throw it

---

## Install it on your laptop — step by step

### Before you start

| You need | Notes |
|---|---|
| Windows 10 (version 2004 or newer) or Windows 11, **64-bit** | Check in *Settings → System → About*. Mac and Linux aren't supported. |
| About **2 GB of free RAM** while Pixel thinks | Close a few browser tabs if your PC is short on memory. |
| About **2 GB of free disk space** | 0.35 GB for the app + 1.4 GB for its brain. |
| An internet connection — **once** | To download the app and its brain. After that Pixel works offline. |
| The **WebView2 runtime** (for the chat panel) | Already built into Windows 11 and installed with Microsoft Edge on Windows 10. If the chat panel stays blank, get it [from Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/). |

### Step 1 — Download Pixel

Go to the [**latest release**](https://github.com/sreevathson1530/pixel-cat/releases/latest) and click
**`Pixel-<version>-win-x64.zip`** under *Assets* to download it (about 130 MB).

### Step 2 — Unzip it to a permanent folder

1. Open your *Downloads* folder, **right-click** the zip and choose **Extract All…**.
2. Pick a folder you'll keep, for example `C:\Users\<you>\Pixel`, and click **Extract**. Avoid folders OneDrive
   syncs (often *Documents* and *Desktop*) — it would upload the whole app to the cloud.

Don't run Pixel from inside the zip, and don't extract it to *Downloads* if you clean that folder — Pixel starts with
Windows from wherever you put it.

### Step 3 — Start Pixel

1. Open the folder you extracted and double-click **`Pixel.exe`**.
2. Windows may show **"Windows protected your PC"** — Pixel isn't signed with a paid certificate. Click
   **More info → Run anyway**. (Some antivirus apps ask about `llama-server.exe` too: that's the open-source
   [llama.cpp](https://github.com/ggml-org/llama.cpp) engine Pixel thinks with. Allow it.)
3. A small black cat appears on your screen and starts wandering around, and a cat-face icon appears in the
   system tray (bottom-right, next to the clock — click **^** if it's hidden).

From now on Pixel starts by itself when you sign in to Windows.

### Step 4 — Open the chat

Press **`Ctrl + Win + Space`** (or click the tray icon → **Chat**). A chat panel opens beside the cat.
Press the same keys again to close it.

### Step 5 — Give Pixel its brain (first time only)

1. Type anything in the chat, for example **`hello`**, and press **Enter**.
2. Pixel answers with a card: **"I don't have a brain yet. Download one?"** Click **Do it**.
3. It downloads **Qwen3-VL-2B** (1.4 GB) from Hugging Face into `%LOCALAPPDATA%\Pixel\models` — you'll see
   *downloading my brain… 42%* at the top of the panel. On a typical connection this takes 2–5 minutes.
4. When it finishes, Pixel loads the brain and answers your message.

If the connection drops, Pixel reconnects and carries on by itself. If it stops completely (Wi-Fi off, laptop
asleep), just ask again — it picks up where it left off.
Every file is checked against a known fingerprint before it's used, so a damaged download is never loaded.

### Step 6 — Ask about your screen

1. Open the page, error, photo or code you want help with.
2. Press **`Ctrl + Win + Space`** and type your question — for example *"what does this error mean?"*,
   *"summarise this page"* or *"what's in this picture?"*.
3. Make sure the **👁 eye** next to the box is switched on (it is by default) — that's what lets Pixel see the screen.

Pixel starts reading your screen the moment the chat opens, while you type, so the answer usually starts within
a second of pressing Enter. Give it about half a minute after Windows starts before your first question while the
brain loads in the background.

### Step 7 — Make it yours (optional)

Right-click the tray icon → **Settings…** to change the fur colour, size, whether it walks anywhere or only along the
bottom of the screen, sound volume, the water/stretch reminders, which brain it uses, and **Start with Windows**.

If you use **Claude Code**, tray → **Connect Claude Code…** lets the cat react while Claude works (laptop out),
finishes (celebration) or needs you (waves). Pixel shows you exactly what it will add to Claude Code's settings and
keeps a backup before changing anything.

---

## Everyday use

| Do this | What happens |
|---|---|
| `Ctrl + Win + Space` | Open / close the chat panel beside the cat. Pixel reads your screen as it opens. |
| `Ctrl + Right Shift + Delete` | Pixel vanishes in a poof (chat saved, screen watching off, brain unloaded). Press again: it pops back, waves hi, and starts a fresh chat. Use the **right** Shift key. |
| Drag the cat | It dangles. Let go gently and it stays put; **fling it** and it flies, bonks off the sides of the screen, bounces on the floor and sits there dizzy for a second. Either way it stays where it ends up until you **double-click** it. |
| Move the mouse back and forth over its head | A happy "meowww" with hearts. 💕 |
| Click the cat while it shows **!** | Asks Pixel about the error it spotted on your screen. |
| Tray icon (the cat's face) | Hide/Show Pixel, Chat, Stay put, History, Notes, Settings, Connect Claude Code, Start with Windows, Quit Pixel. |

Ask in plain words: *"what's in this picture?"*, *"what does this error mean?"*, *"summarise this page"*,
*"remind me in 20 minutes to stand up"*, *"start a pomodoro"*, *"note that the API key rotates on Friday"*,
*"open notepad"*, *"search the web for wpf layered windows"*.
Anything that copies, opens, searches or reminds you later shows a **Do it?** card first. Switch the 👁 eye off to
ask something without sharing your screen.

## The cat notices

- Typing → pulls a laptop out of its pocket and types; typing very fast → overheats (red + steam)
- Fast mouse nearby → crouches and pounces
- Errors in the active window (tracebacks, `npm ERR!`, compiler errors…) → runs over with a **!**
- Claude Code working / done / waiting for you → laptop, celebration, wave (tray → *Connect Claude Code*)
- Cursor, VS Code, Codex busy (best effort, by CPU activity)
- Full-screen video or game → hides in the corner and peeks
- Water every 60 min, stretch every 45 min (only while you're at the PC); Pomodoro with a countdown badge

## Troubleshooting

| Problem | Fix |
|---|---|
| "Windows protected your PC" | Click **More info → Run anyway**. |
| I can't see the cat | It may be hidden: press `Ctrl + Right Shift + Delete`, or tray icon → **Show Pixel**. No tray icon? Start `Pixel.exe` again. |
| The chat panel is blank or doesn't open | Install the [WebView2 runtime](https://developer.microsoft.com/microsoft-edge/webview2/), then quit and restart Pixel. |
| `Ctrl + Win + Space` does nothing | Another app may use that shortcut — open the chat from the tray icon instead. |
| The brain download fails | It reconnects by itself when the connection drops; if it still stops, ask again and it resumes. Some office, school or college networks block or cut big downloads from Hugging Face — try another Wi-Fi or a phone hotspot. |
| "Only X GB of RAM is free…" or very slow answers | Close some apps or browser tabs and ask again. |
| It answers but doesn't know what's on screen | Switch the **👁 eye** on (next to the chat box). |
| It answers something odd | The default brain is small to be fast. For harder questions pick a bigger brain in Settings (see below). |
| Something else | Logs are in `%APPDATA%\Pixel\pixel.log` and `llama-server.log` — they never contain your screen. |

## Update to a new version

1. Right-click the tray icon → **Quit Pixel**.
2. Download the new zip from the [latest release](https://github.com/sreevathson1530/pixel-cat/releases/latest) and
   extract it **over your existing Pixel folder** (choose *Replace the files* if asked).
3. Start `Pixel.exe` again. Your brain (`%LOCALAPPDATA%\Pixel\models`), settings, notes and chat history
   (`%APPDATA%\Pixel`) are kept.

Coming from **1.0.0**, Pixel keeps the slower 4B brain you already have. To switch to the faster 2B brain: quit Pixel,
delete `%APPDATA%\Pixel\settings.json` (this resets your settings) and the two files with `Qwen3VL-4B` in their
names in `Documents\pixel`, then start Pixel, ask anything and click **Do it**.

## Uninstall

1. Tray icon → **Settings…** → untick **Start with Windows** → Save.
2. Tray icon → **Quit Pixel**.
3. Delete the Pixel folder, `%APPDATA%\Pixel` (settings, notes, history) and `%LOCALAPPDATA%\Pixel` (the brain and
   the chat panel's cache) — plus `Documents\pixel` if an older version put a brain there.

---

## Brain (local model)

Pixel runs a [Qwen](https://huggingface.co/Qwen) model on your own PC with [llama.cpp](https://github.com/ggml-org/llama.cpp).
The default, downloaded on first use, is **Qwen3-VL-2B**: it gets a small screenshot with every question (plus the
screen's exact text, read by Windows OCR), so it can describe pictures, photos and layouts as well as read code and
errors — and it's quick: about 80 tokens/s reading and 27 tokens/s writing on a laptop CPU.

Settings → **Brain** lists every `.gguf` model in `%LOCALAPPDATA%\Pixel\models`, `Documents\pixel` and
`%USERPROFILE%\pixel-models`, labelled
*sees pictures* or *reads text*. A model sees pictures when its vision projector (`mmproj-….gguf`) sits next to it.
Bigger models are smarter but slower: Qwen3-VL-4B (also sees pictures) takes about twice as long, and the
text-only Qwen2.5-Coder 7B/14B are stronger at code but need 5–9 GB of free RAM.

The brain loads in the background when Pixel starts and stays loaded (set `idleUnloadMinutes` in
`%APPDATA%\Pixel\settings.json` to unload it after that many idle minutes instead). If memory is short Pixel says so
instead of hanging; while it reads a long screen you'll see *reading… 42%*, and the send button turns into **Stop**.

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
