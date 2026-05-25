using Microsoft.UI.Input;
using Windows.System;

namespace Fortiva.AppHost.Services;

internal static class KeyboardHelpers
{
    public static bool IsControlDown()
    {
        var state = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        return state.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    public static bool IsShiftDown()
    {
        var state = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        return state.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }
}
