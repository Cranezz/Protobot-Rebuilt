using Protobot.Audio;
using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>
    /// On every part prefab: which catalog part this is. The prefab also carries the Rigidbody (real mass),
    /// convex colliders with a physics material for its finish, and a HoverTarget so it can be grabbed.
    /// Parts make a sound when they hit something: a clink for metal, a knock for plastic and rubber,
    /// louder the harder and heavier the hit, higher for small parts. A part joined into an assembly gives up
    /// its own Rigidbody to the assembly's (its colliders become part of one compound body) and gets it back,
    /// with the same settings, when it's taken off again.
    /// </summary>
    public class PartBody : MonoBehaviour
    {
        [SerializeField] string partId;

        Rigidbody body;
        float size;
        float lastSound = -1f;

        // The body's settings while it's lent to an assembly.
        float mass;
        float linearDamping, angularDamping;
        RigidbodyInterpolation interpolation;
        CollisionDetectionMode collisionMode;
        Vector3 localCenterOfMass;

        public string PartId => partId;
        /// <summary>The part's own Rigidbody (null while it's part of an assembly).</summary>
        public Rigidbody Body => body;
        public float Mass => body != null ? body.mass : mass;
        /// <summary>Centre of mass in the part's own space.</summary>
        public Vector3 LocalCenterOfMass => body != null ? transform.InverseTransformPoint(body.worldCenterOfMass) : localCenterOfMass;

        public void Configure(string id) => partId = id;

        /// <summary>Gives up the part's Rigidbody (its settings are kept for <see cref="RestoreBody"/>).</summary>
        public void RemoveBody()
        {
            if (body == null)
                body = GetComponent<Rigidbody>();
            if (body == null)
                return;
            mass = body.mass;
            linearDamping = body.linearDamping;
            angularDamping = body.angularDamping;
            interpolation = body.interpolation;
            collisionMode = body.collisionDetectionMode;
            localCenterOfMass = transform.InverseTransformPoint(body.worldCenterOfMass);
            DestroyImmediate(body);
            body = null;
        }

        /// <summary>A Rigidbody again, as it was before <see cref="RemoveBody"/>.</summary>
        public Rigidbody RestoreBody()
        {
            if (TryGetComponent(out body))
                return body;
            body = gameObject.AddComponent<Rigidbody>();
            body.mass = Mathf.Max(mass, 0.0005f);
            body.linearDamping = linearDamping;
            body.angularDamping = angularDamping;
            body.interpolation = interpolation;
            body.collisionDetectionMode = collisionMode;
            return body;
        }

        /// <summary>After drilling or another edit while the part has no body of its own.</summary>
        public void SetMass(float newMass)
        {
            if (body != null)
                body.mass = newMass;
            else
                mass = newMass;
        }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
                size = Mathf.Max(size, r.bounds.size.magnitude);
        }

        void OnCollisionEnter(Collision collision)
        {
            var speed = collision.relativeVelocity.magnitude;
            if (speed < 0.3f || Time.time - lastSound < 0.08f || collision.contactCount == 0)
                return;
            lastSound = Time.time;
            var contact = collision.GetContact(0);
            var metal = IsMetal(contact.thisCollider.sharedMaterial);
            var weight = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(Mass / 0.15f));
            var volume = Mathf.Clamp(speed / 2.5f, 0.06f, 1f) * weight * 0.7f;
            var pitch = Mathf.Clamp(0.12f / Mathf.Max(size, 0.02f), 0.55f, 1.8f);
            Sounds.PlayAt(metal ? Sounds.MetalHit : Sounds.PlasticHit, contact.point, volume, pitch);
        }

        static bool IsMetal(PhysicsMaterial material)
        {
            if (material == null)
                return false;
            var n = material.name;
            return n.StartsWith("Aluminum") || n.StartsWith("Steel") || n.StartsWith("Brass");
        }
    }
}
