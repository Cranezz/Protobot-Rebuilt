namespace Protobot.Custom {
    // V2: the hole sizes VEX robots use, for adding holes to imported parts and polycarbonate pieces.
    public static class HoleSizes {
        public struct Size {
            public string label;
            public float inches;
            public bool square, threaded;
        }

        public static readonly Size[] Common = {
            new() { label = "Square 0.182\" (VEX hole)", inches = 0.182f, square = true },
            new() { label = "8-32 clearance 0.177\"", inches = 0.177f },
            new() { label = "8-32 tapped 0.136\"", inches = 0.136f, threaded = true },
            new() { label = "3/16\" round", inches = 0.1875f },
            new() { label = "1/4\" round", inches = 0.25f },
            new() { label = "3/8\" round (bearing)", inches = 0.375f },
            new() { label = "1/2\" round", inches = 0.5f },
        };

        public static string[] Labels {
            get {
                var labels = new string[Common.Length];
                for (var i = 0; i < Common.Length; i++) labels[i] = Common[i].label;
                return labels;
            }
        }
    }
}
