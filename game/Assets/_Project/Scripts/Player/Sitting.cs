using Stasik.Interaction;
using UnityEngine;

namespace Stasik.Player
{
    /// <summary>
    /// Sitting down on a seat and getting up again. There are no sit-down clips, so the root hops from where he stands
    /// onto the seat while the animator blends into the sitting pose, then sinks a little into the cushion; getting up
    /// is the same backwards. While seated the CharacterController is off (he sits inside the chair's collider) and
    /// walking is locked. Seated, only the calm stretch of the sitting clip plays, back and forth: the rest of it has him
    /// slump forward and chew his hand (through the table top, at the table).
    /// </summary>
    internal sealed class Sitting
    {
        private const string SitState = "SitIdle";
        private const string WalkState = "Locomotion";
        private const float SitTime = 0.75f;
        private const float StandTime = 0.6f;
        private const float Blend = 0.45f;  // s, into and out of the sitting pose
        private const float Hop = 0.1f;     // m, up over the seat's edge
        private const float Squish = 0.03f; // m, into the cushion at the end

        private enum Phase
        {
            SittingDown,
            Seated,
            StandingUp,
            Done,
        }

        private readonly Seat _seat;
        private readonly Transform _root;
        private readonly CharacterController _body;
        private readonly PlayerMovement _movement;
        private readonly Animator _animator;
        private readonly Pose _seated;
        private readonly Vector2 _calm;

        private float _calmTime;
        private float _calmDirection = 1f;
        private bool _calmStarted;

        private Phase _phase = Phase.Done;
        private Pose _from;
        private Pose _to;
        private float _time;

        /// <param name="hips">Where the sitting pose puts the hips, in the root's frame.</param>
        /// <param name="clearance">The hips bone above the seat's surface.</param>
        /// <param name="calm">The calm stretch of the sitting clip, s; negative = before its end (it loops).</param>
        public Sitting(Seat seat, Transform root, PlayerMovement movement, Animator animator, Vector3 hips,
            float clearance, Vector2 calm)
        {
            _calm = calm;
            _seat = seat;
            _root = root;
            _body = root.GetComponent<CharacterController>();
            _movement = movement;
            _animator = animator;
            var facing = seat.transform.rotation;
            _seated = new Pose(seat.transform.position + Vector3.up * clearance - facing * hips, facing);
        }

        public bool Seated => _phase == Phase.Seated;

        public bool TryStart()
        {
            if (!_seat.TryTake(this))
                return false;
            _movement.AddLock(this);
            _body.enabled = false;
            Begin(Phase.SittingDown, _seated, SitState);
            return true;
        }

        public void StandUp()
        {
            if (_phase != Phase.Seated)
                return;
            var stand = _seat.StandPoint != null ? _seat.StandPoint.position : _root.position + _root.forward * 0.5f;
            Begin(Phase.StandingUp, new Pose(stand, _root.rotation), WalkState);
        }

        /// <summary>Moves him along; false once he's back on his feet.</summary>
        public bool Tick(float deltaTime)
        {
            if (_phase == Phase.Seated)
            {
                PlayCalm(deltaTime);
                return true;
            }

            if (_phase == Phase.Done)
                return false;

            _time += deltaTime;
            var s = Mathf.Clamp01(_time / (_phase == Phase.SittingDown ? SitTime : StandTime));
            var e = s * s * (3f - 2f * s);
            var position = Vector3.Lerp(_from.position, _to.position, e) + Vector3.up * (Hop * Mathf.Sin(Mathf.PI * s));
            if (_phase == Phase.SittingDown && s > 0.75f)
                position -= Vector3.up * (Squish * Mathf.Sin(Mathf.PI * (s - 0.75f) / 0.25f));
            _root.SetPositionAndRotation(position, Quaternion.Slerp(_from.rotation, _to.rotation, e));
            if (s < 1f)
                return true;

            if (_phase == Phase.SittingDown)
            {
                _phase = Phase.Seated;
                return true;
            }

            Finish();
            return false;
        }

        /// <summary>On his feet at once (the player gets disabled, a cutscene takes over).</summary>
        public void Abort()
        {
            if (_phase == Phase.Done)
                return;
            if (_seat.StandPoint != null)
                _root.position = _seat.StandPoint.position;
            _animator.CrossFadeInFixedTime(WalkState, 0f);
            Finish();
        }

        /// <summary>Back and forth through the calm stretch, once the blend into the sitting pose is over.</summary>
        private void PlayCalm(float deltaTime)
        {
            if (_animator.IsInTransition(0))
                return;
            var state = _animator.GetCurrentAnimatorStateInfo(0);
            if (!state.IsName(SitState) || state.length <= 0f)
                return;
            if (!_calmStarted)
            {
                _calmStarted = true;
                var now = state.normalizedTime % 1f * state.length;
                if (now > state.length + _calm.x)
                    now -= state.length; // the stretch before the loop's end
                _calmTime = Mathf.Clamp(now, _calm.x, _calm.y);
            }

            _calmTime += deltaTime * _calmDirection;
            if (_calmTime > _calm.y || _calmTime < _calm.x)
            {
                _calmDirection = -_calmDirection;
                _calmTime = Mathf.Clamp(_calmTime, _calm.x, _calm.y);
            }

            var time = _calmTime >= 0f ? _calmTime : state.length + _calmTime;
            _animator.Play(SitState, 0, time / state.length);
        }

        private void Begin(Phase phase, Pose to, string state)
        {
            _phase = phase;
            _from = new Pose(_root.position, _root.rotation);
            _to = to;
            _time = 0f;
            _animator.CrossFadeInFixedTime(state, Blend);
        }

        private void Finish()
        {
            _phase = Phase.Done;
            _body.enabled = true;
            _movement.RemoveLock(this);
            _seat.Leave(this);
        }
    }
}
