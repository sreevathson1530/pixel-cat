using Pixel.Core.Screen;

namespace Pixel.Core.Brain;

public enum ChatRole { System, User, Assistant, Tool }

/// <summary>A tool call requested by the model. <see cref="ArgumentsJson"/> is the raw JSON object text.</summary>
public sealed record ToolInvocation(string Id, string Name, string ArgumentsJson);

public sealed record ChatMessage(
    ChatRole Role,
    string Content,
    string? ToolCallId = null,
    IReadOnlyList<ToolInvocation>? ToolCalls = null);

/// <summary>Streamed output of one model turn.</summary>
public abstract record BrainEvent;
public sealed record StatusEvent(string Text, bool Busy = true) : BrainEvent;
public sealed record TokenEvent(string Text) : BrainEvent;
public sealed record ToolCallEvent(ToolInvocation Call) : BrainEvent;
public sealed record DoneEvent : BrainEvent;
public sealed record ErrorEvent(string Message) : BrainEvent;

/// <summary>How far the server has got reading the prompt (long screen text can take a while).</summary>
public sealed record ProgressEvent(int Processed, int Total) : BrainEvent
{
    public double Fraction => Total > 0 ? Math.Clamp((double)Processed / Total, 0, 1) : 0;
}

/// <summary>Something that can answer a conversation, optionally looking at the screen.</summary>
public interface IBrain
{
    IAsyncEnumerable<BrainEvent> AskAsync(
        IReadOnlyList<ChatMessage> conversation, ScreenSnapshot? screen, CancellationToken ct);
}
