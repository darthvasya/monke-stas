using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Stasik.EditorTools
{
    /// <summary>
    /// Measures the "natural" ground speed of an in-place locomotion clip: while a foot is planted it slides backwards
    /// relative to the body at exactly the speed the character should move over the floor. If the CharacterController
    /// moves at a different speed, the feet skate and the character looks like it floats.
    /// </summary>
    public static class GaitAnalyzer
    {
        /// <summary>Natural speed in m/s of <paramref name="clip"/> played on <paramref name="character"/>.</summary>
        public static float NaturalSpeed(GameObject character, AnimationClip clip, float sampleRate = 120f)
        {
            var animator = character.GetComponentInChildren<Animator>();
            var left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            var root = character.transform;
            var frames = Mathf.Max(8, Mathf.RoundToInt(clip.length * sampleRate));
            var dt = clip.length / frames;

            var lp = new Vector3[frames + 1];
            var rp = new Vector3[frames + 1];
            AnimationMode.StartAnimationMode();
            try
            {
                for (var i = 0; i <= frames; i++)
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(animator.gameObject, clip, i * dt);
                    AnimationMode.EndSampling();
                    lp[i] = root.InverseTransformPoint(left.position);
                    rp[i] = root.InverseTransformPoint(right.position);
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }

            var lMin = float.MaxValue;
            var rMin = float.MaxValue;
            for (var i = 0; i <= frames; i++)
            {
                lMin = Mathf.Min(lMin, lp[i].y);
                rMin = Mathf.Min(rMin, rp[i].y);
            }

            // Planted = the lower foot, within 2.5 cm of its lowest point. Frames with both feet up (run flight) skip.
            var speeds = new List<float>();
            for (var i = 1; i <= frames; i++)
            {
                var useLeft = lp[i].y - lMin < rp[i].y - rMin;
                var a = useLeft ? lp : rp;
                var min = useLeft ? lMin : rMin;
                if (a[i].y - min > 0.025f || a[i - 1].y - min > 0.025f)
                    continue;
                speeds.Add(-(a[i].z - a[i - 1].z) / dt);
            }

            if (speeds.Count == 0)
                return 0f;
            speeds.Sort();
            return speeds[speeds.Count / 2]; // median: robust to heel/toe roll frames
        }
    }
}
