using Stasik.Characters;
using Stasik.Interaction;
using UnityEngine;

namespace Stasik.Player
{
    /// <summary>
    /// The player's hands on the world. E acts on the best target in front (take an item, hang it on a hook, put it on a
    /// table or down in front, open or close a door by the handle); if it's out of arm's reach he walks up to it first
    /// and turns to it. C is the cap: in hand -> put on peak forward -> turn it round -> take it off into the hand.
    /// Moving the stick cancels walking up; presses while the hand is busy are ignored.
    /// </summary>
    [RequireComponent(typeof(PlayerInput), typeof(PlayerMovement))]
    public class PlayerInteract : MonoBehaviour
    {
        [SerializeField] private CharacterHand _hand;
        [SerializeField] private Transform _camera;
        [SerializeField] private float _range = 2f;        // m, horizontal, from the root to where the hand goes
        [SerializeField] private float _maxAngle = 80f;    // off where he (and the camera) looks
        [SerializeField] private float _maxHeight = 1.45f; // above the floor; higher is out of the monkey's reach
        [SerializeField] private float _standOff = 0.4f;   // m short of the target when walking up
        [SerializeField] private float _handSide = 0.14f;  // stand a bit left so the right hand is in line
        [SerializeField] private float _stickiness = 0.15f;

        private PlayerInput _input;
        private PlayerMovement _movement;
        private bool _locked;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        /// <summary>What E acts on now (null + holding something = put it down); for a future prompt.</summary>
        public InteractionTarget Target { get; private set; }

        public InteractVerb Verb { get; private set; }

        private void Awake()
        {
            _input = GetComponent<PlayerInput>();
            _movement = GetComponent<PlayerMovement>();
            if (_hand == null)
                _hand = GetComponentInChildren<CharacterHand>();
            if (_camera == null && Camera.main != null)
                _camera = Camera.main.transform;
        }

        private void OnDisable()
        {
            _movement.CancelApproach();
            SetLock(false);
            if (_hand != null)
                _hand.CompleteImmediately();
        }

        private void Update()
        {
            if (_hand == null)
                return;
            // Standing still while the hand takes from or puts on something in the world; head moves don't stop him.
            SetLock(_hand.IsWorldMove);

            if (!_hand.IsBusy && !_movement.IsApproaching)
                ChooseTarget();

            if (_input.InteractPressed)
            {
                if (_movement.IsApproaching)
                    _movement.CancelApproach();
                else if (!_hand.IsBusy)
                    Act();
            }

            if (_input.CapPressed && !_hand.IsBusy && !_movement.IsApproaching)
                Cap()?.TryNext(_hand);
        }

        private void Act()
        {
            if (Target is Door door)
            {
                // Hand on the handle; with something in the hand he just nudges it.
                var origin = transform.position;
                Reach(door, door.FocusPoint, () =>
                {
                    var grip = new HandOnDoor(door, transform, () => ToggleDoor(door, origin));
                    if (_hand.Held != null || !_hand.TryRunCustom(grip))
                        ToggleDoor(door, origin);
                });
                return;
            }

            var held = _hand.Held;
            if (held != null)
            {
                if (Target is HoldSpot spot && Verb != InteractVerb.None)
                    Reach(spot, ActionPoint(spot, held), () => _hand.TryPlace(spot));
                else if (Target == null)
                    PutDown(held);
            }
            else if (Target is Holdable item)
            {
                Reach(item, ActionPoint(item, null), () => _hand.TryPickUp(item));
            }
        }

        /// <summary>
        /// Opens or closes the door; if it would hit him (a pull), he steps out of its way, back to the side he came
        /// from (<paramref name="origin"/>) if he can. True = stepping out.
        /// </summary>
        private bool ToggleDoor(Door door, Vector3 origin)
        {
            var root = transform.position;
            door.Toggle(root);
            var body = GetComponent<CharacterController>();
            if (!door.WillHit(body))
                return false;
            if (!door.TryFindClearSpot(root, origin, body.radius, transform, out var spot)
                && !door.TryFindClearSpot(root, root, body.radius, transform, out spot))
                return false;
            _movement.Approach(spot, door.FocusPoint - spot, _ => { });
            return true;
        }

        /// <summary>The cap in the hand, or the one on his head.</summary>
        private HeadCap Cap()
        {
            if (_hand.Held != null)
                return _hand.Held.GetComponent<HeadCap>();
            return _hand.Head != null ? _hand.Head.GetComponentInChildren<HeadCap>() : null;
        }

