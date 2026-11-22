var c = UnityEngine.Object.FindFirstObjectByType<Protobot.Online.ProfileCard>(); if (c == null) return "no card"; c.OnPointerClick(null); return "clicked";
