using UnityEngine;

namespace Stasik.Interaction
{
    /// <summary>
    /// A hinged door: E opens it away from whoever pressed (or into the room only, see <see cref="DoorSwing"/>) and
    /// closes it again. The swing eases out with a little overshoot. The panel's collider moves with it, but the door
    /// never pushes into anything: it stops where something is in the way and goes on once it's clear (a player in the
    /// doorway, another open door), so it can't trap anyone. Its frame: origin on the floor in the middle of the
    /// doorway, +Z = into the room.
    /// </summary>
    public class Door : InteractionTarget
    {
        [SerializeField] private Transform _hinge;    // rotates about its Y; the panel runs along its +X
        [SerializeField] private BoxCollider _panel;  // child of the hinge
        [SerializeField] private Transform _handle;   // where the hand goes
        [SerializeField] private DoorSwing _swing = DoorSwing.Away;
        [SerializeField] private float _openAngle = 90f;
        [SerializeField] private float _duration = 0.5f;
        [SerializeField] private float _overshoot = 1.4f; // ease-out-back strength: the door bounces a bit at the end

        private const float WideEnough = 0.8f; // of the open angle: wide enough to walk through
        private const float MaxStep = 10f;     // degrees between overlap checks
        private const float CharacterGap = 0.08f;

        private readonly Collider[] _overlaps = new Collider[8];
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private Quaternion _hingeBase;
        private float _angle;
        private float _from;
        private float _to;
        private float _t = 1f;

        /// <summary>Open, or opening.</summary>
        public bool IsOpen => !Mathf.Approximately(_to, 0f);

        /// <summary>The handle: the middle of the panel, +Z = out of the panel's +Z face.</summary>
        public Transform Handle => _handle;

        public override Vector3 FocusPoint => _handle != null ? _handle.position : transform.position + Vector3.up;

        public override InteractVerb VerbFor(Holdable held) => IsOpen ? InteractVerb.Close : InteractVerb.Open;

        /// <summary>For the scene builder.</summary>
        public void Setup(Transform hinge, BoxCollider panel, Transform handle, DoorSwing swing)
        {
            _hinge = hinge;
            _panel = panel;
            _handle = handle;
            _swing = swing;
        }

        /// <summary>Opens away from <paramref name="from"/> (where the one who opens it stands), or closes it.</summary>
        public void Toggle(Vector3 from)
        {
            _from = _angle;
            _t = 0f;
            if (IsOpen)
            {
                _to = 0f;
                return;
            }

            var away = -SideOf(from);
            var first = _swing == DoorSwing.Inward ? 1 : away;
            // Furniture in the way stops it; the other way if it wouldn't open wide enough to walk through (the shower
            // stall stops the shower door at ~45°, the bed the bedroom door). People in the way just get waited for.
            var flip = _swing == DoorSwing.Away && !OpensWide(first) && OpensWide(-first);
            _to = AngleFor(flip ? -first : first);
        }

