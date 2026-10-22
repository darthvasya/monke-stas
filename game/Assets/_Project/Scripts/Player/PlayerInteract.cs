using Stasik.Characters;
using Stasik.Interaction;
using UnityEngine;

namespace Stasik.Player
{
    /// <summary>
    /// The player's hands on the world. E acts on the best target in front (take an item, hang it on a hook, put it on a
    /// table or down in front, open or close a door by the handle); if it's out of arm's reach he walks up to it first
    /// and turns to it. C is the cap: in hand -> put on peak forward -> turn it round -> take it off into the hand.
    /// X, with the cap on: a good scratch. F with something to eat or drink in the hand: eat or drink it (HeldItemUse).
    /// Moving the stick cancels walking up; presses while the hand is busy are ignored. Runs before other scripts: the
    /// head it tilts while drinking must be in place before the cap's fur mask reads it.
    /// </summary>
    [DefaultExecutionOrder(-10)]
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

        [Header("Sitting")]
        [SerializeField] private Vector3 _sitHips = new(-0.135f, 0.33f, -0.22f); // SitIdle's hips in the root's frame
        [SerializeField] private float _seatClearance = 0.07f;                  // the hips bone above the seat
        [SerializeField] private Vector2 _sitCalm = new(-1.4f, 1.1f); // SitIdle's upright stretch, s (across its loop)

        private PlayerInput _input;
        private PlayerMovement _movement;
        private bool _locked;
        private Sitting _sitting;
        private HeldItemUse _use;
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
            _sitting?.Abort();
            _sitting = null;
            _use = null;
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

            if (_sitting != null)
            {
                // Seated: E or the stick gets him up; nothing else to pick meanwhile.
                if (_sitting.Seated && (_input.InteractPressed || _input.Move.sqrMagnitude > 0.25f))
                    _sitting.StandUp();
                if (!_sitting.Tick(Time.deltaTime))
                    _sitting = null;
            }
            else if (!_hand.IsBusy && !_movement.IsApproaching)
            {
                ChooseTarget();
            }

            if (_sitting == null && _input.InteractPressed)
            {
                if (_movement.IsApproaching)
                    _movement.CancelApproach();
                else if (!_hand.IsBusy)
                    Act();
            }

            if (_input.CapPressed && !_hand.IsBusy && !_movement.IsApproaching)
                Cap()?.TryNext(_hand);

            // Only a man in a cap may: with it on his head and the hand free.
            if (_input.ScratchPressed && !_hand.IsBusy && _hand.Held == null && Cap() is { Worn: true })
                _hand.TryRunCustom(new HandScratch());

            if (_input.UsePressed && _use == null && !_hand.IsBusy && !_movement.IsApproaching && !_movement.IsTurning
                && _hand.Held is { } held && held.TryGetComponent(out Consumable food))
            {
                var use = new HeldItemUse(held, food);
                if (_hand.TryRunCustom(use))
                    _use = use;
            }
        }

        // After the animation and the arm's IK: eating or drinking, the item stays in the fist and the head goes along.
        private void LateUpdate()
        {
            if (_use == null)
                return;
            // Not asked this frame: the hand dropped it (cancelled, disabled).
            if (_use.AskedFrame != Time.frameCount || _hand.Held == null)
            {
                _use = null;
                return;
            }

            var held = _hand.Held;
            var fist = held.Grip.ParentFor(new Pose(_hand.Bone.position, _hand.Bone.rotation));
            held.transform.SetPositionAndRotation(fist.position, fist.rotation);
            TiltHead(_use.HeadPitch);
            if (_use.Done)
            {
                // Its last goal was the carrying pose: the hand carries on from there.
                _hand.CompleteImmediately();
                _use = null;
            }
        }

        /// <summary>Head and neck turned about his left-right axis, over the animation (negative = up).</summary>
        private void TiltHead(float degrees)
        {
            if (Mathf.Abs(degrees) < 0.01f || _hand.Head == null)
                return;
            var axis = _hand.BodyRotation * Vector3.right;
            var neck = _hand.Animator.GetBoneTransform(HumanBodyBones.Neck);
            if (neck != null)
                neck.rotation = Quaternion.AngleAxis(degrees * 0.4f, axis) * neck.rotation;
            _hand.Head.rotation = Quaternion.AngleAxis(degrees * (neck != null ? 0.6f : 1f), axis) * _hand.Head.rotation;
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

            if (Target is Seat seat)
            {
                SitOn(seat);
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
            else if (Target is HoldSpot hook && Verb != InteractVerb.None && WornCap() is { } cap)
            {
                // Off the head and onto the hook in one go.
                Reach(hook, ActionPoint(hook, cap), () => _hand.TryMove(cap, hook.RestFor(cap), hook.PlayWobble));
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

        /// <summary>Walks to the seat's stand point, turns his back to it and sits down.</summary>
        private void SitOn(Seat seat)
        {
            var stand = seat.StandPoint != null
                ? seat.StandPoint.position
                : seat.transform.position + seat.transform.forward * 0.5f;
            _movement.Approach(stand, seat.transform.forward, arrived =>
            {
                if (!arrived || _sitting != null || !seat.IsFree)
                    return;
                var sitting = new Sitting(seat, transform, _movement, _hand.Animator, _sitHips, _seatClearance,
                    _sitCalm);
                if (sitting.TryStart())
                    _sitting = sitting;
            });
        }

        /// <summary>The cap in the hand, or the one on his head.</summary>
        private HeadCap Cap()
        {
            if (_hand.Held != null)
                return _hand.Held.GetComponent<HeadCap>();
            return _hand.Head != null ? _hand.Head.GetComponentInChildren<HeadCap>() : null;
        }

        /// <summary>The cap on his head, when the hand is free to take it off.</summary>
        private Holdable WornCap() =>
            _hand.Held == null && Cap() is { Worn: true, IsTurning: false } cap ? cap.Holdable : null;

        /// <summary>What he'd act on <paramref name="target"/> with: the held item, or for a hook the cap off his head.
        /// </summary>
        private Holdable ItemFor(InteractionTarget target) =>
            _hand.Held != null ? _hand.Held : target is HoldSpot ? WornCap() : null;

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
                if (arrived && target != null && target.isActiveAndEnabled
                    && target.VerbFor(ItemFor(target)) != InteractVerb.None)
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

            // Turned as it's held, so the hand finds it the same way when he takes it again.
            var turn = Vector3.ProjectOnPlane(held.transform.forward, Vector3.up);
            var yaw = Quaternion.LookRotation(turn.sqrMagnitude > 1e-4f ? turn : body * Vector3.forward, Vector3.up);
            var rest = ItemRest.InWorld(held.Base.ParentFor(new Pose(surface, yaw)), Vector3.up * 0.12f);
            _hand.TryPutDown(rest);
        }

        private void ChooseTarget()
        {
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
                var item = ItemFor(target);
                var verb = target.VerbFor(item);
                if (verb == InteractVerb.None)
                    continue;
                var point = ActionPoint(target, item);
                var flat = Vector3.ProjectOnPlane(point - root, Vector3.up);
                var distance = flat.magnitude;
                var height = point.y - root.y;
                var angle = Vector3.Angle(facing, flat);
                if (distance > _range || height < -0.1f || height > _maxHeight || angle > _maxAngle || !CanSee(target, point))
                    continue;
                var score = distance / _range + 0.6f * angle / 90f - 0.1f * target.Priority - (target == Target ? _stickiness : 0f);
                // An open door is seldom what's wanted next (the fridge's beer is): it wins only when looked right at.
                if (target is Door { IsOpen: true })
                    score += 0.3f;
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
