using Stasik.Characters;
using UnityEngine;

namespace Stasik.Player
{
    /// <summary>
    /// The cap-wearer's privilege: the right hand goes down to the crotch, scratches with feeling and comes back. Follows
    /// the hips, so he can do it walking or sitting.
    /// </summary>
    internal sealed class HandScratch : CharacterHand.IGoalSource
    {
        private const float ReachTime = 0.35f;
        private const float ScratchTime = 1.8f;
        private const float ReleaseTime = 0.35f;
        private const float PalmDepth = 0.07f;                            // from the wrist to the middle of the palm
        private static readonly Vector3 Crotch = new(0.01f, -0.1f, 0.12f); // the palm, from the hips bone (body frame)
        private static readonly Vector3 Fingers = new(-0.3f, -1f, 0.35f);  // down, a bit forward and inwards (body frame)

        private float _time;
        private bool _started;
        private Vector3 _from;
        private Quaternion _fromRotation;

        public bool GetHandGoal(CharacterHand hand, out CharacterHand.Goal goal)
        {
            goal = default;
            if (hand.Hips == null)
                return false;
            if (!_started)
            {
                _started = true;
                _from = hand.Bone.position;
                _fromRotation = hand.Bone.rotation;
            }

            _time += Time.deltaTime;
            var total = ReachTime + ScratchTime + ReleaseTime;
            if (_time >= total)
                return false;

            var body = hand.BodyRotation;
            var scratch = Mathf.Clamp01((_time - ReachTime) / ScratchTime);
            var vigour = Mathf.Sin(Mathf.PI * scratch); // eases in and out of the scratching
            var phase = _time * Mathf.PI * 2f * 5f;      // 5 strokes a second
            var wiggle = new Vector3(0.015f * Mathf.Sin(phase * 0.5f), 0.022f * Mathf.Sin(phase), 0f) * vigour;
            var fingers = body * Fingers.normalized;
            var rotation = Quaternion.AngleAxis(12f * vigour * Mathf.Sin(phase), body * Vector3.forward)
                           * Quaternion.LookRotation(body * Vector3.forward, fingers); // palm (-Z) towards him
            var palm = hand.Hips.position + body * (Crotch + wiggle);
            var wrist = palm - rotation * Vector3.up * PalmDepth;

            float weight;
            if (_time < ReachTime)
            {
                var s = Smooth(_time / ReachTime);
                wrist = Vector3.Lerp(_from, wrist, s);
                rotation = Quaternion.Slerp(_fromRotation, rotation, s);
                weight = s;
            }
            else
            {
                weight = 1f - Smooth((_time - ReachTime - ScratchTime) / ReleaseTime);
            }

            goal = new CharacterHand.Goal
            {
                Position = wrist,
                Rotation = rotation,
                ElbowHint = hand.ElbowHintFor(wrist),
                Weight = weight,
            };
            return true;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
