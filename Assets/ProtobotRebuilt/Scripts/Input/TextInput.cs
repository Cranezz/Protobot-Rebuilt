using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Protobot.InputEvents {
    // V2: whether the user is typing in a text box, so shortcuts can stay out of the way.
    public static class TextInput {
        /// <summary>Set when a text box finishes editing: the Enter that ended it isn't a shortcut too.</summary>
        public static int EditEndedFrame = -10;

        /// <summary>A window or full-screen editor of the new interface is open: the 3D view's shortcuts wait.</summary>
        public static bool ShortcutsPaused => Interface.Dialog.OpenCount > 0 || Polycarb.SheetEditor.IsOpen;

        public static bool IsTyping {
            get {
                if (UnityEngine.Time.frameCount - EditEndedFrame <= 1)
                    return true;
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected == null)
                    return false;
                if (selected.TryGetComponent(out InputField field) && field.isFocused)
                    return true;
                return selected.TryGetComponent(out TMP_InputField tmpField) && tmpField.isFocused;
            }
        }
    }
}
