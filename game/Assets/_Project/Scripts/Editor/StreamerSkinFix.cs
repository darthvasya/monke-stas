using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Stasik.EditorTools
{
    /// <summary>
    /// Fixes the auto-rig's skin weights on the streamer's torso whenever the rigged model is imported. The rigging
    /// service tied the back to the upper arms (~0.5 right on the spine) and the upper back to the head, so every arm
    /// swing bent the shirt's stripes into a "V". Torso vertices - far enough from the arm bones, between the hips and
    /// the neck - get weights along the spine instead; around the arms, below the hips and on the head the original
    /// weights stay, with a smooth blend in between.
    /// </summary>
    public class StreamerSkinFix : AssetPostprocessor
    {
        private const string RiggedModel = "Assets/_Project/Art/Models/Characters/Streamer/Rigged/Streamer_Rigged.fbx";

        // Bone names of the auto-rig. The torso goes on the spine up to the chest; the neck bone stays off the shirt
        // (the idle nods the neck, which stretched the collar), the fur above the collar keeps its weights.
        private static readonly string[] SpineChain = { "Hips", "Spine02", "Spine01", "Spine" };
        private const string NeckBone = "neck";

        private static readonly string[][] ArmChains =
        {
            new[] { "LeftArm", "LeftForeArm", "LeftHand" },
            new[] { "RightArm", "RightForeArm", "RightHand" },
        };

        // Arm + sleeve radius around the arm bones in the bind pose (m). Vertices closer than ArmInner * radius belong
        // to the arm, farther than ArmOuter * radius to the torso.
        private const float ArmRadius = 0.075f;
        private const float ArmInner = 1.05f;
        private const float ArmOuter = 1.5f;
        private const int MaxBonesPerVertex = 4;

        public override uint GetVersion() => 4;

        private void OnPostprocessModel(GameObject root)
        {
            if (assetPath != RiggedModel)
                return;
            var smr = root.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null || smr.sharedMesh == null)
                return;

            var bones = smr.bones;
            var mesh = smr.sharedMesh;
            var bindposes = mesh.bindposes;
            var spine = new int[SpineChain.Length];
            for (var i = 0; i < spine.Length; i++)
                if ((spine[i] = BoneIndex(bones, SpineChain[i])) < 0)
                {
                    Debug.LogWarning($"[Stasik] Skin fix skipped: no bone {SpineChain[i]} in {assetPath}.");
                    return;
                }

            var neckBone = BoneIndex(bones, NeckBone);
            if (neckBone < 0)
            {
                Debug.LogWarning($"[Stasik] Skin fix skipped: no bone {NeckBone} in {assetPath}.");
                return;
            }

            var neck = Joint(bindposes, neckBone);

            // Arm bone polylines in mesh space, extended past the hand to the fingertips.
            var arms = new Vector3[ArmChains.Length][];
            for (var s = 0; s < arms.Length; s++)
            {
                arms[s] = new Vector3[4];
                for (var j = 0; j < 3; j++)
                {
                    var bone = BoneIndex(bones, ArmChains[s][j]);
                    if (bone < 0)
                    {
                        Debug.LogWarning($"[Stasik] Skin fix skipped: no bone {ArmChains[s][j]} in {assetPath}.");
                        return;
                    }

                    arms[s][j] = Joint(bindposes, bone);
                }

                arms[s][3] = arms[s][2] + (arms[s][2] - arms[s][1]) * 0.5f;
            }

            // Each spine bone has full weight at the middle of its segment; in between, weights blend linearly.
            var centers = new float[spine.Length];
            for (var i = 0; i < spine.Length; i++)
            {
                var y = Joint(bindposes, spine[i]).y;
                var next = i + 1 < spine.Length ? Joint(bindposes, spine[i + 1]).y : neck.y;
                centers[i] = (y + next) * 0.5f;
            }

            // Torso height range: fades in just below the hips joint, out around the neck joint - on the back higher, above
            // the collar (the shirt reaches ~4 cm above the neck joint there; with head weights it stretched in Idle).
            var hipsY = Joint(bindposes, spine[0]).y;

            var vertices = mesh.vertices;
            var perVertex = mesh.GetBonesPerVertex();
            var weights = mesh.GetAllBoneWeights();
            var newPerVertex = new NativeArray<byte>(vertices.Length, Allocator.Persistent);
            var newWeights = new List<BoneWeight1>(weights.Length);
            var acc = new float[bones.Length];
            var touched = new List<int>();
            var changed = 0;
            var w = 0;
            for (var v = 0; v < vertices.Length; v++)
            {
                int count = perVertex[v];
                var p = vertices[v];
                var back = Smooth(neck.z, neck.z - 0.05f, p.z);
                var torso = Smooth(hipsY - 0.06f, hipsY + 0.02f, p.y) *
                            (1f - Smooth(neck.y - 0.03f + 0.065f * back, neck.y + 0.02f + 0.065f * back, p.y));
                foreach (var arm in arms)
                    torso *= Smooth(ArmInner, ArmOuter, DistanceToPolyline(p, arm) / ArmRadius);

                if (torso < 1e-3f)
                {
                    newPerVertex[v] = (byte)count;
                    for (var k = 0; k < count; k++)
                        newWeights.Add(weights[w + k]);
                    w += count;
                    continue;
                }

                touched.Clear();
                for (var k = 0; k < count; k++)
                    Add(acc, touched, weights[w + k].boneIndex, weights[w + k].weight * (1f - torso));
                w += count;

                if (p.y <= centers[0])
                    Add(acc, touched, spine[0], torso);
                else if (p.y >= centers[centers.Length - 1])
                    Add(acc, touched, spine[spine.Length - 1], torso);
                else
                    for (var i = 0; i + 1 < centers.Length; i++)
                    {
                        if (p.y >= centers[i + 1])
                            continue;
                        var a = (p.y - centers[i]) / (centers[i + 1] - centers[i]);
                        Add(acc, touched, spine[i], torso * (1f - a));
                        Add(acc, touched, spine[i + 1], torso * a);
                        break;
                    }

                // Strongest bones first (required by SetBoneWeights), at most four, normalised.
                touched.Sort((x, y) => acc[y].CompareTo(acc[x]));
                var kept = Mathf.Min(touched.Count, MaxBonesPerVertex);
                var sum = 0f;
                for (var k = 0; k < kept; k++)
                    sum += acc[touched[k]];
                for (var k = 0; k < kept; k++)
                    newWeights.Add(new BoneWeight1 { boneIndex = touched[k], weight = acc[touched[k]] / sum });
                newPerVertex[v] = (byte)kept;
                foreach (var b in touched)
                    acc[b] = 0f;
                changed++;
            }

            var newWeightsArray = new NativeArray<BoneWeight1>(newWeights.ToArray(), Allocator.Persistent);
            mesh.SetBoneWeights(newPerVertex, newWeightsArray);
            newPerVertex.Dispose();
            newWeightsArray.Dispose();
            Debug.Log($"[Stasik] Skin fix: torso weights moved onto the spine for {changed} of {vertices.Length} vertices.");
        }

        private static void Add(float[] acc, List<int> touched, int bone, float weight)
        {
            if (weight <= 0f)
                return;
            if (acc[bone] == 0f)
                touched.Add(bone);
            acc[bone] += weight;
        }

        private static int BoneIndex(Transform[] bones, string name)
        {
            for (var i = 0; i < bones.Length; i++)
                if (bones[i] != null && bones[i].name == name)
                    return i;
            return -1;
        }

        private static Vector3 Joint(Matrix4x4[] bindposes, int bone) => bindposes[bone].inverse.MultiplyPoint3x4(Vector3.zero);

        private static float DistanceToPolyline(Vector3 p, Vector3[] points)
        {
            var best = float.MaxValue;
            for (var i = 0; i + 1 < points.Length; i++)
            {
                var a = points[i];
                var ab = points[i + 1] - a;
                var t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, (a + ab * t - p).magnitude);
            }

            return best;
        }

        private static float Smooth(float edge0, float edge1, float x)
        {
            var t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
