using System;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Core
{
    /// <summary>
    /// Tracks which menus are open. While any menu is open the mouse is a normal pointer and the
    /// player can't walk, look or grab; otherwise the cursor is locked to the centre of the screen.
    /// </summary>
    public static class MenuState
    {
        static readonly List<object> open = new();

        public static bool AnyOpen => open.Count > 0;

        /// <summary>Frame of the last open/close, so one key press (Esc) can't close one menu and open another.</summary>
        public static int ChangedFrame { get; private set; } = -1;

        /// <summary>Raised whenever a menu opens or closes.</summary>
        public static event Action Changed;

        public static void Open(object menu)
        {
            if (open.Contains(menu))
                return;
            open.Add(menu);
            ChangedFrame = Time.frameCount;
            Changed?.Invoke();
        }

        public static void Close(object menu)
        {
            if (!open.Remove(menu))
                return;
            ChangedFrame = Time.frameCount;
            Changed?.Invoke();
        }

        public static bool IsOpen(object menu) => open.Contains(menu);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            open.Clear();
            Changed = null;
        }
    }
}
