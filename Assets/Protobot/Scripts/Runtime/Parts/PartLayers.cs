using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>
    /// Physics layers parts move between. A part in your hands touches nothing (it can't knock other parts
    /// across the bench, and you can bring it right up to them); let go, it's solid again. Screws and nuts
    /// that are in an assembly touch nothing either (their tips poke into the bench, and they're too small to
    /// matter for the robot's shape). Raycasts still find both, so they can be aimed at and clicked.
    /// </summary>
    public static class PartLayers
    {
        public const int Parts = 13;  // the original Protobot's "Parts" layer
        public const int ViewModel = 15;
        public const int Held = 16;
        public const int Fastened = 17;

        /// <summary>What a held part stays in front of: everything solid except the player and the tool in hand.</summary>
        public const int Solid = ~((1 << 2) | (1 << 5) | (1 << ViewModel) | (1 << Held) | (1 << Fastened));

        /// <summary>The collision matrix for these layers (also written into the project by Setup Player).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void ApplyCollisionRules()
        {
            for (var layer = 0; layer < 32; layer++)
            {
                Set(Held, layer, true);
                Set(Fastened, layer, true);
            }
        }

        static void Set(int a, int b, bool ignore)
        {
            if (Physics.GetIgnoreLayerCollision(a, b) != ignore)
                Physics.IgnoreLayerCollision(a, b, ignore);
        }

        /// <summary>Puts every collider under <paramref name="root"/> on a layer; returns what each was on before.</summary>
        public static List<(GameObject go, int layer)> Move(GameObject root, int layer)
        {
            var before = new List<(GameObject, int)>();
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                var go = collider.gameObject;
                if (go.layer == layer || go.layer == Fastened || before.Exists(entry => entry.Item1 == go))
                    continue;
                before.Add((go, go.layer));
                go.layer = layer;
            }
            return before;
        }

        public static void Restore(List<(GameObject go, int layer)> before)
        {
            foreach (var (go, layer) in before)
                if (go != null)
                    go.layer = layer;
        }

        /// <summary>
        /// A part let go inside something solid (the bench, another part) would be shoved out by the physics
        /// engine; this lifts it straight out instead, the shortest way.
        /// </summary>
        public static void Unstick(Rigidbody body)
        {
            var own = body.GetComponentsInChildren<Collider>();
            for (var iteration = 0; iteration < 6; iteration++)
            {
                var push = Vector3.zero;
                var deepest = 0f;
                foreach (var c in own)
                {
                    if (c.isTrigger || !c.enabled || c.gameObject.layer == Fastened)
                        continue;
                    var b = c.bounds;
                    foreach (var other in Physics.OverlapBox(b.center, b.extents, Quaternion.identity, Solid, QueryTriggerInteraction.Ignore))
                    {
                        if (other.attachedRigidbody == body)
                            continue;
                        if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation,
                                other, other.transform.position, other.transform.rotation, out var direction, out var distance)
                            && distance > deepest)
                        {
                            deepest = distance;
                            push = direction * (distance + 0.0002f);
                        }
                    }
                }
                if (deepest <= 0.0003f)
                    return;
                body.position += push;
                body.transform.position += push;
                Physics.SyncTransforms();
            }
        }

        /// <summary>A screw or nut joined into an assembly stops touching things; taken out, it's a part again.</summary>
        public static void SetFastened(PartBody part, bool fastened)
        {
            foreach (var collider in part.GetComponentsInChildren<Collider>(true))
            {
                var go = collider.gameObject;
                if (fastened && go.layer == Parts)
                    go.layer = Fastened;
                else if (!fastened && go.layer == Fastened)
                    go.layer = Parts;
            }
        }
    }
}
