using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Protobot.Motion {
    // V2: flex wheels in the simulation. A real flex wheel is soft rubber: an intake made of two rows of them closer
    // together than a game element squeezes it, and the squeeze is what lets the wheels pull it in. Rigid bodies can't
    // bend, so a flex wheel's contacts are made soft instead (PhysX contact modification): the push back grows with how
    // far the element presses into the wheel, like a spring (Stiffness, N per inch), instead of shoving it out at once,
    // and its grip on plastic is high (VEX flex wheels are grippy TPE). Sandbox registers every flex wheel ("FWHL-")
    // collider it makes; they're forgotten when it stops.
    public static class FlexGrip {
        /// <summary>Squeeze force per inch of compression, in newtons (a 2" flex wheel takes ~10 N to squash 0.15").</summary>
        public const float Stiffness = 70f;
        /// <summary>Friction of a flex wheel's tread on a game element.</summary>
        public const float StaticFriction = 1.3f, DynamicFriction = 1.1f;

        private static readonly HashSet<int> Wheels = new();
        private static readonly HashSet<int> Elements = new();   // game elements: only their contacts are softened
        private static bool hooked;
        private static float step = 1f / 240f;

        public static int Count => Wheels.Count;

        public static void Register(Collider collider) {
            collider.hasModifiableContacts = true;
            Wheels.Add(collider.GetInstanceID());
            step = Time.fixedDeltaTime;
            if (!hooked) {
                Physics.ContactModifyEvent += Modify;
                Physics.ContactModifyEventCCD += Modify;
                hooked = true;
            }
        }

        /// <summary>A game element (Scripts/Game) a flex wheel grips softly: the floor and the robot's own parts stay hard.</summary>
        public static void RegisterElement(Collider collider) {
            collider.hasModifiableContacts = true;
            Elements.Add(collider.GetInstanceID());
        }

        public static void Clear() {
            Wheels.Clear();
            Elements.Clear();
            if (hooked) {
                Physics.ContactModifyEvent -= Modify;
                Physics.ContactModifyEventCCD -= Modify;
                hooked = false;
            }
        }

        // Runs on the physics thread: only reads the set (filled before the simulation starts).
        private static void Modify(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs) {
            // Newtons -> the simulation's force unit (kg·in/s²) -> impulse over one step.
            var perInch = Stiffness * Sandbox.Newton * step;
            for (var p = 0; p < pairs.Length; p++) {
                var pair = pairs[p];
                var a = pair.colliderInstanceID;
                var b = pair.otherColliderInstanceID;
                if (!(Wheels.Contains(a) && Elements.Contains(b)) && !(Wheels.Contains(b) && Elements.Contains(a))) continue;
                for (var i = 0; i < pair.contactCount; i++) {
                    var depth = -pair.GetSeparation(i);
                    // A little push even when just touching, so a resting element isn't let through.
                    pair.SetMaxImpulse(i, perInch * Mathf.Max(depth, 0.01f));
                    pair.SetStaticFriction(i, StaticFriction);
                    pair.SetDynamicFriction(i, DynamicFriction);
                }
                pairs[p] = pair;
            }
        }
    }
}
