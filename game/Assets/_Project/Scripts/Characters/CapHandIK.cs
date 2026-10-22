using UnityEngine;

namespace Stasik.Characters
{
    /// <summary>
    /// Moves the right hand to the cap while HeadCap changes it (takes it from behind the back, puts it on, turns it
    /// round). Sits next to the Animator; the animator layer needs "IK Pass" on. HeadCap adds it when it is missing.
    /// IK places the wrist; the hand's turn is set on the bone afterwards (the humanoid IK goal frame of this rig's
    /// hand doesn't match its bone, the bone's own axes are clear: fingers along +Y, palm towards -Z).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class CapHandIK : MonoBehaviour
    {
        [SerializeField] private HeadCap _cap;
        [SerializeField, Range(0f, 1f)] private float _rotationWeight = 0.85f; // the wrist keeps some of the animation

        private Animator _animator;
        private Transform _hand;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _hand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (_cap == null)
                _cap = GetComponentInChildren<HeadCap>(true);
        }

        private void OnAnimatorIK(int layerIndex)
        {
            var goal = default(HeadCap.HandGoal);
            var weight = _cap != null && _cap.GetHandGoal(out goal) ? goal.Weight : 0f;
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
            _animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, weight);
            if (weight <= 0f)
                return;
            _animator.SetIKPosition(AvatarIKGoal.RightHand, goal.Position);
            _animator.SetIKHintPosition(AvatarIKHint.RightElbow, goal.ElbowHint);
        }

        // After the IK has written the arm: turn the hand onto the cap's peak.
        private void LateUpdate()
        {
            if (_hand != null && _cap != null && _cap.PeekHandGoal(out var goal))
                _hand.rotation = Quaternion.Slerp(_hand.rotation, goal.Rotation, goal.Weight * _rotationWeight);
        }
    }
}