        /// <summary>Acts at once when the hand reaches; otherwise turns or walks up first.</summary>
        private void Reach(InteractionTarget target, Vector3 point, System.Action act)
        {
            var root = transform.position;
            var toPoint = Vector3.ProjectOnPlane(point - root, Vector3.up);
            if (_hand.CanReachFrom(root, _hand.BodyRotation, point))
            {
                if (Vector3.Angle(transform.forward, toPoint) < 25f)
                    act();
                else
                    _movement.Face(toPoint, act);
                return;
            }

            var dir = toPoint.normalized;
            var right = Vector3.Cross(Vector3.up, dir);
            var stand = target.StandPoint != null
                ? target.StandPoint.position
                : root + toPoint - dir * _standOff - right * _handSide;
            _movement.Approach(stand, Vector3.ProjectOnPlane(point - stand, Vector3.up), arrived =>
            {
                // Still there and still something to do? Then do it (an item just out of reach hops into the hand).
                if (arrived && target != null && target.isActiveAndEnabled && target.VerbFor(_hand.Held) != InteractVerb.None)
                    act();
            });
        }

        /// <summary>Puts the held item on a table or counter in front, or lets it drop on the floor.</summary>
        private void PutDown(Holdable held)
        {
            var body = _hand.BodyRotation;
            var chest = transform.position + Vector3.up * 0.9f;
            var point = transform.position + body * new Vector3(0.12f, 0f, 0.45f);
            // Not into a wall: pull it back towards him.
            var flat = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
            if (Physics.SphereCast(chest, 0.08f, flat.normalized, out var wall, flat.magnitude, ~0, QueryTriggerInteraction.Ignore)
                && !IsOurs(wall.collider))
                point = transform.position + flat.normalized * Mathf.Max(0.15f, wall.distance - 0.1f);

            var surface = new Vector3(point.x, transform.position.y, point.z);
            var count = Physics.RaycastNonAlloc(point + Vector3.up * 1.3f, Vector3.down, _hits, 2f, ~0, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (IsOurs(hit.collider) || hit.collider.transform.IsChildOf(held.transform) || hit.normal.y < 0.7f
                    || hit.point.y - transform.position.y > 1.1f || hit.distance >= best)
                    continue;
                best = hit.distance;
                surface = hit.point;
            }

            var yaw = Quaternion.LookRotation(body * Vector3.forward, Vector3.up);
            var rest = ItemRest.InWorld(held.Base.ParentFor(new Pose(surface, yaw)), Vector3.up * 0.12f);
            _hand.TryPutDown(rest);
        }

        private void ChooseTarget()
        {
            var held = _hand.Held;
            var root = transform.position;
            var facing = transform.forward;
            if (_camera != null)
                facing = (facing + Vector3.ProjectOnPlane(_camera.forward, Vector3.up).normalized).normalized;

            InteractionTarget best = null;
            var bestVerb = InteractVerb.None;
            var bestScore = float.MaxValue;
            foreach (var target in InteractionTarget.All)
            {
                if (target == null || target.transform.IsChildOf(transform))
                    continue; // the held item, the worn cap
                var verb = target.VerbFor(held);
                if (verb == InteractVerb.None)
                    continue;
                var point = ActionPoint(target, held);
                var flat = Vector3.ProjectOnPlane(point - root, Vector3.up);
                var distance = flat.magnitude;
                var height = point.y - root.y;
                var angle = Vector3.Angle(facing, flat);
                if (distance > _range || height < -0.1f || height > _maxHeight || angle > _maxAngle || !CanSee(target, point))
                    continue;
                var score = distance / _range + 0.6f * angle / 90f - 0.1f * target.Priority - (target == Target ? _stickiness : 0f);
                if (score >= bestScore)
                    continue;
                bestScore = score;
                best = target;
                bestVerb = verb;
            }

            Target = best;
            Verb = bestVerb;
        }

        /// <summary>Where the wrist goes: the item's grip, or the grip of the held item resting on the spot.</summary>
        private static Vector3 ActionPoint(InteractionTarget target, Holdable held) =>
            target is HoldSpot spot && held != null ? held.GripAt(spot.RestFor(held).World).position : target.FocusPoint;

        /// <summary>
        /// Nothing between his chest and the point except the target itself and the furniture it's on (an item on a
        /// spot is on the spot's furniture: the cap hangs inside the coat rack's box collider).
        /// </summary>
        private bool CanSee(InteractionTarget target, Vector3 point)
        {
            var host = target.Host;
            if (host == null && target is Holdable item && item.Rest.Spot != null)
                host = item.Rest.Spot.Host;
            var chest = transform.position + Vector3.up * 0.9f;
            var to = point - chest;
            var count = Physics.RaycastNonAlloc(chest, to.normalized, _hits, to.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var t = _hits[i].collider.transform;
                if (IsOurs(_hits[i].collider) || t.IsChildOf(target.transform) || (host != null && t.IsChildOf(host.transform)))
                    continue;
                return false;
            }

            return true;
        }

        private bool IsOurs(Collider c) => c.transform.IsChildOf(transform);

        private void SetLock(bool on)
        {
            if (on == _locked)
                return;
            _locked = on;
            if (on)
                _movement.AddLock(this);
            else
                _movement.RemoveLock(this);
        }

        private void OnDrawGizmos()
        {
            if (Target == null)
                return;
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position + Vector3.up * 0.9f, Target.FocusPoint);
        }
    }
}
