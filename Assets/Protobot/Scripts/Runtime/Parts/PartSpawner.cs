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

            var jitter = new Vector3(Random.Range(-spread.x, spread.x), 0f, Random.Range(-spread.y, spread.y));
            var position = dropPoint.position + dropPoint.rotation * jitter;
            var rotation = dropPoint.rotation * Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f);

            var instance = Instantiate(prefab, position, rotation, spawnedParent);
            instance.name = part.displayName;

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

            RecentParts.Add(part.id);
            return instance;
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
