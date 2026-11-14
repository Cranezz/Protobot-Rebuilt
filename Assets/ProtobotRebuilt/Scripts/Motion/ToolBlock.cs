namespace Protobot.Motion {
    // V2: tools with their own clicks (chain, band, tubing) turn part selection off while they're open. Several can
    // overlap for a frame (one closing as the next opens, destroyed at the frame's end), so they count: the first to
    // open keeps what selecting was blocked by before, the last to close puts it back.
    public static class ToolBlock {
        private static System.Func<bool> before;
        private static int open;

        public static void Push() {
            if (open++ == 0) before = SelectionSystem.SelectionManager.Blocked;
            SelectionSystem.SelectionManager.Blocked = () => true;
        }

        public static void Pop() {
            if (open == 0) return;
            if (--open == 0) {
                SelectionSystem.SelectionManager.Blocked = before;
                before = null;
            }
        }
    }
}
