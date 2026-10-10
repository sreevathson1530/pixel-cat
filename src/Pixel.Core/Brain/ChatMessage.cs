using Pixel.Core.Screen;

namespace Pixel.Core.Brain;

public enum ChatRole { System, User, Assistant, Tool }

/// <summary>A tool call requested by the model. <see cref="ArgumentsJson"/> is the raw JSON object text.</summary>
public sealed record ToolInvocation(string Id, string Name, string ArgumentsJson);

/// <param name="ImageJpeg">A picture sent along with the text (a screenshot); set only on prompts sent to the brain.</param>
/// <param name="Screen">The screen as it was when the user asked this question (memory only, never stored).</param>
/// <param name="AskedAt">When the user asked this question.</param>
public sealed record ChatMessage(
    ChatRole Role,
    string Content,
    string? ToolCallId = null,
    IReadOnlyList<ToolInvocation>? ToolCalls = null,
    byte[]? ImageJpeg = null,
    ScreenSnapshot? Screen = null,
    DateTimeOffset? AskedAt = null);

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

    /// <summary>Gets ready for the next question (while the user is still typing). Best effort and silent.</summary>
    /// <returns>What it did, for the log (never screen content).</returns>
    Task<string> WarmUpAsync(IReadOnlyList<ChatMessage> conversation, ScreenSnapshot? screen, CancellationToken ct) =>
        Task.FromResult("not needed");
}
