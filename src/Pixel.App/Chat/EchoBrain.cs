using System.Runtime.CompilerServices;
using Pixel.Core.Brain;
using Pixel.Core.Screen;

namespace Pixel.App.Chat;

/// <summary>Stand-in brain used until the model is wired in (and when PIXEL_FAKE_BRAIN=1).</summary>
internal sealed class EchoBrain : IBrain
{
    public async IAsyncEnumerable<BrainEvent> AskAsync(
        IReadOnlyList<ChatMessage> conversation, ScreenSnapshot? screen, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new StatusEvent("thinking…");
        await Task.Delay(500, ct);
        var question = conversation.LastOrDefault(m => m.Role == ChatRole.User)?.Content ?? "";
        var seen = screen is null ? "I'm not looking at the screen right now." : $"I can see **{screen.WindowTitle}**.";
        var reply = $"Mrrp! You asked: *{question}*\n\n{seen}\n\n```csharp\nConsole.WriteLine(\"my real brain arrives soon\");\n```";
        foreach (var chunk in reply.Chunk(6))
        {
            await Task.Delay(25, ct);
            yield return new TokenEvent(new string(chunk));
        }
        yield return new DoneEvent();
    }
}
