using System;
using UnityEngine;

namespace Stasik.Interaction
{
    /// <summary>
    /// An item a character can take in his hand, carry, hang on a hook, put on a surface (the cap, later the pot, the
    /// gamepad...). The anchors say how: where the wrist goes to hold it (the hand bone: fingers along +Y, palm towards
    /// -Z), which point hangs on a hook, which point stands on a surface, and how it's carried. A new item is a prefab
    /// with this component, no new code.
    /// </summary>
    [DisallowMultipleComponent]
    public class Holdable : InteractionTarget
    {
        [SerializeField] private LocalPose _grip = new(new Vector3(0f, 0.08f, 0f), Quaternion.LookRotation(Vector3.up, Vector3.forward));
        [SerializeField] private bool _canHang;
        [SerializeField] private LocalPose _hang;   // aligns with a hook frame (+Y up, +Z out of the hook)
        [SerializeField] private LocalPose _base;   // aligns with a point on a surface (+Y up)

        [Header("Carrying (body frame from the hips: x right, y up, z forward)")]
        [SerializeField] private Vector3 _carryHand = new(0.2f, 0.12f, 0.22f); // the wrist
        [SerializeField] private Vector3 _carryEuler;                        // the item
        [SerializeField, Range(0f, 1f)] private float _carryWeight = 0.85f;  // < 1 lets some of the walk swing through

        private ItemRest _rest;
        private bool _hasRest;
        private Collider[] _colliders = Array.Empty<Collider>();

        /// <summary>Left its rest for a hand.</summary>
        public event Action Lifted;

        /// <summary>Came to rest (on a spot, a head, a surface).</summary>
        public event Action<ItemRest> Arrived;

        public ItemRest Rest
        {
            get
            {
                EnsureRest();
                return _rest;
            }
        }

        public bool InHand => Rest.InHand;
        public bool CanHang => _canHang;
        public LocalPose Grip => _grip;
        public LocalPose Hang => _hang;
        public LocalPose Base => _base;
        public Vector3 CarryHand => _carryHand;
        public Quaternion CarryRotation => Quaternion.Euler(_carryEuler);
        public float CarryWeight => _carryWeight;

        /// <summary>The wrist pose that holds the item when the item is at <paramref name="itemPose"/>.</summary>
        public Pose GripAt(Pose itemPose) => _grip.Of(itemPose);

        public override Vector3 FocusPoint => GripAt(new Pose(transform.position, transform.rotation)).position;

        /// <summary>Taken when the hand is empty and it isn't in a hand or on someone (a worn cap is taken off with C).</summary>
        public override InteractVerb VerbFor(Holdable held) =>
            held == null && !InHand && !OnCharacter ? InteractVerb.Take : InteractVerb.None;

        private bool OnCharacter => Rest.Parent != null && Rest.Parent.GetComponentInParent<Animator>() != null;

        public void SetAnchors(LocalPose grip, LocalPose hang, LocalPose @base, bool canHang)
        {
            _grip = grip;
            _hang = hang;
            _base = @base;
            _canHang = canHang;
        }

        public void SetCarry(Vector3 hand, Vector3 euler, float weight)
        {
            _carryHand = hand;
            _carryEuler = euler;
            _carryWeight = weight;
        }

        /// <summary>For simple props: held from above by the top middle, standing on the bottom of its bounds.</summary>
        public void FitAnchorsToBounds()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            foreach (var r in renderers)
            {
                var b = r.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = transform.InverseTransformPoint(new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                    min = Vector3.Min(min, corner);
                    max = Vector3.Max(max, corner);
                }
            }

            var centre = (min + max) * 0.5f;
            // Palm down on the top, fingers forward.
            _grip = new LocalPose(new Vector3(centre.x, max.y + 0.03f, centre.z), Quaternion.LookRotation(Vector3.up, Vector3.forward));
            _base = new LocalPose(new Vector3(centre.x, min.y, centre.z), Quaternion.identity);
        }

        /// <summary>Puts it at a rest at once: parent, pose, colliders back on, the spot taken.</summary>
        public void SnapTo(ItemRest rest)
        {
            EnsureRest();
            if (_rest.Spot != null && _rest.Spot != rest.Spot && _rest.Spot.Occupant == this)
                _rest.Spot.SetOccupant(null);
            transform.SetParent(rest.Parent, true);
            if (rest.Parent != null)
                transform.SetLocalPositionAndRotation(rest.LocalPosition, rest.LocalRotation);
            else
                transform.SetPositionAndRotation(rest.LocalPosition, rest.LocalRotation);
            if (rest.Spot != null)
                rest.Spot.SetOccupant(this);
            SetColliders(true);
            _rest = rest;
            Arrived?.Invoke(rest);
        }

        /// <summary>Into a hand: off its spot, colliders off (nothing snags), under the carrier's root.</summary>
        public void BeginLift(Transform holdRoot)
        {
            EnsureRest();
            if (_rest.Spot != null && _rest.Spot.Occupant == this)
                _rest.Spot.SetOccupant(null);
            SetColliders(false);
            transform.SetParent(holdRoot, true);
            _rest = ItemRest.Hand;
            Lifted?.Invoke();
        }

        protected virtual void Awake() => EnsureRest();

        private void EnsureRest()
        {
            if (_hasRest)
                return;
            _hasRest = true;
            _colliders = GetComponentsInChildren<Collider>(true);
            _rest = ItemRest.At(transform.parent, transform.localPosition, transform.localRotation, Vector3.up * 0.12f);
        }

        private void SetColliders(bool on)
        {
            foreach (var c in _colliders)
                if (c != null)
                    c.enabled = on;
        }

        private void OnDrawGizmosSelected()
        {
            var pose = new Pose(transform.position, transform.rotation);
            DrawAxes(_grip.Of(pose), 0.05f);
            if (_canHang)
                DrawAxes(_hang.Of(pose), 0.04f);
            DrawAxes(_base.Of(pose), 0.04f);
        }

        private static void DrawAxes(Pose p, float size)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(p.position, p.position + p.rotation * Vector3.right * size);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(p.position, p.position + p.rotation * Vector3.up * size);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(p.position, p.position + p.rotation * Vector3.forward * size);
        }
    }
}
