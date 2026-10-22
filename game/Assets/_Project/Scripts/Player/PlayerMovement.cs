using UnityEngine;

namespace Stasik.Player
{
    /// <summary>
    /// Third-person movement like in The Witcher: input is relative to the camera, the character turns towards where
    /// it walks. CharacterController handles walls, furniture, steps and slopes.
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

        private CharacterController _controller;
        private PlayerInput _input;
        private Vector3 _velocity;
        private float _verticalSpeed;

        /// <summary>Current horizontal speed in m/s (for the animator).</summary>
        public float Speed { get; private set; }

        public float WalkSpeed => _walkSpeed;
        public float RunSpeed => _runSpeed;

        /// <summary>Movement can be frozen during dialogues, minigames, cutscenes.</summary>
        public bool Locked { get; set; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInput>();
            if (_camera == null && Camera.main != null)
                _camera = Camera.main.transform;
        }

        private void Update()
        {
            var input = Locked ? Vector2.zero : Vector2.ClampMagnitude(_input.Move, 1f);
            var forward = _camera != null ? Vector3.ProjectOnPlane(_camera.forward, Vector3.up).normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            var wishDir = forward * input.y + right * input.x;
            var wishSpeed = (_input.Sprint ? _runSpeed : _walkSpeed) * input.magnitude;

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
        }
    }
}
