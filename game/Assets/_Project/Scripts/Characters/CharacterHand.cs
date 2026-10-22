using System;
using System.Collections.Generic;
using Stasik.Interaction;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Stasik.Characters
{
    /// <summary>
    /// A character's right hand: takes items, carries them while walking, hangs them on hooks, puts them on surfaces,
    /// on a head and back. Every action is one move "item from rest A to rest B" (a spot, a head, a surface or the hand
    /// itself): reach A, lift it off, carry it (beside the head when a head is involved), lower it onto B, let go.
    /// Items and spots only give data (Holdable anchors, HoldSpot exits), so new items need no code.
    ///
    /// The arm is an Animation Rigging Two Bone IK (built by PlayerCharacterBuilder): this sets its target and hint
    /// before the animation runs (Update) and poses the held item from the real wrist after it (LateUpdate). The target's
    /// rotation is the hand bone's own rotation (this rig: fingers along +Y, palm towards -Z).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public class CharacterHand : MonoBehaviour
    {
        /// <summary>Where the hand should be: wrist position, hand bone rotation, elbow hint, IK weight.</summary>
        public struct Goal
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 ElbowHint;
            public float Weight;
        }

        /// <summary>A move of its own (the cap turning round on the head); returns false when it's done.</summary>
        public interface IGoalSource
        {
            bool GetHandGoal(CharacterHand hand, out Goal goal);
        }

        private enum Phase
        {
            Reach,
            Lift,
            Carry,
            Lower,
            Release,
        }

        [SerializeField] private TwoBoneIKConstraint _ik;
        [SerializeField] private Transform _holdRoot; // carried items live under it (the player root)

        [Header("Move (seconds)")]
        [SerializeField] private float _reachTime = 0.3f;
        [SerializeField] private float _liftTime = 0.18f;
        [SerializeField] private float _carryTime = 0.55f;
        [SerializeField] private float _lowerTime = 0.22f;
        [SerializeField] private float _releaseTime = 0.3f;
        [SerializeField] private Vector3 _sideSpot = new(0.32f, 0.02f, -0.05f); // beside the head, body frame

        private readonly List<(Phase phase, float end)> _phases = new();
        private Animator _animator;
        private Transform _bone;
        private Transform _hips;
        private Transform _head;
        private Transform _shoulder;
        private Vector3 _shoulderOffset; // body frame, from the root
        private float _armLength;

        private Holdable _item;          // moving
        private ItemRest _from;
        private ItemRest _to;
        private Action _arrived;
        private float _elapsed;
        private bool _lifted;
        private bool _landed;
        private float _startWeight;
        private Holdable _held;          // carried, no move running
        private IGoalSource _custom;
        private float _customTime;
        private float _weight;

        public Animator Animator => _animator;
        public Transform Head => _head;
        public Transform Bone => _bone;
        public Transform Hips => _hips;
        public Holdable Held => _held;
        public float ArmLength => _armLength;

        /// <summary>A move or a custom goal runs (just carrying isn't busy).</summary>
        public bool IsBusy => _item != null || _custom != null;

        /// <summary>The running move takes from or puts on something in the world (the player stands still for it).</summary>
        public bool IsWorldMove => _item != null && (IsWorld(_from) || IsWorld(_to));

        /// <summary>Level, the way the character's root faces (Animator.body* may only be read inside the IK pass).</summary>
        public Quaternion BodyRotation =>
            Quaternion.LookRotation(Vector3.ProjectOnPlane(Root.forward, Vector3.up).normalized, Vector3.up);

        private Transform Root => _holdRoot != null ? _holdRoot : transform.root;

        public bool CanReach(Vector3 wrist) => Vector3.Distance(_shoulder.position, wrist) <= 0.95f * _armLength;

        /// <summary>Whether the wrist could reach <paramref name="wrist"/> with the root standing at <paramref name="rootPosition"/>.</summary>
        public bool CanReachFrom(Vector3 rootPosition, Quaternion body, Vector3 wrist) =>
            Vector3.Distance(rootPosition + body * _shoulderOffset, wrist) <= 0.95f * _armLength;

        /// <summary>Moves <paramref name="item"/> from where it is to <paramref name="to"/>; the hand must be free or hold it.</summary>
        public bool TryMove(Holdable item, ItemRest to, Action arrived = null)
        {
            if (item == null || IsBusy || (_held != null && _held != item) || !isActiveAndEnabled)
                return false;
            _item = item;
            _from = item.Rest;
            _to = to;
            _arrived = arrived;
            _elapsed = 0f;
            _lifted = _from.InHand;
            _landed = false;
            _startWeight = _weight;
            _held = null;

            _phases.Clear();
            var t = 0f;
            if (!_from.InHand)
            {
                _phases.Add((Phase.Reach, t += _reachTime));
                _phases.Add((Phase.Lift, t += _liftTime));
            }

            _phases.Add((Phase.Carry, t += _carryTime));
            if (!_to.InHand)
            {
                _phases.Add((Phase.Lower, t += _lowerTime));
                _phases.Add((Phase.Release, t += _releaseTime));
            }

            return true;
        }

        public bool TryPickUp(Holdable item) => TryMove(item, ItemRest.Hand);

        public bool TryPlace(HoldSpot spot, Action arrived = null) =>
            _held != null && spot.Accepts(_held) && TryMove(_held, spot.RestFor(_held), () =>
            {
                spot.PlayWobble();
                arrived?.Invoke();
            });

        public bool TryPutDown(ItemRest surface) => _held != null && TryMove(_held, surface);

        public bool TryRunCustom(IGoalSource source)
        {
            if (source == null || IsBusy || !isActiveAndEnabled)
                return false;
            _custom = source;
            _customTime = 0f;
            return true;
        }

        /// <summary>Ends whatever runs at once, in its end state (disable, timeouts, a dialogue starting).</summary>
        public void CompleteImmediately()
        {
            _custom = null;
            if (_item == null)
                return;
            if (!_lifted)
                Lift();
            Land();
            _item = null;
        }

        /// <summary>Default elbow: out to the side, up when the wrist is up near the head.</summary>
        public Vector3 ElbowHintFor(Vector3 wrist)
        {
            var top = _head.position.y + 0.2f;
            var h = Mathf.InverseLerp(_shoulder.position.y, top, wrist.y);
            return _hips.position + BodyRotation * new Vector3(0.5f, Mathf.Lerp(0.1f, 0.55f, h), -0.15f);
        }

        internal static float Ease(float t) => t * t * (3f - 2f * t);

        internal static float Span(float u, float from, float to) => Mathf.Clamp01((u - from) / Mathf.Max(to - from, 1e-4f));

        internal static Vector3 Bezier(Vector3 a, Vector3 control, Vector3 b, float t) =>
            Vector3.Lerp(Vector3.Lerp(a, control, t), Vector3.Lerp(control, b, t), t);

        /// <summary>
        /// From one rotation to another, the turn round the vertical done explicitly and the small rest by slerp. Half a
        /// turn goes the way of <paramref name="halfTurnSign"/> (the crown swings round the outside of the hand).
        /// </summary>
        internal static Quaternion Turn(Quaternion from, Quaternion to, float t, float halfTurnSign)
        {
            var yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(from * Vector3.forward, Vector3.up),
                Vector3.ProjectOnPlane(to * Vector3.forward, Vector3.up), Vector3.up);
            if (Mathf.Abs(yaw) > 170f)
                yaw = Mathf.Abs(yaw) * halfTurnSign;
            return Quaternion.Slerp(Quaternion.AngleAxis(yaw * t, Vector3.up) * from, to, t);
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _bone = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            _head = _animator.GetBoneTransform(HumanBodyBones.Head);
            _shoulder = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var elbow = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _armLength = Vector3.Distance(_shoulder.position, elbow.position) + Vector3.Distance(elbow.position, _bone.position);
            _shoulderOffset = Quaternion.Inverse(BodyRotation) * (_shoulder.position - Root.position);
            if (_ik != null)
                _ik.weight = 0f;
        }

        private void OnDisable()
        {
            CompleteImmediately();
            _weight = 0f;
            if (_ik != null)
                _ik.weight = 0f;
        }

        // Before the animation: the IK target for this frame.
        private void Update()
        {
            if (_ik == null)
                return;

            var goal = default(Goal);
            if (_custom != null)
            {
                _customTime += Time.deltaTime;
                if (_customTime > 5f || !_custom.GetHandGoal(this, out goal))
                {
                    _custom = null;
                    goal.Weight = 0f;
                }
            }
            else if (_item != null)
            {
                _elapsed += Time.deltaTime;
                goal = MoveGoal();
            }
            else if (_held != null)
            {
                goal = CarryGoal(_held);
            }

            _weight = goal.Weight;
            _ik.weight = goal.Weight;
            if (goal.Weight <= 0f)
                return;
            _ik.data.target.SetPositionAndRotation(goal.Position, goal.Rotation);
            if (_ik.data.hint != null)
                _ik.data.hint.position = goal.ElbowHint;
        }

        // After the animation and the IK: things happen, the held item follows the real wrist.
        private void LateUpdate()
        {
            if (_item != null)
            {
                var (phase, s) = Current();
                if (!_lifted && phase > Phase.Reach)
                    Lift();
                if (!_landed && (phase > Phase.Lower || (_to.InHand && _elapsed >= Total)))
                    Land();
                if (_item != null && !_landed && _lifted)
                    PoseRiding(phase, s);
                if (_elapsed >= Total)
                    _item = null;
            }
            else if (_held != null && _custom == null)
            {
                _held.transform.SetPositionAndRotation(RidingPosition(_held, CarryRotationOf(_held)), CarryRotationOf(_held));
            }
        }

        private float Total => _phases.Count == 0 ? 0f : _phases[^1].end;

        private (Phase phase, float s) Current()
        {
            var start = 0f;
            foreach (var (phase, end) in _phases)
            {
                if (_elapsed < end)
                    return (phase, Span(_elapsed, start, end));
                start = end;
            }

            return _phases.Count > 0 ? (_phases[^1].phase, 1f) : (Phase.Release, 1f);
        }

        private void Lift()
        {
            _lifted = true;
            _item.BeginLift(Root);
        }

        private void Land()
        {
            _landed = true;
            if (_to.InHand)
            {
                _held = _item;
            }
            else
            {
                _item.SnapTo(_to);
                _held = null;
            }

            var arrived = _arrived;
            _arrived = null;
            arrived?.Invoke();
        }

        private Goal MoveGoal()
        {
            var (phase, s) = Current();
            var item = _item;
            var gripA = _from.InHand ? CarryWrist(item) : item.GripAt(_from.World);
            var gripB = _to.InHand ? CarryWrist(item) : item.GripAt(_to.World);
            Vector3 wrist;
            Quaternion rotation;
            float weight;
            switch (phase)
            {
                case Phase.Reach:
                    (wrist, rotation, weight) = (gripA.position, gripA.rotation, Mathf.Lerp(_startWeight, 1f, Ease(s)));
                    break;
                case Phase.Lift:
                    (wrist, rotation, weight) = (gripA.position + _from.Exit * Ease(s), gripA.rotation, 1f);
                    break;
                case Phase.Carry:
                {
                    var p = _from.InHand ? gripA.position : gripA.position + _from.Exit;
                    var q = _to.InHand ? gripB.position : gripB.position + _to.Exit;
                    var control = _from.BySide || _to.BySide
                        ? _head.position + BodyRotation * _sideSpot
                        : (p + q) * 0.5f + Vector3.up * 0.05f;
                    var e = Ease(s);
                    wrist = Bezier(p, control, q, e);
                    rotation = ItemRotation(Phase.Carry, s) * item.Grip.Rotation;
                    weight = Mathf.Lerp(_from.InHand ? item.CarryWeight : 1f, _to.InHand ? item.CarryWeight : 1f, e);
                    break;
                }
                case Phase.Lower:
                    (wrist, rotation, weight) = (Vector3.Lerp(gripB.position + _to.Exit, gripB.position, Ease(s)), gripB.rotation, 1f);
                    break;
                default:
                    (wrist, rotation, weight) = (gripB.position, gripB.rotation, 1f - Ease(s));
                    break;
            }

            return new Goal { Position = wrist, Rotation = rotation, ElbowHint = ElbowHintFor(wrist), Weight = weight };
        }

        private Goal CarryGoal(Holdable item)
        {
            var grip = CarryWrist(item);
            return new Goal { Position = grip.position, Rotation = grip.rotation, ElbowHint = ElbowHintFor(grip.position), Weight = item.CarryWeight };
        }

        /// <summary>The item's planned rotation in a phase (the hand follows it; the position follows the real wrist).</summary>
        private Quaternion ItemRotation(Phase phase, float s)
        {
            var a = _from.InHand ? CarryRotationOf(_item) : _from.World.rotation;
            var b = _to.InHand ? CarryRotationOf(_item) : _to.World.rotation;
            return phase switch
            {
                Phase.Reach or Phase.Lift => a,
                Phase.Carry => Turn(a, b, Ease(s), 1f),
                _ => b,
            };
        }

        private void PoseRiding(Phase phase, float s)
        {
            var rotation = ItemRotation(phase, s);
            var riding = new Pose(RidingPosition(_item, rotation), rotation);
            var pose = phase switch
            {
                // Out of reach (the floor): it hops into the hand / drops onto its rest.
                Phase.Lift => Lerp(_from.World, riding, Ease(s)),
                Phase.Lower => Lerp(riding, _to.World, Ease(s)),
                _ => riding,
            };
            _item.transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        /// <summary>Item position that puts its grip on the real wrist.</summary>
        private Vector3 RidingPosition(Holdable item, Quaternion rotation) => _bone.position - rotation * item.Grip.Position;

        private Pose CarryWrist(Holdable item)
        {
            var rotation = CarryRotationOf(item);
            return new Pose(_hips.position + BodyRotation * item.CarryHand, rotation * item.Grip.Rotation);
        }

        private Quaternion CarryRotationOf(Holdable item) => BodyRotation * item.CarryRotation;

        private static Pose Lerp(Pose a, Pose b, float t) =>
            new(Vector3.Lerp(a.position, b.position, t), Quaternion.Slerp(a.rotation, b.rotation, t));

        private static bool IsWorld(ItemRest rest) => !rest.InHand && !rest.BySide;
    }
}
