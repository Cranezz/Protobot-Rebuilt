using System.Collections.Generic;
using Protobot.Audio;
using Protobot.Menus;
using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>
    /// The scrap bin: a trigger inside a trash can. A part dropped in (not one still in your hands, and not a
    /// frozen one) is thrown away, with a clatter and a notice saying what went.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ScrapBin : MonoBehaviour
    {
        static Hud hud;
        readonly HashSet<PartBody> gone = new();

        void Reset() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerStay(Collider other)
        {
            var part = other.GetComponentInParent<PartBody>();
            if (part == null || gone.Contains(part) || !other.attachedRigidbody)
                return;
            var body = other.attachedRigidbody;
            // Held parts float (no gravity) and frozen ones are kinematic: only things that were let go count.
            if (body.isKinematic || !body.useGravity)
                return;
            gone.Add(part);
            Sounds.PlayAt(Sounds.Trash, body.worldCenterOfMass, 0.7f);
            if (hud == null)
                hud = FindFirstObjectByType<Hud>();
            if (hud != null)
                hud.ShowToast($"Threw away {part.name}", 2f);
            Destroy(part.gameObject);
        }

        void LateUpdate() => gone.RemoveWhere(p => p == null);
    }
}
