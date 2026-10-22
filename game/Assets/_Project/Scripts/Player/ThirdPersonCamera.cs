using UnityEngine;

namespace Stasik.Player
{
    /// <summary>
    /// Over-the-shoulder orbit camera (The Witcher style): mouse / right stick orbits around the character, the camera
    /// sits slightly to the right of the head, pulls in when a wall is behind it, scroll changes the distance.
    /// </summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private PlayerInput _input;
        [SerializeField] private float _pivotHeight = 1.05f;   // around the character's head (1.3 m monkey)
        [SerializeField] private float _shoulderOffset = 0.3f; // to the right
        [SerializeField] private float _distance = 1.9f;
        [SerializeField] private float _minDistance = 0.8f;
        [SerializeField] private float _maxDistance = 3.2f;
        [SerializeField] private float _zoomStep = 0.25f;          // metres per mouse-wheel notch
        [SerializeField] private float _mouseSensitivity = 0.12f;  // degrees per mouse pixel
        [SerializeField] private float _stickSensitivity = 160f;   // degrees per second
        [SerializeField] private float _minPitch = -35f;
        [SerializeField] private float _maxPitch = 65f;
        [SerializeField] private float _collisionRadius = 0.15f;
        [SerializeField] private LayerMask _collisionMask = ~0;
        [SerializeField] private float _followSharpness = 15f;

        private float _yaw;
        private float _pitch = 12f;
        private float _currentDistance;
        private Vector3 _pivot;

        /// <summary>Look input is ignored while the cursor is free (menus, dialogues).</summary>
        public bool Locked { get; set; }

        private void Start()
        {
            if (_target != null)
            {
                _yaw = _target.eulerAngles.y;
                _pivot = _target.position + Vector3.up * _pivotHeight;
            }

            _currentDistance = _distance;
            SetCursorLocked(true);
        }

        private void LateUpdate()
        {
            if (_target == null || _input == null)
                return;

            // The project uses only the new Input System: legacy UnityEngine.Input would throw here.
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                SetCursorLocked(false);
            if (!Cursor.visible && !Locked)
            {
                var look = _input.Look;
                var scale = _input.LookIsGamepad ? _stickSensitivity * Time.deltaTime : _mouseSensitivity;
                _yaw += look.x * scale;
                _pitch = Mathf.Clamp(_pitch - look.y * scale, _minPitch, _maxPitch);
                // Input System reports about ±1 per wheel notch (normalized scroll).
                _distance = Mathf.Clamp(_distance - Mathf.Clamp(_input.Zoom, -1f, 1f) * _zoomStep, _minDistance, _maxDistance);
            }
            else if (Cursor.visible && UnityEngine.InputSystem.Mouse.current != null &&
                     UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
            {
                SetCursorLocked(true);
            }

            var targetPivot = _target.position + Vector3.up * _pivotHeight;
            _pivot = Vector3.Lerp(_pivot, targetPivot, 1f - Mathf.Exp(-_followSharpness * Time.deltaTime));

            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            var side = rotation * Vector3.right;
            var back = rotation * Vector3.back;

            // The shoulder offset itself must not go into a wall: next to a wall the shoulder point would start
            // inside it, and a cast starting inside a collider ignores that collider - the camera would go through.
            var shoulder = _pivot + side * CastDistance(_pivot, side, _shoulderOffset);

            // Pull the camera in front of walls and furniture between the shoulder and the wanted position.
            var wanted = Mathf.Max(0.2f, CastDistance(shoulder, back, _distance));
            // Snap in fast (never through a wall), ease back out slowly.
            _currentDistance = wanted < _currentDistance
                ? wanted
                : Mathf.Lerp(_currentDistance, wanted, 1f - Mathf.Exp(-4f * Time.deltaTime));

            transform.SetPositionAndRotation(shoulder + back * _currentDistance, rotation);
        }

        /// <summary>
        /// How far a camera-sized sphere can move from <paramref name="origin"/> along <paramref name="direction"/>
        /// before touching scene geometry. The player's own colliders are ignored (all hits are checked, not just the
        /// first, so the player standing in the way does not hide a wall behind him).
        /// </summary>
        private float CastDistance(Vector3 origin, Vector3 direction, float maxDistance)
        {
            var hits = Physics.SphereCastAll(origin, _collisionRadius, direction, maxDistance, _collisionMask,
                QueryTriggerInteraction.Ignore);
            var nearest = maxDistance;
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(_target))
                    continue;
                // distance 0 = the sphere already overlaps this collider at the origin.
                nearest = Mathf.Min(nearest, hit.distance);
            }

            return nearest;
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
