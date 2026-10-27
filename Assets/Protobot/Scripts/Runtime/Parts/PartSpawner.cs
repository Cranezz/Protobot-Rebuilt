using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>
    /// Puts picked parts on the workbench under the window. Each part is dropped from just above
    /// whatever is already there, so new parts land on top of earlier ones instead of inside them.
    /// </summary>
    public class PartSpawner : MonoBehaviour
    {
        [Tooltip("Centre of the drop area; parts are spread around it.")]
        [SerializeField] Transform dropPoint;
        [SerializeField] Vector2 spread = new(0.5f, 0.15f);
        [SerializeField] Transform spawnedParent;
        [SerializeField] LayerMask surfaceMask = ~(1 << 2);

        public GameObject Spawn(PartEntry part)
        {
            var prefab = part.prefab != null ? part.prefab : part.model;
            if (prefab == null)
                return null;
            Saving.BuildManager.Record("Get " + part.displayName);

            var jitter = new Vector3(Random.Range(-spread.x, spread.x), 0f, Random.Range(-spread.y, spread.y));
            var position = dropPoint.position + dropPoint.rotation * jitter;

            var instance = Instantiate(prefab, position, dropPoint.rotation, spawnedParent);
            instance.name = part.displayName;
            // Lying flat with its long side along the bench, so long stock is easy to reach and saw.
            var along = Quaternion.AngleAxis(Random.Range(-8f, 8f), Vector3.up) * dropPoint.right;
            instance.transform.rotation = LyingRotation(instance, along);

            // Find the surface below (ignoring the new part itself) and rest its bounding box 3 cm above it.
            var colliders = instance.GetComponentsInChildren<Collider>();
            foreach (var c in colliders)
                c.enabled = false;
            var bounds = WorldBounds(instance);
            var probe = new Vector3(bounds.center.x, dropPoint.position.y + 1f, bounds.center.z);
            if (Physics.BoxCast(probe, new Vector3(bounds.extents.x, 0.001f, bounds.extents.z), Vector3.down, out var hit,
                    Quaternion.identity, 3f, surfaceMask, QueryTriggerInteraction.Ignore))
            {
                instance.transform.position += Vector3.up * (hit.point.y + 0.03f - bounds.min.y);
            }
            foreach (var c in colliders)
                c.enabled = true;

            // Parts interpolate, which overwrites a moved transform with the body's own pose: move both.
            if (instance.TryGetComponent(out Rigidbody body))
            {
                body.position = instance.transform.position;
                body.rotation = instance.transform.rotation;
            }

            RecentParts.Add(part.id);
            return instance;
        }

        /// <summary>
        /// The rotation that lays a part on its broadest side (thinnest dimension up) with its longest side
        /// along <paramref name="along"/>.
        /// </summary>
        public static Quaternion LyingRotation(GameObject part, Vector3 along)
        {
            var size = PartShape.LocalBounds(part).size;
            var longest = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            var thinnest = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            if (longest == thinnest)
                return Quaternion.LookRotation(along, Vector3.up);  // a cube: any way up
            var longAxis = Vector3.zero;
            longAxis[longest] = 1f;
            var upAxis = Vector3.zero;
            upAxis[thinnest] = 1f;
            return Quaternion.LookRotation(along, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(longAxis, upAxis));
        }

        static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (i == 0)
                    bounds = renderers[i].bounds;
                else
                    bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }
    }
}
