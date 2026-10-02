using System.ComponentModel;
using HsWin.App.Native;
using HsWin.Core.Logging;

namespace HsWin.App.Input;

/// <summary>
/// Sends keyboard events with SendInput (preferred over legacy keybd_event per Microsoft Learn).
/// </summary>
internal static class KeyboardInputSender
{
    // Enough for a tap with every modifier suppressed and pressed; larger requests use the heap.
    private const int StackInputCapacity = 32;

    internal static void SendKeyDown(uint virtualKey, IRuntimeLogger? logger = null)
    {
        Send([NativeInputSender.Key(virtualKey, keyUp: false)], virtualKey, logger);
    }

    internal static void SendKeyUp(uint virtualKey, IRuntimeLogger? logger = null)
    {
        Send([NativeInputSender.Key(virtualKey, keyUp: true)], virtualKey, logger);
    }

    internal static void SendTap(
        uint virtualKey,
        IReadOnlyList<uint>? suppressedModifierVirtualKeys = null,
        IReadOnlyList<uint>? modifierVirtualKeys = null,
        IRuntimeLogger? logger = null)
    {
        suppressedModifierVirtualKeys ??= [];
        modifierVirtualKeys ??= [];

        var capacity = 2 + (2 * (suppressedModifierVirtualKeys.Count + modifierVirtualKeys.Count));
        Span<NativeInput> inputs = capacity <= StackInputCapacity
            ? stackalloc NativeInput[StackInputCapacity]
            : new NativeInput[capacity];
        var count = 0;
        foreach (var modifier in suppressedModifierVirtualKeys)
        {
            inputs[count++] = NativeInputSender.Key(modifier, keyUp: true);
        }

        foreach (var modifier in modifierVirtualKeys)
        {
            inputs[count++] = NativeInputSender.Key(modifier, keyUp: false);
        }

        inputs[count++] = NativeInputSender.Key(virtualKey, keyUp: false);
        inputs[count++] = NativeInputSender.Key(virtualKey, keyUp: true);
        count = AppendRestore(inputs, count, suppressedModifierVirtualKeys, modifierVirtualKeys);

        try
        {
            Send(inputs[..count], virtualKey, logger);
        }
        catch
        {
            RestoreAfterFailedTap(virtualKey, suppressedModifierVirtualKeys, modifierVirtualKeys, logger);
            throw;
        }
    }

    /// <summary>Releases pressed modifiers, then re-presses suppressed physical ones, in reverse order.</summary>
    private static int AppendRestore(
        Span<NativeInput> inputs,
        int count,
        IReadOnlyList<uint> suppressedModifierVirtualKeys,
        IReadOnlyList<uint> modifierVirtualKeys)
    {
        for (var index = modifierVirtualKeys.Count - 1; index >= 0; index--)
        {
            inputs[count++] = NativeInputSender.Key(modifierVirtualKeys[index], keyUp: true);
        }

        for (var index = suppressedModifierVirtualKeys.Count - 1; index >= 0; index--)
        {
            inputs[count++] = NativeInputSender.Key(suppressedModifierVirtualKeys[index], keyUp: false);
        }

        return count;
    }

    private static void RestoreAfterFailedTap(
        uint virtualKey,
        IReadOnlyList<uint> suppressedModifierVirtualKeys,
        IReadOnlyList<uint> modifierVirtualKeys,
        IRuntimeLogger? logger)
    {
        var inputs = new NativeInput[1 + modifierVirtualKeys.Count + suppressedModifierVirtualKeys.Count];
        inputs[0] = NativeInputSender.Key(virtualKey, keyUp: true);
        var count = AppendRestore(inputs, 1, suppressedModifierVirtualKeys, modifierVirtualKeys);

        if (!NativeInputSender.TrySend(inputs.AsSpan(0, count), out var sentCount, out var error))
        {
            logger?.Warning($"SendInput recovery sent {sentCount}/{count} events for vk=0x{virtualKey:X2} win32=0x{error:X}.");
            return;
        }

        logger?.Warning($"Recovered keyboard state after failed tap for vk=0x{virtualKey:X2}.");
    }

    private static void Send(ReadOnlySpan<NativeInput> inputs, uint virtualKey, IRuntimeLogger? logger)
    {
        if (NativeInputSender.TrySend(inputs, out var sentCount, out var error))
        {
            return;
        }

        logger?.Warning($"SendInput sent {sentCount}/{inputs.Length} events for vk=0x{virtualKey:X2} win32=0x{error:X}.");
        throw new Win32Exception(error, $"Could not send keyboard input for virtual key 0x{virtualKey:X2}.");
    }
}
