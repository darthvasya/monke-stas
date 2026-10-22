using Stasik.Characters;
using UnityEngine;

namespace Stasik.Player
{
    /// <summary>
    /// The cap button (C / D-pad up) cycles the cap on the player's head: off -> peak forward -> peak backward -> off.
    /// The character does it by hand (HeadCap + CapHandIK).
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerCap : MonoBehaviour
    {
        [SerializeField] private HeadCap _cap;

        private PlayerInput _input;

        private void Awake()
        {
            _input = GetComponent<PlayerInput>();
            if (_cap == null)
                _cap = GetComponentInChildren<HeadCap>(true);
        }

        private void Update()
        {
            // One change at a time: a press while the hand is busy with the cap is ignored.
            if (_cap != null && _input.CapPressed && !_cap.IsChanging)
                _cap.Next();
        }
    }
}
