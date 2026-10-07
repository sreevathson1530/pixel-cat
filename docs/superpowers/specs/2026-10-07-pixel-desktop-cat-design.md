# Pixel — Desktop AI Cat · Design Spec

Date: 2026-10-07 · Status: approved in brainstorming, pending written-spec review

## 1. Goal

An always-on desktop companion: an original black pixel-art cat ("Pixel") that lives on the
Windows desktop, reacts to what the user does (Comnyang-style behaviours), and answers typed
questions about anything — including what is currently on screen — using a fully local LLM
(Qwen2.5-Coder-14B-Instruct GGUF). Nothing leaves the PC except web searches the user approves.

### Success criteria
- Cat is visible, animated and always on top; it never blocks clicks outside its own pixels.
- `Ctrl + Right Shift + Delete` vanishes / revives Pixel; each revive starts a new conversation.
- `Ctrl + Win + Space` toggles the chat panel; questions about on-screen text are answered correctly.
- All four behaviour groups work: mouse, keyboard/scroll, productivity helpers, AI-tool reactions.
- Pixel's own process stays under ~150 MB RAM; the brain is loaded only when needed.

### Constraints (measured on the target PC)
- Intel Core Ultra 5 225H, 15.4 GB RAM, Intel Arc iGPU ("Intel(R) Graphics"), Intel AI Boost NPU,
  no NVIDIA GPU. Single display (1280×800 logical). Windows 11 Home 26200.
- Commit memory is frequently exhausted on this PC when many apps are open.
- Model file: `C:\Users\sreev\OneDrive\Documents\pixel\*.gguf` (Q4_K_M, ~8.99 GB).
- The model is **text-only** → screen understanding is via OCR text, not images.
- Installed: .NET 8 Desktop runtime (SDK must be installed), WebView2 runtime, Python 3.11, Node 24.

### Out of scope (v1)
Voice input/output, image understanding (photos/diagrams), multi-monitor-specific behaviour,
cloud models, running terminal commands or editing files, mobile/other OS.

## 2. Architecture

Two processes:

1. **`Pixel.exe`** — .NET 8 WPF app (per-monitor-DPI-aware v2, single instance, starts with
   Windows, system-tray icon). Hosts the cat window, the chat panel (WebView2), settings and
   history windows, and all modules below.
2. **`llama-server.exe`** — llama.cpp server, spawned and killed by Pixel. Bound to
   `127.0.0.1:17860` only, OpenAI-compatible `/v1/chat/completions` with streaming and tool calls
   (`--jinja`). Backend chosen by benchmark on this PC (CPU vs Vulkan vs SYCL); fastest wins and is
   stored in settings.

### Solution layout
```
pixel-cat/
  src/Pixel.Core/        # pure logic, no WPF: state machine, rules, prompt builder, tools, storage
  src/Pixel.App/         # WPF shell: windows, hooks, OCR, capture, tray, WebView2 chat UI
  src/Pixel.App/web/     # chat panel HTML/CSS/JS (marked + highlight.js bundled locally, no CDN)
  assets/sprites/        # generated sprite sheet PNG + frame metadata JSON
  assets/sounds/         # purr / meow / poof (original or CC0)
  tools/SpriteGen/       # program that renders sprite frames from pixel grids → PNG
  tests/Pixel.Core.Tests # xUnit
  runtime/llama/         # llama.cpp binaries (downloaded at setup, git-ignored)
```

