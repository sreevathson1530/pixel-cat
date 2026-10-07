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

        if (!server.IsAlive)
        {
            switch (checkMemory(new FileInfo(modelPath).Length))
            {
                case MemoryVerdict.TooLow low:
                    yield return new ErrorEvent(low.Message);
                    yield break;
                case MemoryVerdict.Slow slow when !await askUser(slow.Message):
                    yield return new ErrorEvent("Okay, I'll stay asleep for now. Close a few apps and ask me again. 😴");
                    yield break;
            }

            yield return new StatusEvent("waking up my brain… the first answer takes a little while");
            if (!await server.EnsureReadyAsync(optionsFor(modelPath), ct))
            {
                yield return new ErrorEvent("My brain wouldn't start. Details are in %APPDATA%\\Pixel\\llama-server.log.");
                yield break;
            }
        }
        else if (!await server.EnsureReadyAsync(optionsFor(modelPath), ct))
        {
            yield return new ErrorEvent("My brain stopped and wouldn't restart. Details are in %APPDATA%\\Pixel\\llama-server.log.");
            yield break;
        }

        yield return new StatusEvent(screen is null ? "thinking…" : "reading your screen & thinking…");
        var prompt = PromptBuilder.Build(conversation, screen, clock());
        await foreach (var ev in client.StreamAsync(prompt, tools, ct))
        {
            server.Touch();
            yield return ev;
        }
        server.Touch();
    }
}
