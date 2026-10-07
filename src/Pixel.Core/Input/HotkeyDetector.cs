namespace Pixel.Core.Input;

public enum HotkeyAction { ToggleVanish, ToggleChat }

/// <param name="Action">Hotkey that fired on this event, if any.</param>
/// <param name="Swallow">Block this key event from reaching other apps.</param>
/// <param name="InjectMask">Send a dummy key so releasing Win doesn't open the Start menu.</param>
public readonly record struct KeyDecision(HotkeyAction? Action, bool Swallow, bool InjectMask);

/// <summary>Virtual-key codes as reported by low-level keyboard hooks (left/right specific).</summary>
public static class VK
{
    public const int Space = 0x20, Delete = 0x2E, LWin = 0x5B, RWin = 0x5C,
        LShift = 0xA0, RShift = 0xA1, LControl = 0xA2, RControl = 0xA3, MaskKey = 0xE8;
}

/// <summary>
/// Pure hotkey state machine for <c>Ctrl + Right Shift + Delete</c> (vanish) and <c>Ctrl + Win + Space</c> (chat).
/// Fires once per press, swallows the trigger key's down/repeat/up so other apps never see it.
/// </summary>
public sealed class HotkeyDetector
{
    private static readonly int[] Modifiers = [VK.LControl, VK.RControl, VK.LShift, VK.RShift, VK.LWin, VK.RWin];
    private readonly HashSet<int> _down = [];
    private bool _swallowingDelete;
    private bool _swallowingSpace;

    private bool Ctrl => _down.Contains(VK.LControl) || _down.Contains(VK.RControl);
    private bool Win => _down.Contains(VK.LWin) || _down.Contains(VK.RWin);

    public KeyDecision OnKey(int vk, bool down)
    {
        if (Array.IndexOf(Modifiers, vk) >= 0)
        {
            if (down) _down.Add(vk);
            else _down.Remove(vk);
            return default;
        }

        if (vk == VK.Delete) return Trigger(down, Ctrl && _down.Contains(VK.RShift), ref _swallowingDelete, HotkeyAction.ToggleVanish, mask: false);
        if (vk == VK.Space) return Trigger(down, Ctrl && Win, ref _swallowingSpace, HotkeyAction.ToggleChat, mask: true);
        return default;
    }

    private static KeyDecision Trigger(bool down, bool chordHeld, ref bool swallowing, HotkeyAction action, bool mask)
    {
        if (!down)
        {
            var wasSwallowing = swallowing;
            swallowing = false;
            return new KeyDecision(null, wasSwallowing, false);
        }
        if (swallowing) return new KeyDecision(null, true, false); // auto-repeat
        if (!chordHeld) return default;
        swallowing = true;
        return new KeyDecision(action, true, mask);
    }

    /// <summary>Re-reads modifier state (e.g. via GetAsyncKeyState) after key-ups may have been missed.</summary>
    public void ResyncModifiers(Func<int, bool> isDown)
    {
        foreach (var m in Modifiers)
        {
            if (isDown(m)) _down.Add(m);
            else _down.Remove(m);
        }
    }
}
