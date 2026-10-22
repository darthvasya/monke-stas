using UnityEngine;
using UnityEngine.InputSystem;

namespace Stasik.Player
{
    /// <summary>
    /// Player controls (keyboard + mouse and gamepad). One place for bindings so other components only read values.
    /// </summary>
    public class PlayerInput : MonoBehaviour
    {
        private InputAction _move;
        private InputAction _look;
        private InputAction _sprint;
        private InputAction _interact;
        private InputAction _zoom;
        private InputAction _cap;

        public Vector2 Move => _move.ReadValue<Vector2>();
        public Vector2 Look => _look.ReadValue<Vector2>();
        public bool Sprint => _sprint.IsPressed();
        public bool InteractPressed => _interact.WasPressedThisFrame();
        public float Zoom => _zoom.ReadValue<Vector2>().y;
        public bool CapPressed => _cap.WasPressedThisFrame();

        /// <summary>Gamepad stick values are per-second, mouse delta is per-frame: the camera scales them differently.</summary>
        public bool LookIsGamepad => _look.activeControl?.device is Gamepad;

        private void Awake()
        {
            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick");

            _look = new InputAction("Look", InputActionType.Value);
            _look.AddBinding("<Mouse>/delta");
            _look.AddBinding("<Gamepad>/rightStick");

            _sprint = new InputAction("Sprint", InputActionType.Button);
            _sprint.AddBinding("<Keyboard>/leftShift");
            _sprint.AddBinding("<Gamepad>/leftStickPress");

            _interact = new InputAction("Interact", InputActionType.Button);
            _interact.AddBinding("<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonSouth");

            _zoom = new InputAction("Zoom", InputActionType.Value);
            _zoom.AddBinding("<Mouse>/scroll");

            _cap = new InputAction("Cap", InputActionType.Button);
            _cap.AddBinding("<Keyboard>/c");
            _cap.AddBinding("<Gamepad>/dpad/up");
        }

        private void OnEnable()
        {
            _move.Enable();
            _look.Enable();
            _sprint.Enable();
            _interact.Enable();
            _zoom.Enable();
            _cap.Enable();
        }

        private void OnDisable()
        {
            _move.Disable();
            _look.Disable();
            _sprint.Disable();
            _interact.Disable();
            _zoom.Disable();
            _cap.Disable();
        }
    }
}
