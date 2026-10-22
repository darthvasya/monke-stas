using System;
using System.Collections.Generic;
using UnityEngine;

namespace Stasik.Player
{
    /// <summary>
    /// Third-person movement like in The Witcher: input is relative to the camera, the character turns towards where
    /// it walks. CharacterController handles walls, furniture, steps and slopes.
    /// Can also walk up to a point and turn to face something by itself (going to take an item), through the same
    /// velocity code, so the walk animation matches.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(PlayerInput))]
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private Transform _camera;
        [SerializeField] private float _walkSpeed = 1.3f;
        [SerializeField] private float _runSpeed = 3.0f;
        [SerializeField] private float _acceleration = 8f;
        [SerializeField] private float _deceleration = 10f;
        [SerializeField] private float _turnSpeed = 720f; // degrees per second
        [SerializeField] private float _gravity = -20f;

        private const float ArriveDistance = 0.06f;
        private const float ApproachTimeout = 2.5f;
        private const float CancelInput = 0.3f;

        private readonly HashSet<object> _locks = new();
        private CharacterController _controller;
        private PlayerInput _input;
        private Vector3 _velocity;
        private float _verticalSpeed;
        private bool _locked;

        private bool _approaching;
        private Vector3 _approachTarget;
        private Vector3 _approachFacing;
        private Action<bool> _approachDone;
        private float _approachTime;
        private float _stallTime;
        private Vector3 _stallFrom;
        private Vector3 _faceDirection;
        private Action _faceDone;

        /// <summary>Current horizontal speed in m/s (for the animator).</summary>
        public float Speed { get; private set; }

        public float WalkSpeed => _walkSpeed;
        public float RunSpeed => _runSpeed;

        /// <summary>Movement can be frozen during dialogues, minigames, cutscenes (the setter) or by owners (locks).</summary>
        public bool Locked
        {
            get => _locked || _locks.Count > 0;
            set => _locked = value;
        }

        public bool IsApproaching => _approaching;

        /// <summary>Freezes player input until the same owner removes it (a lock from someone else stays).</summary>
        public void AddLock(object owner) => _locks.Add(owner);

        public void RemoveLock(object owner) => _locks.Remove(owner);

        /// <summary>
        /// Walks to <paramref name="stand"/> at walking speed, then turns to <paramref name="facing"/>. done(true) on
        /// arrival or when stuck (the caller checks again), done(false) when the player takes over or it's cancelled.
        /// </summary>
        public void Approach(Vector3 stand, Vector3 facing, Action<bool> done)
        {
            CancelApproach();
            _approaching = true;
            _approachTarget = stand;
            _approachFacing = facing;
            _approachDone = done;
            _approachTime = 0f;
            _stallTime = 0f;
            _stallFrom = transform.position;
        }

        /// <summary>Turns in place to <paramref name="direction"/> (also while locked), then calls done.</summary>
        public void Face(Vector3 direction, Action done)
        {
            direction = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (direction.sqrMagnitude < 1e-6f)
            {
                done?.Invoke();
                return;
            }

            _faceDirection = direction.normalized;
            _faceDone = done ?? (() => { });
        }

        public void CancelApproach()
        {
            _faceDone = null;
            if (!_approaching)
                return;
            _approaching = false;
            var done = _approachDone;
            _approachDone = null;
            done?.Invoke(false);
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInput>();
            if (_camera == null && Camera.main != null)
                _camera = Camera.main.transform;
        }

        private void OnDisable()
        {
            CancelApproach();
            _locks.Clear();
        }

        private void Update()
        {
            if (!_controller.enabled)
            {
                // Someone else moves him (sitting down on a chair).
                _velocity = Vector3.zero;
                Speed = 0f;
                return;
            }

            var input = Locked ? Vector2.zero : Vector2.ClampMagnitude(_input.Move, 1f);
            if ((_approaching || _faceDone != null) && input.magnitude > CancelInput)
                CancelApproach();

            var forward = _camera != null ? Vector3.ProjectOnPlane(_camera.forward, Vector3.up).normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            var wishDir = forward * input.y + right * input.x;
            var wishSpeed = (_input.Sprint ? _runSpeed : _walkSpeed) * input.magnitude;
            if (_approaching)
                (wishDir, wishSpeed) = ApproachStep();

            var target = wishDir.sqrMagnitude > 0.0001f ? wishDir.normalized * wishSpeed : Vector3.zero;
            var rate = target.sqrMagnitude > _velocity.sqrMagnitude ? _acceleration : _deceleration;
            _velocity = Vector3.MoveTowards(_velocity, target, rate * Time.deltaTime);

            if (_controller.isGrounded && _verticalSpeed < 0f)
                _verticalSpeed = -1f; // keep pressed to the floor
            _verticalSpeed += _gravity * Time.deltaTime;

            _controller.Move((_velocity + Vector3.up * _verticalSpeed) * Time.deltaTime);
            Speed = new Vector3(_controller.velocity.x, 0f, _controller.velocity.z).magnitude;

            if (wishDir.sqrMagnitude > 0.0001f)
            {
                var look = Quaternion.LookRotation(wishDir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _turnSpeed * Time.deltaTime);
            }
            else if (_faceDone != null)
            {
                TurnToFace();
            }
        }

        private (Vector3 dir, float speed) ApproachStep()
        {
            var to = Vector3.ProjectOnPlane(_approachTarget - transform.position, Vector3.up);
            var distance = to.magnitude;
            _approachTime += Time.deltaTime;
            _stallTime += Time.deltaTime;
            // Not on the first frames: a long first frame (a hitch) would count as "didn't move".
            if (_stallTime >= 0.3f && _approachTime > 0.5f)
            {
                var stalled = Vector3.Distance(transform.position, _stallFrom) < 0.01f;
                _stallTime = 0f;
                _stallFrom = transform.position;
                if (stalled)
                    distance = 0f; // stuck on furniture: as close as it gets
            }

            if (distance > ArriveDistance && _approachTime < ApproachTimeout)
                return (to / distance, _walkSpeed * Mathf.Clamp01(distance / 0.3f + 0.25f));

            _approaching = false;
            var done = _approachDone;
            _approachDone = null;
            Face(_approachFacing, () => done?.Invoke(true));
            return (Vector3.zero, 0f);
        }

        private void TurnToFace()
        {
            var look = Quaternion.LookRotation(_faceDirection, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _turnSpeed * 0.6f * Time.deltaTime);
            if (Quaternion.Angle(transform.rotation, look) > 3f)
                return;
            var done = _faceDone;
            _faceDone = null;
            done();
        }
    }
}
