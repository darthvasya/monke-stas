using System;
using Stasik.Characters;
using Stasik.Interaction;
using UnityEngine;

namespace Stasik.Player
{
    /// <summary>
    /// The hand on a door handle: reaches for it, opens or closes the door when it gets there, goes along with the handle
    /// for a moment (lets go at once when it's a pull and he steps out of the way) and drops back.
    /// </summary>
    internal sealed class HandOnDoor : CharacterHand.IGoalSource
    {
        private const float ReachTime = 0.3f;
        private const float FollowTime = 0.3f;
        private const float ReleaseTime = 0.3f;
        private const float LeverOut = 0.1f;   // the lever stands off the panel
        private const float PalmDepth = 0.07f; // from the wrist to the middle of the palm

        private readonly Door _door;
        private readonly Transform _root;
        private readonly Func<bool> _toggle; // toggles the door; true = a pull, he steps back and lets go

        private float _time;
        private float _side;      // which face of the panel he holds
        private float _releaseAt;
        private Vector3 _from;
        private Quaternion _fromRotation;
        private CharacterHand.Goal _last;
        private bool _started;
        private bool _toggled;

        public HandOnDoor(Door door, Transform root, Func<bool> toggle)
        {
            _door = door;
            _root = root;
            _toggle = toggle;
        }

        public bool GetHandGoal(CharacterHand hand, out CharacterHand.Goal goal)
        {
            goal = default;
            if (_door == null || _door.Handle == null)
                return false;
            if (!_started)
            {
                _started = true;
                _from = hand.Bone.position;
                _fromRotation = hand.Bone.rotation;
                _side = Mathf.Sign(Vector3.Dot(_root.position - _door.Handle.position, _door.Handle.forward));
                _releaseAt = ReachTime + FollowTime;
            }

            _time += Time.deltaTime;
            if (_time < ReachTime)
            {
                var s = Smooth(_time / ReachTime);
                var (position, rotation) = Grip();
                _last = Goal(hand, Vector3.Lerp(_from, position, s), Quaternion.Slerp(_fromRotation, rotation, s), s);
            }
            else if (_time < _releaseAt)
            {
                if (!_toggled)
                {
                    _toggled = true;
                    if (_toggle())
                        _releaseAt = ReachTime;
                }

                var (position, rotation) = Grip();
                _last = Goal(hand, position, rotation, 1f);
            }
            else if (_time < _releaseAt + ReleaseTime)
            {
                if (!_toggled)
                {
                    _toggled = true;
                    _toggle();
                }

                _last.Weight = 1f - Smooth((_time - _releaseAt) / ReleaseTime);
            }
            else
            {
                return false;
            }

            goal = _last;
            return true;
        }

        /// <summary>Wrist pose holding the lever on his face of the panel: palm down, fingers towards the door.</summary>
        private (Vector3 position, Quaternion rotation) Grip()
        {
            var handle = _door.Handle;
            var outward = handle.forward * _side;
            var fingers = Vector3.ProjectOnPlane(-outward, Vector3.up).normalized;
            // This rig's hand bone: fingers along +Y, palm towards -Z.
            var rotation = Quaternion.LookRotation(Vector3.up, fingers);
            var palm = handle.position + outward * LeverOut + Vector3.up * 0.03f;
            return (palm - fingers * PalmDepth, rotation);
        }

        private static CharacterHand.Goal Goal(CharacterHand hand, Vector3 position, Quaternion rotation, float weight) =>
            new()
            {
                Position = position,
                Rotation = rotation,
                ElbowHint = hand.ElbowHintFor(position),
                Weight = weight,
            };

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