### Modules (inside Pixel.exe)
| Module | Responsibility | Depends on |
|---|---|---|
| **Input** | `WH_KEYBOARD_LL` + `WH_MOUSE_LL` hooks. Detects and **swallows** both hotkeys (distinguishes Right Shift via `VK_RSHIFT`; injects a dummy key before Win key-up so the Start menu doesn't open). Emits typing-rate, scroll and mouse-speed events. Counts keys only — never records which keys. | Win32 |
| **ScreenWatcher** | Every 3 s (only while Pixel is visible): capture primary screen excluding Pixel's windows (`WDA_EXCLUDEFROMCAPTURE`), compare a downscaled hash, run Windows OCR (`Windows.Media.Ocr`) only if changed. Keeps latest `ScreenSnapshot` (full text + foreground-window text + window title/process) **in memory only**. On hotkey, takes a fresh snapshot before the panel appears. | Win32, WinRT OCR |
| **ErrorSpotter** | Rule-based scan of each new snapshot (`Traceback`, `Exception`, `error:`, `Error:`, `FAILED`, `npm ERR!`, `SyntaxError`, `TypeError`, `fatal:`, `Unhandled`, `cannot find`, `is not recognized`…). New match (not seen in last 2 min) → cat alert. No LLM call until the user clicks the alert. | Core |
| **Pet** | Behaviour state machine (§3) + sprite animator at 12 fps. Decides position, facing, animation. | Core |
| **Brain** | Lifecycle of llama-server (start on chat open, stop after 10 min without use, restart once on crash, free-RAM check before load). Builds prompts, streams tokens, parses tool calls. | Core, HTTP |
| **Tools** | `set_reminder`, `start_pomodoro`, `stop_pomodoro`, `add_note`, `copy_to_clipboard`, `open_url`, `open_app`, `open_file`, `web_search`. First four run directly; the rest require a **Do it? [Yes] [No]** confirmation in the panel. | Core |
| **Reminders** | Water / stretch / custom reminders, Pomodoro, pinned notes. Fire as cat speech bubble + meow when visible; as a Windows toast when vanished. | Core |
| **AgentWatch** | Claude Code: hooks (`UserPromptSubmit` → working, `Stop` → done, `Notification` → needs-you) POST to `127.0.0.1:17861/event`; installed via tray "Connect Claude Code" (shows the diff to `~/.claude/settings.json`, asks first). Cursor / VS Code+Copilot / Codex & other CLI agents: best-effort heuristics from process CPU activity + window titles. | Core, HTTP listener |
| **FocusMode** | Foreground window covering the whole monitor (not desktop/shell) → cat hides at the right edge with a peek animation; returns when it ends. | Win32 |
| **Storage** | `%APPDATA%\Pixel\settings.json`, `%APPDATA%\Pixel\pixel.db` (SQLite: conversations, messages, reminders, notes). | Microsoft.Data.Sqlite |

### Data flow — asking a question
1. User presses `Ctrl+Win+Space` → Input swallows it → ScreenWatcher takes a fresh snapshot.
2. Chat panel opens beside the cat (flips side near screen edges). If the brain is not running,
   Brain starts llama-server; panel shows "waking up…" with a sleepy-cat animation.
3. User sends text → Brain builds: system prompt (persona + tool list + date/time) + screen
   context (window title, foreground-window text first, then rest of screen; capped ~8 000 chars)
   + conversation so far (oldest turns trimmed to fit `-c 8192`).
4. Tokens stream into the panel (markdown + highlighted code + copy buttons). Tool calls render
   as action cards; confirm-required ones wait for Yes/No.
5. Message pair saved to `pixel.db` (screen text is **not** saved).

## 3. The cat

- **Art:** original black cat, yellow-green eyes, pink nose/inner ears, 32×32 base frames scaled
  3× with nearest-neighbour. Authored as pixel grids in `tools/SpriteGen`, rendered to one sprite
  sheet. Fur colour/pattern changeable in settings via palette swap (default black).
- **Animations:** idle (blink, tail flick), sit, walk, run, jump, sleep (zZ), groom, stretch,
  purr (petting head), eye-tracking (pupils follow cursor), hunt/pounce, knead (typing),
  overheat (red tint + steam), paper unspool (scroll), working (tiny laptop), celebrate (jump +
  sparkles), wave (needs attention), alert ("!" bubble), confused ("?" sign), peek (focus mode),
  dangle (being dragged), poof (vanish/revive).
- **Thresholds:** knead while typing ≥ 1 key/s; overheat when ≥ 8 keys/s sustained 3 s, cools
  after 4 s below that; hunt when mouse speed ≥ 2 500 px/s within 300 px of the cat; purr after
  ≥ 3 back-and-forth mouse passes over the head; paper unspool on scroll.
- **Movement:** roams the whole screen — walks along the bottom of the work area and jumps onto
  top edges of visible top-level windows; random sit/groom/sleep pauses. Drag & drop → it
  stays (naps) at the drop spot until double-clicked, then resumes roaming.
- **Hit-testing:** window is click-through except on non-transparent cat pixels.
- **Priority** (highest wins): dangle > poof > peek > wave/celebrate/working (AI tools) > alert >
  overheat > hunt > purr > knead > paper > roam/idle states.
- **Sounds:** soft purr, meows (reminder, error spotted), poof. Volume + mute in settings.

## 4. Hotkeys & vanish

| Hotkey | Action |
|---|---|
| `Ctrl + Right Shift + Delete` | Toggle vanish. **Vanish:** poof animation, chat panel closes, current conversation is saved and ended, ScreenWatcher stops, brain unloads; reminders continue as Windows toasts. **Revive:** poof in, new empty conversation. |
| `Ctrl + Win + Space` | Toggle chat panel (only while Pixel is visible). Hiding keeps the conversation. |

Known overrides (accepted): browsers' "Clear browsing data" (`Ctrl+Shift+Del` with right Shift)
and Windows "previous input language" (`Ctrl+Win+Space`) no longer reach their apps.

## 5. Chat panel & brain

- WebView2 panel, ~360×480, dark, rounded, matches the pixel-art theme; markdown, highlighted
  code with copy buttons, streaming text, status line ("waking up…", "reading your screen…").
- **Persona:** "cute but useful" — accurate, concise answers first; occasional light cat touch.
  Answers about the screen must say when the needed text isn't visible rather than guess.
- **History:** every conversation saved locally; tray → History window to browse/search/delete.
- **Model path:** setting; defaults to the first `*.gguf` in `C:\Users\sreev\OneDrive\Documents\pixel`.
- **llama-server args (baseline):** `-m <model> --host 127.0.0.1 --port 17860 -c 8192 --jinja
  -ngl <99 for GPU backends, 0 for CPU> -t <physical cores>`.

## 6. Failure handling

| Situation | Behaviour |
|---|---|
| Model file missing / still `.crdownload` | Cat shows "?" sign; panel explains where to put the model; everything else works. |
| Free RAM < model size + 1 GB | Panel warns and offers "Load anyway" / "Cancel". |
| llama-server crashes | Auto-restart once; if it fails again, show error + "Retry" in panel. |
| OCR unavailable (no OCR language pack) | Chat works without screen context; panel notes it once. |
| Hook install fails | Tray shows a warning; hotkeys fall back to `RegisterHotKey` (left/right Shift not distinguished). |
| Claude Code hook port busy | AgentWatch picks next free port and rewrites hook URL on reconnect. |

## 7. Privacy

Screenshots never touch disk; OCR text lives only in memory and is discarded on vanish.
Keystroke *content* is never recorded. Servers bind to 127.0.0.1 only. History DB is local and
deletable from the History window. The only outbound network action is a user-approved web
search (opens the default browser).

## 8. Testing

- **Unit (xUnit, Pixel.Core):** behaviour state machine & priorities, typing-rate / overheat /
  hunt thresholds, ErrorSpotter rules, prompt builder trimming, tool-call parsing & confirmation
  gating, reminder scheduling, storage round-trips.
- **Integration:** Brain against real llama-server + model (start, stream, tool call, idle
  unload, crash restart); OCR on a known test image.
- **Manual checklist:** each animation, click-through, drag/drop/double-click, both hotkeys
  (incl. Start menu not opening), focus mode with a full-screen video, Claude Code hook events.
- **Benchmark:** tokens/s and time-to-first-token per backend (CPU / Vulkan / SYCL), recorded.

## 9. Build order

1. Body: solution scaffold, sprite generator + art, cat window, state machine, movement, input
   reactions, hotkeys, tray, vanish/revive. (Can proceed while the model downloads.)
2. Chat panel UI (with a fake brain for streaming), ScreenWatcher + OCR, ErrorSpotter.
3. Brain: llama.cpp setup + benchmark, real streaming, tool calls, history.
4. Productivity helpers, AgentWatch, FocusMode, sounds, settings/history windows, autostart.
