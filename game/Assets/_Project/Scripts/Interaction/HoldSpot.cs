using UnityEngine;

namespace Stasik.Interaction
{
    /// <summary>
    /// A place an item rests: a hook (items that can hang, by their hang anchor) or a spot on a surface (by their base).
    /// The spot's frame: +Y up, +Z out of the hook / towards the person using it.
    /// </summary>
    public class HoldSpot : InteractionTarget
    {
        public enum Kind
        {
            Hook,
            Surface,
        }

        [SerializeField] private Kind _kind = Kind.Surface;
        [SerializeField] private Holdable _occupant;
        [SerializeField] private Vector3 _exit = new(0f, 0.05f, 0.12f); // local: the way a hand takes an item off
        [SerializeField] private float _wobble = 8f;                    // degrees an item swings after hanging (hooks)
        [SerializeField] private float _wobbleTime = 0.8f;

        private float _wobbleLeft;

        public Holdable Occupant => _occupant;

        public Kind SpotKind
        {
            get => _kind;
            set => _kind = value;
        }

        public bool Accepts(Holdable item) =>
            item != null && (_occupant == null || _occupant == item) && (_kind == Kind.Surface || item.CanHang);

        /// <summary>The rest that puts <paramref name="item"/>'s hang (hook) or base (surface) anchor on this spot.</summary>
        public ItemRest RestFor(Holdable item)
        {
            var anchor = _kind == Kind.Hook ? item.Hang : item.Base;
            var local = anchor.ParentFor(new Pose(Vector3.zero, Quaternion.identity));
            return ItemRest.At(transform, local.position, local.rotation, transform.TransformDirection(_exit), false, this);
        }

        public override Vector3 FocusPoint => transform.position;

        public override InteractVerb VerbFor(Holdable held) =>
            held != null && !OccupiedByOther(held) && Accepts(held)
                ? _kind == Kind.Hook ? InteractVerb.Hang : InteractVerb.Put
                : InteractVerb.None;

        internal void SetOccupant(Holdable item) => _occupant = item;

        /// <summary>The item swings on the hook and settles (after it's hung up).</summary>
        public void PlayWobble()
        {
            if (_kind == Kind.Hook && _wobble > 0f)
                _wobbleLeft = _wobbleTime;
        }

        private bool OccupiedByOther(Holdable item) => _occupant != null && _occupant != item;

        // A refitted item prefab hangs right without rebuilding the scene.
        private void Start()
        {
            if (_occupant != null && _occupant.Rest.Parent == transform)
                _occupant.SnapTo(RestFor(_occupant));
        }

        private void LateUpdate()
        {
            if (_wobbleLeft <= 0f || _occupant == null || _occupant.Rest.Spot != this)
                return;
            _wobbleLeft = Mathf.Max(0f, _wobbleLeft - Time.deltaTime);
            var t = 1f - _wobbleLeft / _wobbleTime;
            var angle = _wobble * Mathf.Sin(t * Mathf.PI * 4f) * (1f - t);
            var rest = RestFor(_occupant);
            // Swing about the hook (the spot's origin), across the hook arm.
            var swing = Quaternion.AngleAxis(angle, Vector3.forward);
            _occupant.transform.SetLocalPositionAndRotation(swing * rest.LocalPosition, swing * rest.LocalRotation);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = _kind == Kind.Hook ? Color.yellow : Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.03f);
            Gizmos.DrawLine(transform.position, transform.position + transform.TransformDirection(_exit));
        }
    }
}
