using System.Globalization;
using System.Text;
using Pixel.Core.Screen;

namespace Pixel.Core.Brain;

/// <summary>
/// Builds the model prompt: persona system message, recent turns, and the screen text attached to the newest
/// question only. Everything is budgeted in characters so the prompt always fits the 8k context.
/// </summary>
public static class PromptBuilder
{
    public const int MaxScreenChars = 8000;
    public const int MaxPromptChars = 18000; // ≈ 6k tokens, leaving room for the answer in an 8k context
    private const string Truncated = "\n…[truncated]";

    public static List<ChatMessage> Build(IReadOnlyList<ChatMessage> conversation, ScreenSnapshot? screen, DateTimeOffset now)
    {
        var system = new ChatMessage(ChatRole.System, Persona);
        var turns = SplitTurns(conversation);
        if (turns.Count == 0) return [system];

        // Newest question carries the clock and screen block, so the system prompt (and the tool list the server
        // appends to it) stays byte-identical between questions and llama-server can reuse its cache.
        var last = turns[^1];
        var question = last[0].Content;
        var screenBlock = $"[Now: {now.ToString("dddd, d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture)}]\n"
                          + (screen is null ? "" : ScreenBlock(screen));

        while (turns.Count > 1 && Size(system, turns, screenBlock.Length) > MaxPromptChars) turns.RemoveAt(0);

        var room = MaxPromptChars - Size(system, turns, screenBlock.Length) + question.Length;
        if (question.Length > room) question = question[..Math.Max(0, room - Truncated.Length)] + Truncated;

        last[0] = last[0] with { Content = screenBlock + question };
        return [system, .. turns.SelectMany(t => t)];
    }

    private static int Size(ChatMessage system, List<List<ChatMessage>> turns, int extra) =>
        system.Content.Length + extra + turns.Sum(t => t.Sum(m => m.Content.Length + (m.ToolCalls?.Sum(c => c.ArgumentsJson.Length + c.Name.Length) ?? 0)));

    /// <summary>A turn is a user message plus everything after it until the next user message.</summary>
    private static List<List<ChatMessage>> SplitTurns(IReadOnlyList<ChatMessage> conversation)
    {
        var turns = new List<List<ChatMessage>>();
        foreach (var m in conversation)
        {
            if (m.Role == ChatRole.System) continue;
            if (m.Role == ChatRole.User || turns.Count == 0) turns.Add([]);
            turns[^1].Add(m);
        }
        // A conversation must end on the user's question for the screen block to attach to it.
        while (turns.Count > 0 && turns[^1][0].Role != ChatRole.User) turns.RemoveAt(turns.Count - 1);
        return turns;
    }

    private static string ScreenBlock(ScreenSnapshot s)
    {
        var sb = new StringBuilder();
        sb.Append("<screen>\nActive window: ").Append(s.WindowTitle);
        if (!string.IsNullOrEmpty(s.ProcessName)) sb.Append(" (").Append(s.ProcessName).Append(')');
        sb.Append('\n');

        var budget = MaxScreenChars;
        if (!string.IsNullOrWhiteSpace(s.ForegroundText))
        {
            sb.Append("--- text in the active window (OCR) ---\n");
            budget -= AppendCapped(sb, s.ForegroundText, budget);
        }
        if (!string.IsNullOrWhiteSpace(s.OtherText) && budget > 200)
        {
            sb.Append("\n--- elsewhere on screen (OCR) ---\n");
            AppendCapped(sb, s.OtherText, budget);
        }
        sb.Append("\n</screen>\n\n");
        return sb.ToString();
    }

    private static int AppendCapped(StringBuilder sb, string text, int budget)
    {
        var t = text.Trim();
        if (t.Length > budget) t = t[..Math.Max(0, budget - 1)] + "…";
        sb.Append(t);
        return t.Length;
    }

    public const string Persona =
        """
        You are Pixel, a small black pixel-art cat who lives on the user's Windows desktop and helps them with anything.
        Personality: cute but useful. Give accurate, concise answers first; an occasional light cat touch ("mrrp", 🐾) is fine, never at the expense of clarity.
        Format answers in Markdown. Put code in fenced blocks with a language tag.
        When a <screen> block is present it is OCR text of the user's screen at the moment they asked. OCR can confuse similar characters (l/I/1, O/0, rn/m) — read through that.
        If the answer depends on something that is not in the screen text, say you can't see it instead of guessing.
        Use the tools only when the user asks for an action (reminders, timers, notes, copying, opening things, web search).
        Each question begins with the current date and time as [Now: …].
        """;
}
