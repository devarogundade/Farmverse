#nullable enable
using UnityEngine;

namespace Farmverse.Presentation.World
{
    /// <summary>
    /// Turns a moving part (windmill sails, water wheel) about one of its own local axes.
    /// The Tripo models are exported with the rotor's pivot at the hub; after FBX import the axle is the
    /// rotor's local Y (checked in the Tripo Showcase scene).
    /// Finds the child named "*_rotor" when no target is assigned.
    /// </summary>
    public sealed class Spinner : MonoBehaviour
    {
        [SerializeField] private Transform? rotor;
        [SerializeField] private Vector3 localAxis = Vector3.up;
        [Tooltip("Degrees per second; negative turns the other way.")]
        [SerializeField] private float speed = 30f;

        public bool ReduceMotion { get; set; }

        private void Awake()
        {
            if (rotor != null) return;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name.EndsWith("_rotor")) { rotor = t; break; }
            }
        }

        private void Update()
        {
            if (ReduceMotion || rotor == null) return;
            rotor.Rotate(localAxis, speed * Time.deltaTime, Space.Self);
        }
    }
}