        /// <summary>
        /// Whether the swing under way would run into <paramref name="character"/> (a pull, or he stands in the
        /// doorway): then the door waits for him, so he'd better step aside.
        /// </summary>
        public bool WillHit(Collider character)
        {
            if (_t >= 1f || _panel == null || character == null)
                return false;
            var steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(_to - _angle) / MaxStep));
            for (var i = 1; i <= steps; i++)
            {
                var (center, half, rotation) = PanelBox(Mathf.Lerp(_angle, _to, (float)i / steps));
                var count = Physics.OverlapBoxNonAlloc(center, half + new Vector3(CharacterGap, 0f, CharacterGap),
                    _overlaps, rotation, ~0, QueryTriggerInteraction.Ignore);
                for (var j = 0; j < count; j++)
                {
                    if (_overlaps[j] == character)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The nearest free spot out of the panel's way on <paramref name="side"/>'s side of the doorway that a character
        /// of <paramref name="radius"/> at <paramref name="from"/> (its own colliders under <paramref name="self"/>) can
        /// walk straight to: past the latch jamb, beside the hinge, or straight out from the wall.
        /// </summary>
        public bool TryFindClearSpot(Vector3 from, Vector3 side, float radius, Transform self, out Vector3 spot)
        {
            var along = HingeRotation(0f) * Vector3.right; // from the hinge to the latch jamb
            var outward = transform.forward * SideOf(side);
            var width = Vector3.Scale(_panel.size, _panel.transform.lossyScale).x;
            var clear = width + radius + CharacterGap;
            var hinge = new Vector3(_hinge.position.x, from.y, _hinge.position.z);
            Vector3[] candidates =
            {
                hinge + along * clear + outward * (radius + 0.2f),
                hinge - along * (radius + 0.25f) + outward * (radius + 0.2f),
                hinge + along * (width / 2f) + outward * (clear + 0.05f),
            };
            System.Array.Sort(candidates, (a, b) => (a - from).sqrMagnitude.CompareTo((b - from).sqrMagnitude));
            foreach (var candidate in candidates)
            {
                if (!Occupied(candidate, radius, self) && PathClear(from, candidate, radius, self))
                {
                    spot = candidate;
                    return true;
                }
            }

            spot = default;
            return false;
        }

        private bool PathClear(Vector3 from, Vector3 to, float radius, Transform self)
        {
            var chest = from + Vector3.up * 0.6f;
            var way = Vector3.ProjectOnPlane(to - from, Vector3.up);
            var count = Physics.SphereCastNonAlloc(chest, radius * 0.8f, way.normalized, _hits, way.magnitude, ~0,
                QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                // Things he already touches (distance 0) don't stop him walking off.
                if (_hits[i].distance > 0f && !_hits[i].collider.transform.IsChildOf(self))
                    return false;
            }

            return true;
        }

        private bool Occupied(Vector3 foot, float radius, Transform self)
        {
            // From just above the floor (rugs don't count) to a monkey's height.
            var bottom = foot + Vector3.up * (radius + 0.05f);
            var top = foot + Vector3.up * Mathf.Max(radius + 0.05f, 1.25f - radius);
            var count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, _overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                if (!_overlaps[i].transform.IsChildOf(self))
                    return true;
            }

            return false;
        }

        private void Awake()
        {
            if (_hinge != null)
                _hingeBase = _hinge.localRotation;
        }

        private void Update()
        {
            if (_t >= 1f || _hinge == null)
                return;
            var t = Mathf.Min(1f, _t + Time.deltaTime / _duration);
            var target = Mathf.LerpUnclamped(_from, _to, EaseOutBack(t));
            // In small steps, so a long frame doesn't swing it through someone.
            var steps = Mathf.CeilToInt(Mathf.Abs(target - _angle) / MaxStep);
            var free = _angle;
            for (var i = 1; i <= steps; i++)
            {
                var angle = Mathf.Lerp(_angle, target, (float)i / steps);
                if (Blocked(angle, false))
                {
                    // Creep up to whatever is in the way, then try again next frame.
                    for (var j = 0; j < 4; j++)
                    {
                        var mid = (free + angle) / 2f;
                        if (Blocked(mid, false))
                            angle = mid;
                        else
                            free = mid;
                    }

                    t = _t;
                    break;
                }

                free = angle;
            }

            _t = t;
            _angle = free;
            _hinge.localRotation = _hingeBase * Quaternion.Euler(0f, _angle, 0f);
        }

        /// <summary>The hinge angle that swings the panel to <paramref name="side"/> (+1 = +Z, into the room).</summary>
        /// <summary>Can it swing to <paramref name="side"/> wide enough to walk through, furniture-wise?</summary>
        private bool OpensWide(int side)
        {
            var to = AngleFor(side) * WideEnough;
            var steps = Mathf.CeilToInt(Mathf.Abs(to) / MaxStep);
            for (var i = 1; i <= steps; i++)
            {
                if (Blocked(to * i / steps, true))
                    return false;
            }

            return true;
        }

        private float AngleFor(int side) => SideOfAngle(_openAngle) == side ? _openAngle : -_openAngle;

        /// <summary>Which side of the doorway the panel is on at <paramref name="angle"/> (+1 = +Z).</summary>
        private int SideOfAngle(float angle) =>
            transform.InverseTransformDirection(HingeRotation(angle) * Vector3.right).z > 0f ? 1 : -1;

        private int SideOf(Vector3 point) => Vector3.Dot(point - transform.position, transform.forward) > 0f ? 1 : -1;

        private Quaternion HingeRotation(float angle)
        {
            var parent = _hinge.parent != null ? _hinge.parent.rotation : Quaternion.identity;
            return parent * _hingeBase * Quaternion.Euler(0f, angle, 0f);
        }

        /// <summary>
        /// Would the panel at <paramref name="angle"/> overlap something? The box is trimmed at the hinge and the
        /// floor, so the jambs and rugs don't count. Characters are kept a bit further off: a CharacterController
        /// steps out of anything within its skin, so a door that came right up to one would shove it along.
        /// </summary>
        private bool Blocked(float angle, bool ignoreCharacters)
        {
            if (_panel == null)
                return false;
            var (world, half, rotation) = PanelBox(angle);
            var count = Physics.OverlapBoxNonAlloc(world, half, _overlaps, rotation, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var c = _overlaps[i];
                // Not itself, nor the furniture it's part of (the fridge's box collider takes in its doors).
                if (!c.transform.IsChildOf(transform) && c is not CharacterController
                    && (Host == null || !c.transform.IsChildOf(Host.transform)))
                    return true;
            }

            if (ignoreCharacters)
                return false;
            var padded = half + new Vector3(CharacterGap, 0f, CharacterGap);
            count = Physics.OverlapBoxNonAlloc(world, padded, _overlaps, rotation, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                if (_overlaps[i] is CharacterController)
                    return true;
            }

            return false;
        }

        /// <summary>The panel's box at <paramref name="angle"/>, trimmed at the hinge and the floor.</summary>
        private (Vector3 center, Vector3 half, Quaternion rotation) PanelBox(float angle)
        {
            var scale = _panel.transform.lossyScale;
            var size = Vector3.Scale(_panel.size, scale);
            var center = Vector3.Scale(_panel.center, scale);
            const float hingeGap = 0.06f, floorGap = 0.06f, edgeGap = 0.01f;
            var half = new Vector3((size.x - hingeGap - edgeGap) / 2f, (size.y - floorGap - edgeGap) / 2f, size.z / 2f);
            center += new Vector3((hingeGap - edgeGap) / 2f, (floorGap - edgeGap) / 2f, 0f);
            var rotation = HingeRotation(angle);
            return (_hinge.position + rotation * center, half, rotation);
        }

        private float EaseOutBack(float t)
        {
            var u = t - 1f;
            return 1f + (_overshoot + 1f) * u * u * u + _overshoot * u * u;
        }
    }

    public enum DoorSwing
    {
        Away,   // either way, away from whoever opens it
        Inward, // only into the room (the door's +Z)
    }
}
