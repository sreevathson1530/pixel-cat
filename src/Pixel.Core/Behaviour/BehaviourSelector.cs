namespace Pixel.Core.Behaviour;

public enum AgentState { None, Working, Done, NeedsYou }

public enum ChatState { None, Thinking, Talking }

public enum PoofState { None, Out, In }

/// <summary>Everything happening around the cat this tick.</summary>
public sealed record PetSignals
{
    public bool Held { get; init; }
    public PoofState Poof { get; init; }
    public ChatState Chat { get; init; }
    public bool FocusMode { get; init; }
    public AgentState Agent { get; init; }
    public bool Alert { get; init; }
    public bool Overheated { get; init; }
    public bool Hunting { get; init; }
    public bool Purring { get; init; }
    public bool Typing { get; init; }
    public bool Scrolling { get; init; }
}

public enum Activity
{
    Roam, Dangle, PoofOut, PoofIn, Thinking, Talk, Peek, AgentWorking, AgentDone, AgentNeedsYou,
    Alert, Overheat, Hunt, Purr, Knead, Paper,
}

/// <summary>Picks the single most important thing the cat should be doing (spec §3 priority list).</summary>
public static class BehaviourSelector
{
    public static Activity Select(PetSignals s)
    {
        if (s.Held) return Activity.Dangle;
        if (s.Poof == PoofState.Out) return Activity.PoofOut;
        if (s.Poof == PoofState.In) return Activity.PoofIn;
        if (s.Chat == ChatState.Thinking) return Activity.Thinking;
        if (s.Chat == ChatState.Talking) return Activity.Talk;
        if (s.FocusMode) return Activity.Peek;
        switch (s.Agent)
        {
            case AgentState.NeedsYou: return Activity.AgentNeedsYou;
            case AgentState.Done: return Activity.AgentDone;
            case AgentState.Working: return Activity.AgentWorking;
        }
        if (s.Alert) return Activity.Alert;
        if (s.Overheated) return Activity.Overheat;
        if (s.Hunting) return Activity.Hunt;
        if (s.Purring) return Activity.Purr;
        if (s.Typing) return Activity.Knead;
        if (s.Scrolling) return Activity.Paper;
        return Activity.Roam;
    }
}

public static class HuntRule
{
    public const double MinSpeed = 2500;
    public const double MaxDistance = 300;

    public static bool ShouldHunt(double mouseSpeed, double distanceToCat) =>
        mouseSpeed >= MinSpeed && distanceToCat <= MaxDistance;
}
