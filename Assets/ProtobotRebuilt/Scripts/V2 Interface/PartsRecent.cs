using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.Interface {
    // V2: the parts used most recently (newest first, kept between sessions), for the parts browser's
    // "Recently used" sort. Entries are part ids as placed ("SCRW-1/2in", "CUST-1a2b3c4d").
    public static class PartsRecent {
        private const string Key = "V2.RecentParts";
        private const int Max = 40;
        private static List<string> recent;

        public static List<string> All => recent ??= PlayerPrefs.GetString(Key, "").Split('|').Where(s => s.Length > 0).ToList();

        public static void Use(string id) {
            if (string.IsNullOrEmpty(id)) return;
            All.Remove(id);
            All.Insert(0, id);
            if (All.Count > Max) All.RemoveRange(Max, All.Count - Max);
            PlayerPrefs.SetString(Key, string.Join("|", All));
        }

        /// <summary>How recently a part type was used: 0 = most recent; large when never.</summary>
        public static int Rank(string typeId) {
            var index = All.FindIndex(id => id.Split('-')[0] == typeId);
            return index < 0 ? int.MaxValue : index;
        }

        public static int RankOf(string fullId) {
            var index = All.IndexOf(fullId);
            return index < 0 ? int.MaxValue : index;
        }
    }
}
