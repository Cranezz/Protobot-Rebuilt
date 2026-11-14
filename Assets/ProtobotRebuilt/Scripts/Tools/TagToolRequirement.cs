using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Tools {
    public class TagToolRequirement : MonoBehaviour, IToolRequirement {
        [SerializeField] private ObjectLink objectLink;
        [SerializeField] private List<string> requiredTags;
        // V2: bands and tubing follow the parts they're on (their points are edited, not the whole), so the move and
        // turn tools leave them alone.
        public bool isValid => objectLink.active && (requiredTags.Contains(objectLink.obj.tag) || requiredTags.Count == 0)
                               && objectLink.obj.GetComponentInParent<Motion.PneumaticTube>() == null && objectLink.obj.GetComponentInParent<Motion.ElasticBand>() == null;
    }
}