using UnityEngine;

namespace Protobot.Interface {
    /// <summary>V2: marks an object that isn't a build part as something FeaturePicker can pick (holes aside: its
    /// faces, corners and edges), e.g. the pieces of a game's alignment tool. It needs a collider on the Default layer
    /// and a readable mesh in its MeshFilter.</summary>
    public class PickablePart : MonoBehaviour { }
}
