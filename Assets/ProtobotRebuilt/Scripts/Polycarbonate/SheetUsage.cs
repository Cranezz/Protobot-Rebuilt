using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Custom;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Polycarb {
    // V2: how much plastic the robot uses, against the game manual's limits:
    // - 2025-26 on (V5RC Push Back R25, Override R24): at most 12 pieces of non-shattering plastic, each no
    //   bigger than 4" x 8" x 0.070".
    // - Before 2025 (e.g. Over Under R18): all the plastic must come from one 12" x 24" sheet up to 0.070" thick.
    // The pieces in the build (each copy counts) are laid out on stock sheets as tightly as they go, turned to
    // fit, so you can see how many sheets to buy and cut; a piece that doesn't fit starts another sheet.
    public static class SheetUsage {
        public enum Rules { Current, OneSheet }

        public const int MaxPieces = 12;
        public static readonly Vector2 MaxPiece = new(4f, 8f);
        public static readonly Vector2 LegacySheet = new(12f, 24f);
        public const float Gap = 0.125f;   // between pieces on a sheet (saw kerf and a little room)

        private const string RulesKey = "V2.PlasticRules", SheetKey = "V2.PlasticSheet";

        public static Rules CurrentRules {
            get => (Rules)Mathf.Clamp(PlayerPrefs.GetInt(RulesKey, 0), 0, 1);
            set { PlayerPrefs.SetInt(RulesKey, (int)value); Changed?.Invoke(); }
        }

        public static readonly Vector2[] StockSheets = { new(12f, 24f), new(24f, 24f), new(12f, 12f), new(24f, 48f) };

        public static int StockIndex {
            get => Mathf.Clamp(PlayerPrefs.GetInt(SheetKey, 0), 0, StockSheets.Length - 1);
            set { PlayerPrefs.SetInt(SheetKey, value); Changed?.Invoke(); }
        }

        /// <summary>The sheet the layout uses: the rules' one sheet, or the stock size chosen.</summary>
        public static Vector2 Sheet => CurrentRules == Rules.OneSheet ? LegacySheet : StockSheets[StockIndex];

        public static event Action Changed;

        public class Piece {
            public CustomPartData part;
            public GameObject obj;
            public List<Vector2> hull, points;
            public List<int> top;
            public Vector2 size;      // its smallest box
            public float angle;       // the turn that squares that box with the sheet
            public bool tooBig;       // over 4" x 8"
            // Where it went
            public int sheet = -1;
            public Vector2 position;  // the box's lower corner on the sheet
            public bool turned;       // box turned 90°
        }

        public class Layout {
            public List<Piece> pieces = new();
            public int sheets;
            public float area;
            public bool OverCount => CurrentRules == Rules.Current && pieces.Count > MaxPieces;
            public bool AnyTooBig => CurrentRules == Rules.Current && pieces.Any(p => p.tooBig);
            public bool OverSheet => CurrentRules == Rules.OneSheet && sheets > 1;
            public bool Legal => !OverCount && !AnyTooBig && !OverSheet && pieces.All(p => p.sheet >= 0);
        }

        /// <summary>The polycarbonate pieces in the build now (not deleted).</summary>
        public static List<(CustomPartData part, GameObject obj)> PiecesInBuild() {
            var list = new List<(CustomPartData, GameObject)>();
            foreach (var obj in PartsManager.FindLoadedObjects()) {
                if (obj == null || obj.IsDeleted() || !obj.activeInHierarchy) continue;
                var saved = obj.GetComponent<SavedObject>();
                if (saved == null || !saved.id.StartsWith(CustomParts.Polycarbonate + "-")) continue;
                var part = CustomParts.Find(saved.id);
                if (part != null) list.Add((part, obj));
            }
            return list;
        }

        public static Layout Plan(IEnumerable<CustomPartData> extra = null) {
            var layout = new Layout();
            var cache = new Dictionary<string, Piece>();
            var entries = PiecesInBuild().Select(p => (p.part, p.obj)).ToList();
            if (extra != null) entries.AddRange(extra.Select(p => (p, (GameObject)null)));
            foreach (var (part, obj) in entries) {
                if (!cache.TryGetValue(part.key, out var shape)) {
                    shape = Shape(part);
                    cache[part.key] = shape;
                }
                layout.pieces.Add(new Piece {
                    part = part, obj = obj, hull = shape.hull, points = shape.points, top = shape.top, size = shape.size,
                    angle = shape.angle, tooBig = shape.tooBig,
                });
            }
            layout.area = layout.pieces.Sum(p => Area(p));
            Pack(layout);
            return layout;
        }

        private static float Area(Piece piece) {
            var area = 0f;
            for (var i = 0; i + 2 < piece.top.Count; i += 3) {
                var a = piece.points[piece.top[i]];
                var b = piece.points[piece.top[i + 1]];
                var c = piece.points[piece.top[i + 2]];
                area += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) / 2f;
            }
            return area;
        }

        public static Piece Shape(CustomPartData part) {
            var hull = PolyPart.TopOutline(part, out var points, out var top);
            var (size, angle) = PolyPart.SmallestBox(hull);
            // Prefer the box standing the long way up (it matters only for packing, which turns as needed).
            var fits = Fits(size, MaxPiece);
            return new Piece { part = part, hull = hull, points = points, top = top, size = size, angle = angle, tooBig = !fits };
        }

        public static bool Fits(Vector2 size, Vector2 limit) =>
            (size.x <= limit.x + 1e-3f && size.y <= limit.y + 1e-3f) || (size.y <= limit.x + 1e-3f && size.x <= limit.y + 1e-3f);

        // ------------------------------------------------------------------ packing (MaxRects, best short side fit)

        private static void Pack(Layout layout) {
            var sheet = Sheet;
            var free = new List<List<Rect>>();
            // Biggest first packs best.
            foreach (var piece in layout.pieces.OrderByDescending(p => p.size.x * p.size.y)) {
                var w = piece.size.x + Gap;
                var h = piece.size.y + Gap;
                if (!Fits(new Vector2(w - Gap, h - Gap), sheet)) continue;  // bigger than a whole sheet
                var placed = false;
                for (var s = 0; s <= free.Count && !placed; s++) {
                    if (s == free.Count) free.Add(new List<Rect> { new(0f, 0f, sheet.x + Gap, sheet.y + Gap) });
                    var best = Rect.zero;
                    var bestScore = float.MaxValue;
                    var bestTurned = false;
                    foreach (var r in free[s]) {
                        foreach (var turned in new[] { false, true }) {
                            var pw = turned ? h : w;
                            var ph = turned ? w : h;
                            if (pw > r.width + 1e-4f || ph > r.height + 1e-4f) continue;
                            // Bottom-left first, then the tightest fit.
                            var score = r.y * 1000f + r.x + Mathf.Min(r.width - pw, r.height - ph) * 0.01f;
                            if (score < bestScore) {
                                bestScore = score;
                                best = new Rect(r.x, r.y, pw, ph);
                                bestTurned = turned;
                            }
                        }
                    }
                    if (bestScore == float.MaxValue) continue;
                    piece.sheet = s;
                    piece.position = best.position;
                    piece.turned = bestTurned;
                    Split(free[s], best);
                    placed = true;
                }
            }
            layout.sheets = free.Count;
        }

        private static void Split(List<Rect> free, Rect used) {
            var result = new List<Rect>();
            foreach (var r in free) {
                if (!r.Overlaps(used)) {
                    result.Add(r);
                    continue;
                }
                if (used.xMin > r.xMin) result.Add(Rect.MinMaxRect(r.xMin, r.yMin, used.xMin, r.yMax));
                if (used.xMax < r.xMax) result.Add(Rect.MinMaxRect(used.xMax, r.yMin, r.xMax, r.yMax));
                if (used.yMin > r.yMin) result.Add(Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, used.yMin));
                if (used.yMax < r.yMax) result.Add(Rect.MinMaxRect(r.xMin, used.yMax, r.xMax, r.yMax));
            }
            // Drop free rectangles inside others.
            free.Clear();
            for (var i = 0; i < result.Count; i++) {
                var inside = false;
                for (var j = 0; j < result.Count && !inside; j++)
                    inside = i != j && Contains(result[j], result[i]) && (!Contains(result[i], result[j]) || j < i);
                if (!inside && result[i].width > 1e-4f && result[i].height > 1e-4f) free.Add(result[i]);
            }
        }

        private static bool Contains(Rect outer, Rect inner) =>
            inner.xMin >= outer.xMin - 1e-5f && inner.yMin >= outer.yMin - 1e-5f && inner.xMax <= outer.xMax + 1e-5f && inner.yMax <= outer.yMax + 1e-5f;

        /// <summary>Where a piece's own point lands on its sheet (after its turn and place).</summary>
        public static Vector2 OnSheet(Piece piece, Vector2 local) {
            float cos = Mathf.Cos(piece.angle), sin = Mathf.Sin(piece.angle);
            var r = new Vector2(local.x * cos - local.y * sin, local.x * sin + local.y * cos);
            // The squared box's lower corner.
            var min = new Vector2(float.MaxValue, float.MaxValue);
            foreach (var p in piece.hull) {
                var q = new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos);
                min = Vector2.Min(min, q);
            }
            r -= min;
            if (piece.turned) r = new Vector2(piece.size.y - r.y, r.x);
            return piece.position + r + new Vector2(Gap / 2f, Gap / 2f);
        }

        // ------------------------------------------------------------------ watching the build

        private static int lastCount = -1;
        private static bool lastLegal = true;

        [RuntimeInitializeOnLoadMethod]
        private static void Watch() {
            StateSystem.OnChangeState += Check;
        }

        /// <summary>After any change: a message when the build goes over (or back under) the limits.</summary>
        public static void Check() {
            var pieces = PiecesInBuild();
            if (pieces.Count == lastCount) return;
            var growing = pieces.Count > lastCount && lastCount >= 0;
            lastCount = pieces.Count;
            if (pieces.Count == 0) {
                lastLegal = true;
                Changed?.Invoke();
                return;
            }
            var layout = Plan();
            if (!layout.Legal && (lastLegal || growing))
                Interface.Interface.Say(Problem(layout), false);
            lastLegal = layout.Legal;
            Changed?.Invoke();
        }

        public static string Problem(Layout layout) {
            if (layout.OverSheet) return "Over the plastic limit: the pieces need " + layout.sheets + " sheets of 12\" x 24\" (one is allowed)";
            if (layout.OverCount) return "Over the plastic limit: " + layout.pieces.Count + " pieces (" + MaxPieces + " are allowed)";
            if (layout.AnyTooBig) return "A plastic piece is bigger than 4\" x 8\", the largest allowed";
            if (layout.pieces.Any(p => p.sheet < 0)) return "A plastic piece is bigger than the stock sheet";
            return "";
        }
    }
}
