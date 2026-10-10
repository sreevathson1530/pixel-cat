using System.Runtime.CompilerServices;
using Pixel.Core.Screen;

namespace Pixel.Core.Brain;

/// <summary>
/// The real brain: finds the model, checks memory (asking the user when it would be slow), wakes llama-server
/// on demand and streams the answer.
/// </summary>
public sealed class LlamaBrain(
    ServerManager server,
    Func<ModelStatus> locateModel,
    Func<string, ServerOptions> optionsFor,
    Func<long, MemoryVerdict> checkMemory,
    Func<string, Task<bool>> askUser,
    LlamaClient client,
    IReadOnlyList<ToolSpec> tools,
    Func<DateTimeOffset> clock) : IBrain
{
    public async IAsyncEnumerable<BrainEvent> AskAsync(
        IReadOnlyList<ChatMessage> conversation, ScreenSnapshot? screen, [EnumeratorCancellation] CancellationToken ct)
    {
        string modelPath;
        switch (locateModel())
        {
            case ModelStatus.Found f:
                modelPath = f.Path;
                break;
            case ModelStatus.Downloading d:
                yield return new ErrorEvent($"My brain is still downloading ({d.File}). Ask me again once it's done! 🐾");
                yield break;
            case ModelStatus.Missing m:
                yield return new ErrorEvent($"I can't find my brain. Put the .gguf model file in {m.Directory} or choose it in Settings.");
                yield break;
            default:
                yield break;
        }

        var options = optionsFor(modelPath);
        // A warm-up may already be loading it: memory was checked then, and is now being filled by the brain itself.
        if (!server.IsAlive && server.State != ServerState.Starting)
        {
            var bytes = new FileInfo(modelPath).Length + (options.ProjectorPath is { } projector ? new FileInfo(projector).Length : 0);
            switch (checkMemory(bytes))
            {
                case MemoryVerdict.TooLow low:
                    yield return new ErrorEvent(low.Message);
                    yield break;
                case MemoryVerdict.Slow slow when !await askUser(slow.Message):
                    yield return new ErrorEvent("Okay, I'll stay asleep for now. Close a few apps and ask me again. 😴");
                    yield break;
            }

            yield return new StatusEvent("waking up my brain… the first answer takes a little while");
            if (!await server.EnsureReadyAsync(options, ct))
            {
                yield return new ErrorEvent("My brain wouldn't start. Details are in %APPDATA%\\Pixel\\llama-server.log.");
                yield break;
            }
        }
        else
        {
            if (server.State == ServerState.Starting) yield return new StatusEvent("waking up my brain… the first answer takes a little while");
            if (!await server.EnsureReadyAsync(options, ct))
            {
                yield return new ErrorEvent("My brain stopped and wouldn't restart. Details are in %APPDATA%\\Pixel\\llama-server.log.");
                yield break;
            }
        }

        using var inFlight = server.BeginRequest();
        // What the running server can do (it may have been started before a model change).
        var vision = server.Current?.ProjectorPath is not null;
        yield return new StatusEvent(screen is null ? "thinking…"
            : vision && screen.Screenshot is not null ? "looking at your screen…" : "reading your screen & thinking…");
        var prompt = PromptBuilder.Build(conversation, screen, clock(), vision);
        await foreach (var ev in client.StreamAsync(prompt, tools, ct))
        {
            server.Touch();
            yield return ev;
        }
        server.Touch();
    }

    /// <summary>
    /// Loads the brain and reads the conversation and screen while the user is still typing, so the answer starts
    /// sooner. Silent and best effort: never asks anything — when memory is short it waits for the real question.
    /// </summary>
    public async Task WarmUpAsync(IReadOnlyList<ChatMessage> conversation, ScreenSnapshot? screen, CancellationToken ct)
    {
        if (locateModel() is not ModelStatus.Found found) return;
        var options = optionsFor(found.Path);
        if (!server.IsAlive)
        {
            if (server.State == ServerState.Starting) return; // a question is already waking it
            var bytes = new FileInfo(found.Path).Length + (options.ProjectorPath is { } projector ? new FileInfo(projector).Length : 0);
            if (checkMemory(bytes) is not MemoryVerdict.Ok) return;
            if (!await server.EnsureReadyAsync(options, ct)) return;
        }

        using var inFlight = server.BeginRequest();
        var vision = server.Current?.ProjectorPath is not null;
        // The question isn't typed yet: an empty one renders everything up to where its words will go.
        var prompt = PromptBuilder.Build([.. conversation, new ChatMessage(ChatRole.User, "")], screen, clock(), vision);
        await client.PrefillAsync(prompt, tools, ct);
        server.Touch();
    }
}
