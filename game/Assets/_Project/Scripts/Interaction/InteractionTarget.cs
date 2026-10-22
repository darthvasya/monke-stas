using System.Collections.Generic;
using UnityEngine;

namespace Stasik.Interaction
{
    /// <summary>
    /// Something a character can act on (an item, a hook, later a stove or a bed). Targets register themselves, so
    /// finding them needs no layers or trigger colliders.
    /// </summary>
    public abstract class InteractionTarget : MonoBehaviour
    {
        private static readonly List<InteractionTarget> Registry = new();

        [SerializeField] private GameObject _host;      // furniture it belongs to: its colliders don't block the view
        [SerializeField] private Transform _standPoint; // where a character stands to use it (optional)
        [SerializeField] private int _priority;

        public static IReadOnlyList<InteractionTarget> All => Registry;

        public GameObject Host
        {
            get => _host;
            set => _host = value;
        }

        public Transform StandPoint
        {
            get => _standPoint;
            set => _standPoint = value;
        }

        public int Priority => _priority;

        /// <summary>Where the hand goes to act on it.</summary>
        public abstract Vector3 FocusPoint { get; }

        /// <summary>What a character holding <paramref name="held"/> (null = empty hand) can do here; None = nothing.</summary>
        public abstract InteractVerb VerbFor(Holdable held);

        protected virtual void OnEnable() => Registry.Add(this);

        protected virtual void OnDisable() => Registry.Remove(this);

        // Domain reload is off in this project: statics survive entering Play mode.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => Registry.Clear();
    }
}
