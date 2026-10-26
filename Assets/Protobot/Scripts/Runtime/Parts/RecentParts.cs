using System;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>The parts picked most recently, newest first. Shown in the "Recently Used" bin.</summary>
    public static class RecentParts
    {
        const int MaxCount = 30;
        static readonly List<string> ids = new();

        public static IReadOnlyList<string> Ids => ids;
        public static event Action Changed;

        public static void Add(string partId)
        {
            ids.Remove(partId);
            ids.Insert(0, partId);
            if (ids.Count > MaxCount)
                ids.RemoveAt(ids.Count - 1);
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ids.Clear();
            Changed = null;
        }
    }
}
