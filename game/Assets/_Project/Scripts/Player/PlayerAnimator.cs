using UnityEngine;

namespace Stasik.Player
{
    /// <summary>Feeds movement into the Animator: "Speed" in m/s drives the Idle / Walk / Run blend tree.</summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class PlayerAnimator : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");

        [SerializeField] private Animator _animator;
        [SerializeField] private float _damping = 0.1f;

        private PlayerMovement _movement;

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();
        }

        private void Update()
        {
            if (_animator != null)
                _animator.SetFloat(SpeedId, _movement.Speed, _damping, Time.deltaTime);
        }
    }
}
