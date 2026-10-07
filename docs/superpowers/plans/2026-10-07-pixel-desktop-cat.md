# Pixel Desktop Cat Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build "Pixel", an always-on-top original black pixel cat for Windows with Comnyang-style reactions, two global hotkeys, OCR-based screen awareness, and a local Qwen2.5-Coder-14B brain via llama.cpp.

**Architecture:** `Pixel.Core` holds all pure, unit-tested logic (art, behaviour, motion, hotkey detection, error spotting, prompt building, streaming parser, tools, reminders, storage, agent tracking, synth). `Pixel.App` is a thin WPF shell: Win32 hooks, layered cat window, WebView2 chat panel, GDI capture + Windows OCR, tray, process management of `llama-server.exe`.

**Tech Stack:** C# 14 / .NET 10 (LTS) WPF `net10.0-windows10.0.19041.0`, WinForms `NotifyIcon`, Microsoft.Web.WebView2, Microsoft.Data.Sqlite, Windows.Media.Ocr (WinRT projection), xUnit, llama.cpp `llama-server` (CPU / Vulkan / SYCL, chosen by benchmark).

**Spec:** `docs/superpowers/specs/2026-10-07-pixel-desktop-cat-design.md`

## Global Constraints

- Target `net10.0-windows10.0.19041.0`; app is per-monitor-DPI-aware v2; all positions in DIPs inside Core.
- .NET SDK installed user-locally at `%LOCALAPPDATA%\Microsoft\dotnet` (no admin); app published **self-contained** win-x64.
- Hotkeys: `Ctrl + Right Shift + Delete` = toggle vanish; `Ctrl + Win + Space` = toggle chat. Both swallowed.
- llama-server on `127.0.0.1:17860`; AgentWatch listener on `127.0.0.1:17861`. Never bind other interfaces.
- Model dir default `C:\Users\sreev\OneDrive\Documents\pixel`; first `*.gguf` wins; `.crdownload`/`.part` = still downloading.
- Idle unload 10 min; context `-c 8192`; screen context cap 8 000 chars; ScreenWatcher interval 3 s.
- Thresholds: knead ≥ 1 key/s; overheat ≥ 8 keys/s for 3 s, cool after 4 s below; hunt ≥ 2 500 DIP/s within 300 DIP; purr after ≥ 3 strokes.
- Art: 32×32 cat inside a 48×40 canvas, scale 3× nearest-neighbour; default palette black fur, yellow-green eyes.
- Screen text and screenshots never written to disk; key *identity* never stored, only counts.
- Data dir `%APPDATA%\Pixel\` (`settings.json`, `pixel.db`).

## Review Focus

1. Hotkey pressed while an elevated window (Task Manager, admin terminal) is focused → LL hooks don't see it; Pixel must not crash and must still work once focus returns.
2. Win key released after `Ctrl+Win+Space` → Start menu must **not** open (mask key injected).
3. Model loaded when free memory is low (this PC: ~1 GB free phys / ~3 GB commit with apps open) → warn, never hang the UI; server crash → one restart then visible error.
4. Window the cat stands on is moved/minimised/closed → cat falls to the next platform or ground, never floats or leaves the screen.
5. Very long screen text / huge pasted question → prompt trimmed to fit `-c 8192`, no server 400 error.

---

## Phase 1 — Body

### Task 1: Toolchain + solution scaffold
**Files:** `scripts/install-dotnet.ps1`, `scripts/env.ps1`, `Pixel.sln`, `src/Pixel.Core/Pixel.Core.csproj`, `src/Pixel.App/Pixel.App.csproj` (+`App.xaml`, `app.manifest`), `tests/Pixel.Core.Tests/Pixel.Core.Tests.csproj`, `Directory.Build.props`
- [ ] Download `dotnet-install.ps1`, install SDK channel 10.0 to `%LOCALAPPDATA%\Microsoft\dotnet`; `env.ps1` sets `DOTNET_ROOT` + PATH for the session.
- [ ] Create sln: Core = class lib `net10.0-windows10.0.19041.0` (no WPF); App = WPF+WinForms exe, manifest `PerMonitorV2`; Tests = xUnit referencing Core.
- [ ] Smoke test `Smoke_Works()` passes via `dotnet test`. Commit.

### Task 2: Pixel art engine + original cat art
**Files:** `src/Pixel.Core/Art/{PngWriter,Palette,PixelCanvas,SpriteParts,CatPose,CatRenderer,Effects,AnimationId,Animations}.cs`, `tools/ArtPreview/` (console), tests `Art/*Tests.cs`
**Produces:** `uint[] CatRenderer.Render(CatPose pose, Palette p)` (48×40 BGRA, premultiplied), `Animations.Get(AnimationId) → Animation { Frame[] Frames; bool Loop }`, `Frame { CatPose Pose; int Ms }`, `Palette.Named(string)`, `PngWriter.Write(Stream, int w, int h, uint[] px)`.
- [ ] Tests: PNG signature/IHDR correct; render returns 48*40 px; index 0 → transparent; palette swap changes fur pixels only; pupil offset clamps inside eye; every AnimationId has ≥1 frame and durations > 0; mirrored render = horizontal flip.
- [ ] Hand-author parts (head 3/4 view with ears, bodies: sit, stand, walk×4, run×2, loaf/sleep, stretch, jump, fall, dangle, knead L/R, groom, peek), tails, eyes drawn procedurally (open/closed/happy/half/wide + pupil offset).
- [ ] Effects overlays: zZ, "!", "?", steam puffs, red tint, sparkles, hearts, paper roll, tiny laptop, wave paw, poof cloud.
- [ ] `ArtPreview` renders a 4× contact sheet `artifacts/contact-sheet.png`; inspect visually, iterate until cute and readable. Commit.

### Task 3: Activity meter, petting, hotkey detector (pure logic)
**Files:** `src/Pixel.Core/Input/{ActivityMeter,PettingDetector,HotkeyDetector,VK}.cs`, tests.
**Produces:** `ActivityMeter.OnKeyDown(double t)`, `.OnScroll(double t)`, `.OnMouse(double x,double y,double t)`, props `TypingRate`, `IsTyping`, `IsOverheated`, `IsScrolling`, `MouseSpeed`, `Update(double t)`; `PettingDetector.OnPointer(double x, double t)` / `IsPurring(t)`; `HotkeyDetector.OnKey(int vk, bool down) → KeyDecision { HotkeyAction? Action; bool Swallow; bool InjectMask }`, `enum HotkeyAction { ToggleVanish, ToggleChat }`.
- [ ] Tests: 8+ keys/s for 3 s → overheated; drops after 4 s slower; 0.9 keys/s → not typing; Ctrl+RShift+Delete fires once (repeat ignored) and swallows down+up; Ctrl+**L**Shift+Delete does nothing; Ctrl+Win+Space fires ToggleChat with InjectMask; Space alone passes; 3 direction reversals within 1.5 s → purring. Commit.

### Task 4: Motion + behaviour selection
**Files:** `src/Pixel.Core/Motion/{Platform,CatBody,MotionController}.cs`, `src/Pixel.Core/Behaviour/{PetSignals,Activity,BehaviourSelector,Roamer}.cs`, tests.
**Produces:** `CatBody { X, Y, Vx, Vy, Facing, Grounded }`, `MotionController.Step(double dt, IReadOnlyList<Platform> platforms, Rect workArea)`, `.WalkTo(x)`, `.JumpTo(Platform, x)`, `.Drop(x,y)`; `BehaviourSelector.Select(PetSignals s) → Activity`; `Roamer.Next(Random, CatContext) → RoamIntent`.
- [ ] Tests: gravity lands on ground; removed platform → falls to ground; never leaves work area; jump reaches target platform; priority table (dangle > poof > peek > agent > alert > overheat > hunt > purr > knead > paper > roam); stay-mode suppresses roaming until released. Commit.

### Task 5: Cat window
**Files:** `src/Pixel.App/Cat/{CatWindow.xaml,CatWindow.xaml.cs,CatController.cs}`, `src/Pixel.App/Native/{Win32.cs,Windows.cs}`
- [ ] Layered topmost tool window (no taskbar/alt-tab), `WDA_EXCLUDEFROMCAPTURE`, nearest-neighbour `Image` fed by `WriteableBitmap`; 30 fps loop driving motion + 12 fps sprite frames; eye-tracking pupils.
- [ ] Platforms from `EnumWindows` (visible, not cloaked, not minimised, top edge actually visible via `WindowFromPoint`), refreshed 2×/s.
- [ ] Drag (dangle) → drop = stay; double-click = roam; petting via MouseMove on head; re-assert topmost every 2 s. Manual check: runs, roams, clicks pass through transparent area. Commit.

### Task 6: Global input hooks + vanish/revive + tray + settings
**Files:** `src/Pixel.App/Input/HookThread.cs`, `src/Pixel.App/Shell/{TrayIcon,AppHost,SingleInstance,AutoStart}.cs`, `src/Pixel.Core/Storage/{Settings,SettingsStore}.cs`, tests for settings.
- [ ] Dedicated thread with message loop hosting `WH_KEYBOARD_LL` + `WH_MOUSE_LL`; callbacks only enqueue; mask key `0xE8` via `SendInput`; fallback `RegisterHotKey` if hook install fails.
- [ ] Vanish: poof, hide windows, stop watcher, unload brain, end conversation. Revive: poof in, new conversation.
- [ ] Tray menu: Show/Hide, Roam/Stay, Settings, History, Notes, Connect Claude Code, Start with Windows ✓, Quit. Settings JSON round-trip test with defaults + unknown fields tolerated. Commit.

## Phase 2 — Chat & screen

### Task 7: Chat panel (WebView2) with fake brain
**Files:** `src/Pixel.App/Chat/{ChatWindow.xaml,ChatWindow.xaml.cs,ChatBridge.cs}`, `src/Pixel.App/web/{index.html,style.css,app.js,vendor/*}`
- [ ] Normal (non-layered) borderless window, DWM rounded corners + dark, ~360×480, placed beside cat and flipped near edges; lazily created; `MemoryUsageTargetLevel=Low` when hidden.
- [ ] JS↔C# messages: `send`, `token`, `done`, `status`, `action-card`, `confirm`, `copy`, `reset`. Markdown (marked) + highlight.js bundled locally, copy buttons, streaming caret.
- [ ] Fake brain echoes in chunks; Ctrl+Win+Space toggles; manual check. Commit.

### Task 8: ScreenWatcher, OCR, ErrorSpotter
**Files:** `src/Pixel.Core/Screen/{ScreenSnapshot,ChangeDetector,ErrorSpotter}.cs`, `src/Pixel.App/Screen/{ScreenCapture,OcrService,ScreenWatcher}.cs`, tests.
- [ ] Tests: identical thumbnails → unchanged; small diff below threshold → unchanged; `Traceback (most recent call last)` → finding; same finding within 2 min → suppressed; plain prose with word "error-free" → no finding.
- [ ] GDI capture (excludes our windows), grayscale 64×36 hash, `OcrEngine.TryCreateFromUserProfileLanguages()`; foreground window text = OCR lines inside its rect. Error finding → cat alert + meow. Commit.

## Phase 3 — Brain

### Task 9: llama.cpp setup + benchmark
**Files:** `scripts/setup-llama.ps1`, `scripts/bench.ps1`, `docs/benchmark.md`
- [ ] Download latest release zips (cpu, vulkan, sycl) into `runtime/llama/<backend>/`; bench each with `llama-bench` (pp512/tg128) and record; pick fastest that loads under current memory (CPU+mmap avoids commit charge). Write choice to settings. Commit scripts + results.

### Task 10: Brain service — server lifecycle, prompt, streaming
**Files:** `src/Pixel.Core/Brain/{ChatMessage,PromptBuilder,SseParser,ToolCallAccumulator,ModelLocator,MemoryGate,ServerManager,IProcessRunner}.cs`, `src/Pixel.App/Brain/BrainService.cs`, tests.
- [ ] Tests: PromptBuilder puts foreground text before rest, caps 8 000 chars, trims oldest history to budget, includes date/time + persona; SseParser handles split chunks, `[DONE]`, tool-call deltas; ToolCallAccumulator also parses `<tool_call>{…}</tool_call>` text; ModelLocator: missing / downloading / found; MemoryGate warns when free < need; ServerManager restarts once then Failed, idle 10 min → Stop.
- [ ] Wire into chat panel replacing fake brain; integration run against real model when download completes. Commit.

### Task 11: Tools + confirmation
**Files:** `src/Pixel.Core/Tools/{ToolDefinitions,ToolPolicy,ToolArgs}.cs`, `src/Pixel.App/Tools/ToolExecutor.cs`, tests.
- [ ] Tests: reminder/pomodoro/note tools need no confirmation; copy/open/search need it; `open_url` rejects non-http(s); `web_search` builds encoded URL; bad JSON → friendly error result.
- [ ] Executor runs tools, returns tool result message, loops model once more for final answer. Commit.

### Task 12: History storage + window
**Files:** `src/Pixel.Core/Storage/HistoryStore.cs`, `src/Pixel.App/History/HistoryWindow.xaml(.cs)`, tests.
- [ ] Tests (temp db): start conversation, append, end, list newest-first, search text, delete. Window lists/searches/deletes. Commit.

## Phase 4 — Helpers & polish

### Task 13: Reminders, Pomodoro, notes
**Files:** `src/Pixel.Core/Reminders/{ReminderScheduler,Pomodoro}.cs`, `src/Pixel.App/Reminders/{ReminderService,Bubble}.cs`, `src/Pixel.App/Notes/StickyNoteWindow.xaml(.cs)`, tests.
- [ ] Tests (fake clock): water every 60 min, stretch every 45 min, one-shot fires once, Pomodoro 25/5 ×4 then 15; vanished → toast path. Commit.

### Task 14: AgentWatch
**Files:** `src/Pixel.Core/Agents/{AgentStateTracker,HookEventParser,ProcessActivityHeuristic}.cs`, `src/Pixel.App/Agents/{AgentListener,ClaudeHookInstaller,ProcessWatcher}.cs`, tests.
- [ ] Tests: multi-session working/done; done → celebrate 4 s; needs-you → wave until next event or 2 min; stale working expires after 10 min; CPU hysteresis for heuristics.
- [ ] TcpListener HTTP on 127.0.0.1:17861; installer previews and merges hooks into `~/.claude/settings.json` after confirmation. Commit.

### Task 15: Focus mode, sounds, settings window, autostart, publish
**Files:** `src/Pixel.Core/Focus/FocusRule.cs`, `src/Pixel.Core/Sound/ChipSynth.cs`, `src/Pixel.App/Settings/SettingsWindow.xaml(.cs)`, `scripts/publish.ps1`, `README.md`, tests.
- [ ] Tests: fullscreen rule (excludes desktop/shell classes); synth WAV header valid, volume 0 = silence.
- [ ] Publish self-contained to `dist/`, register autostart, run full manual checklist from the spec. Commit.
