using System.Globalization;
using System.Text;
using Pixel.Core.Screen;

namespace Pixel.Core.Brain;

/// <summary>
/// Builds the model prompt: persona system message, recent turns, and the screen (OCR text, plus the screenshot for
/// a brain that can see) attached to the newest question only. Everything is budgeted in characters so the prompt
/// always fits the 8k context.
/// </summary>
public static class PromptBuilder
{
    public const int MaxScreenChars = 4000; // ≈1k tokens: active window first; most questions need far less
    public const int MaxPromptChars = 18000; // ≈ 6k tokens, leaving room for the answer in an 8k context

    /// <summary>Room the screenshot takes in the context, in prompt characters (≈3 per token).</summary>
    public const int ScreenshotChars = ServerOptions.ImageMaxTokens * 3;

    private const string Truncated = "\n…[truncated]";
    private const string NoScreen = "[Screen: not shared for this question]\n\n";

    /// <param name="vision">The brain can look at pictures, so the screenshot goes along with the OCR text.</param>
    public static List<ChatMessage> Build(
        IReadOnlyList<ChatMessage> conversation, ScreenSnapshot? screen, DateTimeOffset now, bool vision = false)
    {
        var system = new ChatMessage(ChatRole.System, vision ? VisionPersona : Persona);
        var turns = SplitTurns(conversation);
        if (turns.Count == 0) return [system];

        // Newest question carries the clock and screen, so the system prompt (and the tool list the server appends
        // to it) stays byte-identical between questions and llama-server can reuse its cache.
        var last = turns[^1];
        var question = last[0].Content;
        var image = vision ? screen?.Screenshot : null;
        var screenBlock = $"[Now: {now.ToString("dddd, d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture)}]\n"
                          + (screen is null ? NoScreen : ScreenBlock(screen, image is not null));
        var extra = screenBlock.Length + (image is null ? 0 : ScreenshotChars);

        while (turns.Count > 1 && Size(system, turns, extra) > MaxPromptChars) turns.RemoveAt(0);

        var room = MaxPromptChars - Size(system, turns, extra) + question.Length;
        if (question.Length > room) question = question[..Math.Max(0, room - Truncated.Length)] + Truncated;

        last[0] = last[0] with { Content = screenBlock + question, ImageJpeg = image };
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
            // History never carries pictures: only the newest question gets the current screenshot.
            if (m.Role == ChatRole.User || turns.Count == 0) turns.Add([]);
            turns[^1].Add(m.ImageJpeg is null ? m : m with { ImageJpeg = null });
        }
        // A conversation must end on the user's question for the screen block to attach to it.
        while (turns.Count > 0 && turns[^1][0].Role != ChatRole.User) turns.RemoveAt(turns.Count - 1);
        return turns;
    }

    private static string ScreenBlock(ScreenSnapshot s, bool withScreenshot)
    {
        var sb = new StringBuilder("<screen>\n");
        if (withScreenshot) sb.Append("Screenshot: attached (the user's whole screen right now)\n");
        sb.Append("Active window: ").Append(string.IsNullOrWhiteSpace(s.WindowTitle) ? "(untitled)" : s.WindowTitle);
        if (!string.IsNullOrEmpty(s.ProcessName)) sb.Append(" (").Append(s.ProcessName).Append(')');
        sb.Append('\n');

        var budget = MaxScreenChars;
        var hasForeground = !string.IsNullOrWhiteSpace(s.ForegroundText);
        var hasOther = !string.IsNullOrWhiteSpace(s.OtherText);
        if (hasForeground)
        {
            sb.Append("--- words in the active window (OCR) ---\n");
            budget -= AppendCapped(sb, s.ForegroundText, budget);
        }
        if (hasOther && budget > 200)
        {
            sb.Append("\n--- words elsewhere on screen (OCR) ---\n");
            AppendCapped(sb, s.OtherText, budget);
        }
        if (!hasForeground && !hasOther) sb.Append("(no readable words on screen)");
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

    private const string Intro =
        """
        You are Pixel, a small black pixel-art cat who lives on the user's Windows desktop and helps them with anything.
        Personality: cute but useful. Give accurate, concise answers first; an occasional light cat touch ("mrrp", 🐾) is fine, never at the expense of clarity.
        Format answers in Markdown. Put code in fenced blocks with a language tag.
        """;

    private const string Outro =
        """
        Words like "this", "that", "here", "that person", "the page" or "my screen" mean what is on the user's screen, not you.
        If a question says [Screen: not shared], the user switched the 👁 eye off for it: answer without the screen, and mention they can switch it on.
        Use the tools only when the user asks for an action (reminders, timers, notes, copying, opening things, web search).
        Each question begins with the current date and time as [Now: …].
        """;

    /// <summary>For a text-only brain: it reads the screen's words but can't see pictures.</summary>
    public const string Persona =
        Intro + "\n" +
        """
        You can read the user's screen. Each question comes with a <screen> block: the window they are using and the words on screen, read by OCR (OCR can confuse l/I/1, O/0, rn/m — read through that).
        Answer from it directly. Never say you can't see the screen when a <screen> block is present.
        You read words only: pictures, photos, faces and colours are invisible to this brain. When asked about one, say so in a sentence and suggest choosing a vision brain (a Qwen3-VL model) in Settings.
        """ + "\n" + Outro;

    /// <summary>For a brain that also gets a screenshot with each question.</summary>
    public const string VisionPersona =
        Intro + "\n" +
        """
        You can see the user's screen. Each question comes with a screenshot of their whole screen and a <screen> block: the window they are using and the exact words on screen, read by OCR (OCR can confuse l/I/1, O/0, rn/m).
        Look at the screenshot for pictures, photos, people, colours and layout; use the OCR words for exact text, numbers and code.
        Answer from what you see. Never say you can't see the screen when a screenshot or <screen> block is present; if something really isn't visible, say what you do see instead.
        """ + "\n" + Outro;
}
