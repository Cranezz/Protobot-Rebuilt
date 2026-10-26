using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>
    /// On every part prefab: which catalog part this is. The prefab also carries the Rigidbody (real mass),
    /// convex colliders with a physics material for its finish, and a HoverTarget so it can be grabbed.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PartBody : MonoBehaviour
    {
        [SerializeField] string partId;

        public string PartId => partId;

        public void Configure(string id) => partId = id;
    }
}
