using System.Collections.Generic;
using System.IO;
using Stasik.Characters;
using Stasik.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Stasik.EditorTools
{
    /// <summary>
    /// Builds the playable streamer: the rigged model (Meshy auto-rig of our fitted + baked model) with the baked
    /// material and fur, an Animator (Idle/Walk/Run blend + action states), CharacterController and player scripts.
    /// Menu: Stasik/Characters/Build Player Prefab. ApartmentSceneBuilder places the prefab and the Witcher-style camera.
    ///
    /// Animation FBX files from the rigging service carry the whole mesh (~30 MB each): put them into
    /// Art/Animations/Streamer/_Import, run the builder, it extracts the clips into .anim files and deletes the FBXs.
    /// </summary>
    public static class PlayerCharacterBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Characters/Player.prefab";

        private const string RiggedModel = "Assets/_Project/Art/Models/Characters/Streamer/Rigged/Streamer_Rigged.fbx";
        private const string BakedMaterial = "Assets/_Project/Art/Models/Characters/Streamer/Baked/M_Streamer.mat";
        private const string FurMaterial = "Assets/_Project/Art/Materials/M_Fur_Streamer.mat";
        private const string AnimDir = "Assets/_Project/Art/Animations/Streamer";
        private const string ImportDir = AnimDir + "/_Import";
        private const string ControllerPath = AnimDir + "/Streamer.controller";
        private const float Height = 1.3f;

        // Clips can play a bit faster than recorded; movement speed follows, so the feet still don't skate.
        private const float WalkPlayback = 1.15f;
        private const float RunPlayback = 1f;

        // Clip name, loops. Locomotion uses Idle/Walk/Run; the rest are states played by name (Animator.CrossFade).
        private static readonly (string Name, bool Loop)[] Clips =
        {
            ("Idle", true), ("Walk", true), ("Run", true), ("Interact", false), ("SitIdle", true),
            ("Talk", true), ("TalkAngry", true), ("Sleep", true), ("Dance", true), ("Cheer", false),
        };

        // Humanoid limits in degrees, (twist, front-back, down-up) per bone; defaults are arm down-up -60..100,
        // shoulder front-back ±15.
        private static readonly Dictionary<string, (Vector3 min, Vector3 max)> ArmLimits = new()
        {
            ["LeftUpperArm"] = (new Vector3(-90f, -100f, -100f), new Vector3(90f, 100f, 100f)),
            ["RightUpperArm"] = (new Vector3(-90f, -100f, -100f), new Vector3(90f, 100f, 100f)),
            ["LeftShoulder"] = (new Vector3(0f, -30f, -15f), new Vector3(0f, 30f, 30f)),
            ["RightShoulder"] = (new Vector3(0f, -30f, -15f), new Vector3(0f, 30f, 30f)),
        };

        [MenuItem("Stasik/Characters/Build Player Prefab")]
        private static void BuildMenu() => Debug.Log("[Stasik] " + Build());

        public static string Build()
        {
            // Deleting and recreating the controller while a game runs would strip the live Animator.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "Stop Play mode before building the player.";

            var log = new System.Text.StringBuilder();
            var avatar = SetupRiggedModel(log);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                return log.Append("Avatar is not a valid humanoid: nothing changed.").ToString();
            ExtractClips(avatar, log);
            HoldShoulders(avatar, log);
            ApplyRootSettings(log);
            var controller = BuildController(log);
            BuildPrefab(controller, log);
            return log.ToString();
        }

        // ------------------------------------------------------------------ model

        private static Avatar SetupRiggedModel(System.Text.StringBuilder log)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(RiggedModel);
            if (importer.animationType != ModelImporterAnimationType.Human || importer.importAnimation)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.SaveAndReimport();
            }

            // A monkey's long arms hang straight down, below the default humanoid "Arm Down-Up" limit (-60°), and
            // HoldShoulders moves the shoulders' angle into the arms. Clips must be extracted again after changing the
            // limits (they are encoded against them).
            var hd = importer.humanDescription;
            var human = hd.human;
            var changed = false;
            for (var i = 0; i < human.Length; i++)
            {
                if (!ArmLimits.TryGetValue(human[i].humanName, out var lim))
                    continue;
                var current = human[i].limit;
                if (!current.useDefaultValues && current.min == lim.min && current.max == lim.max)
                    continue;
                human[i].limit = new HumanLimit { useDefaultValues = false, min = lim.min, max = lim.max, center = Vector3.zero };
                changed = true;
            }

            if (changed)
            {
                hd.human = human;
                importer.humanDescription = hd;
                importer.SaveAndReimport();
                log.AppendLine("Avatar: widened arm/shoulder limits (re-extract the clips).");
            }

            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(RiggedModel);
            log.AppendLine($"Rigged model: avatar valid={avatar != null && avatar.isValid}, human={avatar != null && avatar.isHuman}");
            return avatar;
        }

        // ------------------------------------------------------------------ clips

        private static void ExtractClips(Avatar avatar, System.Text.StringBuilder log)
        {
            if (!AssetDatabase.IsValidFolder(ImportDir))
            {
                log.AppendLine("No _Import folder: using existing .anim clips.");
                return;
            }

            foreach (var (name, loop) in Clips)
            {
                var fbx = $"{ImportDir}/{name}.fbx";
                if (!(AssetImporter.GetAtPath(fbx) is ModelImporter importer))
                {
                    log.AppendLine($"  missing {fbx}");
                    continue;
                }

                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = avatar;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                var clips = importer.defaultClipAnimations;
                for (var i = 0; i < clips.Length; i++)
                {
                    clips[i].name = name;
                    clips[i].loopTime = loop;
                }

                importer.clipAnimations = clips;
                importer.SaveAndReimport();

                AnimationClip source = null;
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbx))
                    if (a is AnimationClip c && !c.name.StartsWith("__preview"))
                        source = c;
                if (source == null)
                {
                    log.AppendLine($"  no clip in {fbx}");
                    continue;
                }

                var copy = Object.Instantiate(source);
                copy.name = name;
                var settings = AnimationUtility.GetAnimationClipSettings(source);
                settings.loopTime = loop;
                AnimationUtility.SetAnimationClipSettings(copy, settings);
                var animPath = $"{AnimDir}/{name}.anim";
                AssetDatabase.DeleteAsset(animPath);
                AssetDatabase.CreateAsset(copy, animPath);
                AssetDatabase.DeleteAsset(fbx); // only now that the clip is safely extracted
                log.AppendLine($"  {name}: {copy.length:F2}s, loop={loop}");
            }

            AssetDatabase.SaveAssets();
            var left = AssetDatabase.FindAssets("", new[] { ImportDir });
            if (left.Length == 0)
                AssetDatabase.DeleteAsset(ImportDir);
            else
                log.AppendLine($"Kept {left.Length} file(s) in {ImportDir} that were not extracted.");
            log.AppendLine("Extracted clips.");
        }

        /// <summary>
        /// Root settings of every clip (stored in the .anim, so they apply to already extracted clips too): the
        /// CharacterController moves the player, so clips stay in place, and the body height is measured from the
        /// feet - the lowest foot stays on the root, nobody floats or sinks whatever the clip's own hip height.
        /// </summary>
        private static void ApplyRootSettings(System.Text.StringBuilder log)
        {
            foreach (var (name, loop) in Clips)
            {
                var clip = Clip(name);
                if (clip == null)
                    continue;
                var s = AnimationUtility.GetAnimationClipSettings(clip);
                s.loopTime = loop;
                s.loopBlend = loop;
                s.loopBlendOrientation = true;
                s.loopBlendPositionY = true;
                s.loopBlendPositionXZ = true;
                s.keepOriginalOrientation = true;
                s.keepOriginalPositionY = false;
                s.heightFromFeet = true;
                s.keepOriginalPositionXZ = true;
                AnimationUtility.SetAnimationClipSettings(clip, s);
                EditorUtility.SetDirty(clip);
            }

            AssetDatabase.SaveAssets();
            log.AppendLine("Clip root settings: in place, height from feet.");
        }

        private static readonly string[] Sides = { "Left", "Right" };

        private static readonly HumanBodyBones[][] ShoulderAndArm =
        {
            new[] { HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm },
            new[] { HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm },
        };

        private static readonly string[] ShoulderMuscles = { "Shoulder Down-Up", "Shoulder Front-Back" };

        // Muscles of the shoulder and everything below it; HoldShoulders encodes them again.
        private static readonly string[] ArmMuscles =
        {
            "Shoulder Down-Up", "Shoulder Front-Back", "Arm Down-Up", "Arm Front-Back", "Arm Twist In-Out",
            "Forearm Stretch", "Forearm Twist In-Out", "Hand Down-Up", "Hand In-Out",
        };

        private static readonly string[] BodyCurves = { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };

        /// <summary>
        /// The rigging service's clips move the shoulders a lot: Idle shrugs them ~20° (upper arms at neck height),
        /// Walk pulls them down. The monkey's shoulders stay where they are modelled: frame by frame the shoulder bones
        /// go back to their rest rotation while the upper arms keep their world rotation, and Unity encodes the pose
        /// again - the arm muscles and the body too (Unity measures the body frame from the shoulders, so keeping the
        /// old body values would tilt the whole torso). Running it again changes nothing.
        /// </summary>
        private static void HoldShoulders(Avatar avatar, System.Text.StringBuilder log)
        {
            var rig = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RiggedModel));
            var animator = rig.GetComponent<Animator>();
            if (animator == null)
                animator = rig.AddComponent<Animator>();
            animator.avatar = avatar;
            var handler = new HumanPoseHandler(avatar, rig.transform);
            var count = 0;
            try
            {
                // Rest rotation = shoulder muscles at 0 (it depends on no other muscle).
                var pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount], bodyRotation = Quaternion.identity };
                handler.SetHumanPose(ref pose);
                var rest = new Quaternion[2];
                for (var s = 0; s < 2; s++)
                    rest[s] = animator.GetBoneTransform(ShoulderAndArm[s][0]).localRotation;

                foreach (var (name, _) in Clips)
                {
                    var clip = Clip(name);
                    if (clip == null || !HoldShoulders(clip, animator, handler, rest))
                        continue;
                    EditorUtility.SetDirty(clip);
                    count++;
                }
            }
            finally
            {
                handler.Dispose();
                Object.DestroyImmediate(rig);
            }

            AssetDatabase.SaveAssets();
            log.AppendLine($"Shoulders: held still in {count} clip(s).");
        }

        private static bool HoldShoulders(AnimationClip clip, Animator animator, HumanPoseHandler handler, Quaternion[] rest)
        {
            var names = HumanTrait.MuscleName;
            var muscleCurves = new AnimationCurve[names.Length];
            for (var i = 0; i < names.Length; i++)
                muscleCurves[i] = AnimationUtility.GetEditorCurve(clip, AnimatorCurve(names[i]));
            var bodyCurves = new AnimationCurve[BodyCurves.Length];
            for (var i = 0; i < BodyCurves.Length; i++)
                if ((bodyCurves[i] = AnimationUtility.GetEditorCurve(clip, AnimatorCurve(BodyCurves[i]))) == null)
                    return false;

            var moving = false;
            var rewritten = new List<int>();
            foreach (var side in Sides)
            {
                foreach (var muscle in ShoulderMuscles)
                {
                    var curve = muscleCurves[System.Array.IndexOf(names, $"{side} {muscle}")];
                    if (curve != null)
                        foreach (var key in curve.keys)
                            moving |= Mathf.Abs(key.value) > 1e-4f;
                }

                foreach (var muscle in ArmMuscles)
                    rewritten.Add(System.Array.IndexOf(names, $"{side} {muscle}"));
            }

            if (!moving)
                return false;

            var frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * clip.frameRate));
            var times = new float[frames + 1];
            var values = new float[rewritten.Count + BodyCurves.Length][];
            for (var k = 0; k < values.Length; k++)
                values[k] = new float[frames + 1];
            var pose = new HumanPose { muscles = new float[names.Length] };
            for (var f = 0; f <= frames; f++)
            {
                var t = times[f] = clip.length * f / frames;
                for (var i = 0; i < names.Length; i++)
                    pose.muscles[i] = muscleCurves[i]?.Evaluate(t) ?? 0f;
                pose.bodyPosition = new Vector3(bodyCurves[0].Evaluate(t), bodyCurves[1].Evaluate(t), bodyCurves[2].Evaluate(t));
                var bodyRotation = new Quaternion(bodyCurves[3].Evaluate(t), bodyCurves[4].Evaluate(t),
                    bodyCurves[5].Evaluate(t), bodyCurves[6].Evaluate(t)).normalized;
                pose.bodyRotation = bodyRotation;
                handler.SetHumanPose(ref pose);

                for (var s = 0; s < 2; s++)
                {
                    var upperArm = animator.GetBoneTransform(ShoulderAndArm[s][1]);
                    var keep = upperArm.rotation;
                    animator.GetBoneTransform(ShoulderAndArm[s][0]).localRotation = rest[s];
                    upperArm.rotation = keep;
                }

                handler.GetHumanPose(ref pose);
                for (var k = 0; k < rewritten.Count; k++)
                    values[k][f] = pose.muscles[rewritten[k]];
                var q = pose.bodyRotation;
                if (Quaternion.Dot(q, bodyRotation) < 0f)
                    q = new Quaternion(-q.x, -q.y, -q.z, -q.w); // same rotation, same hemisphere as the curve
                var body = new[] { pose.bodyPosition.x, pose.bodyPosition.y, pose.bodyPosition.z, q.x, q.y, q.z, q.w };
                for (var k = 0; k < body.Length; k++)
                    values[rewritten.Count + k][f] = body[k];
            }

            for (var k = 0; k < rewritten.Count; k++)
                AnimationUtility.SetEditorCurve(clip, AnimatorCurve(names[rewritten[k]]), SampledCurve(times, values[k]));
            for (var k = 0; k < BodyCurves.Length; k++)
                AnimationUtility.SetEditorCurve(clip, AnimatorCurve(BodyCurves[k]), SampledCurve(times, values[rewritten.Count + k]));
            return true;
        }

        /// <summary>Curve through the samples with finite-difference tangents.</summary>
        private static AnimationCurve SampledCurve(float[] times, float[] values)
        {
            var keys = new Keyframe[times.Length];
            for (var i = 0; i < times.Length; i++)
            {
                var prev = Mathf.Max(0, i - 1);
                var next = Mathf.Min(times.Length - 1, i + 1);
                var slope = next > prev ? (values[next] - values[prev]) / (times[next] - times[prev]) : 0f;
                keys[i] = new Keyframe(times[i], values[i], slope, slope);
            }

            return new AnimationCurve(keys);
        }

        private static EditorCurveBinding AnimatorCurve(string property) => EditorCurveBinding.FloatCurve("", typeof(Animator), property);

        private static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AnimDir}/{name}.anim");

        // ------------------------------------------------------------------ animator

        private static AnimatorController BuildController(System.Text.StringBuilder log)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var sm = controller.layers[0].stateMachine;

            var locomotion = controller.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            // Placeholder thresholds; BuildPrefab measures the clips and sets them = PlayerMovement speeds.
            tree.AddChild(Clip("Idle"), 0f);
            tree.AddChild(Clip("Walk"), 1f);
            tree.AddChild(Clip("Run"), 2.5f);
            sm.defaultState = locomotion;

            foreach (var (name, loop) in Clips)
            {
                if (name == "Idle" || name == "Walk" || name == "Run")
                    continue;
                var state = sm.AddState(name);
                state.motion = Clip(name);
                if (!loop)
                {
                    var back = state.AddTransition(locomotion);
                    back.hasExitTime = true;
                    back.exitTime = 0.9f;
                    back.duration = 0.2f;
                }
            }

            AssetDatabase.SaveAssets();
            log.AppendLine($"Animator: {ControllerPath}");
            return controller;
        }

        // ------------------------------------------------------------------ prefab

        private static void BuildPrefab(AnimatorController controller, System.Text.StringBuilder log)
        {
            var root = new GameObject("Player");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RiggedModel));
            model.name = "Model";
            model.transform.SetParent(root.transform, false);

            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            smr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(BakedMaterial);
            smr.updateWhenOffscreen = true;

            // Face +Z (Unity forward), stand on the floor, Height tall - measured on the skeleton, not loose bounds.
            var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var front = FindBone(model.transform, "headfront");
            var headBone = FindBone(model.transform, "Head");
            if (front != null && headBone != null && front.position.z < headBone.position.z)
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            // Height from the real skinned vertices (SkinnedMeshRenderer.bounds is loose and would shrink him).
            var (minY, maxY) = MeshHeightRange(smr);
            var scale = Height / (maxY - minY);
            model.transform.localScale *= scale;

            // Stand the model on the floor in the pose it is actually shown in (Idle with height-from-feet clips),
            // not in the bind pose: the soles touch the root.
            var idleBottom = AnimatedMeshBottom(animator, smr, 0f);
            model.transform.localPosition -= new Vector3(0f, idleBottom - root.transform.position.y, 0f);
            log.AppendLine($"Feet: idle bottom moved by {-idleBottom:F3} m");

            // Locomotion speeds come from the clips themselves, so the feet never skate (= no "floating").
            var walkSpeed = GaitAnalyzer.NaturalSpeed(root, Clip("Walk")) * WalkPlayback;
            var runSpeed = GaitAnalyzer.NaturalSpeed(root, Clip("Run")) * RunPlayback;
            SetLocomotionThresholds(controller, walkSpeed, runSpeed);
            log.AppendLine($"Locomotion: walk {walkSpeed:F2} m/s (clip x{WalkPlayback}), run {runSpeed:F2} m/s (clip x{RunPlayback})");

            // Radius covers the shoulders and hanging arms (they reach ~0.39 m sideways in Idle), so arms don't go into
            // furniture; doors are 0.9 m wide. The capsule bottom sits at the root once the skin width is counted.
            var cc = root.AddComponent<CharacterController>();
            cc.radius = 0.34f;
            cc.height = 1.25f;
            cc.skinWidth = 0.02f;
            cc.center = new Vector3(0f, cc.height * 0.5f + cc.skinWidth, 0f);
            cc.stepOffset = 0.25f;
            root.AddComponent<Stasik.Player.PlayerInput>();
            var movement = root.AddComponent<PlayerMovement>();
            var moveSo = new SerializedObject(movement);
            moveSo.FindProperty("_walkSpeed").floatValue = walkSpeed;
            moveSo.FindProperty("_runSpeed").floatValue = runSpeed;
            moveSo.ApplyModifiedPropertiesWithoutUndo();
            var anim = root.AddComponent<PlayerAnimator>();
            var animSo = new SerializedObject(anim);
            animSo.FindProperty("_animator").objectReferenceValue = animator;
            animSo.ApplyModifiedPropertiesWithoutUndo();

            var fur = root.AddComponent<FurShells>();
            var furSo = new SerializedObject(fur);
            furSo.FindProperty("_source").objectReferenceValue = smr;
            furSo.FindProperty("_furMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(FurMaterial);
            furSo.FindProperty("_shellCount").intValue = 24;
            furSo.ApplyModifiedPropertiesWithoutUndo();
            fur.enabled = false; // shells are runtime children: keep them out of the saved prefab

            // Hands: the right arm is an Animation Rigging Two Bone IK moved by CharacterHand; PlayerInteract is E / C.
            var hand = AddHand(root, animator);
            var interactSo = new SerializedObject(root.AddComponent<PlayerInteract>());
            interactSo.FindProperty("_hand").objectReferenceValue = hand;
            interactSo.ApplyModifiedPropertiesWithoutUndo();

            // The cap is an item of its own (it lives on the coat rack), fitted to this head in the bind pose.
            log.AppendLine(CapBuilder.BuildCapPrefab(root));

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            var prefabFur = new SerializedObject(prefab.GetComponent<FurShells>());
            prefabFur.FindProperty("m_Enabled").boolValue = true;
            prefabFur.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            log.AppendLine($"Prefab: {PrefabPath} (model scale {scale:F3})");
        }

        /// <summary>
        /// The right hand: an Animation Rigging rig on the model with a Two Bone IK on the right arm. CharacterHand moves
        /// its target (wrist position + hand bone rotation) and elbow hint and sets its weight (0 = the animation).
        /// </summary>
        private static CharacterHand AddHand(GameObject root, Animator animator)
        {
            var model = animator.gameObject;
            var upper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var wrist = animator.GetBoneTransform(HumanBodyBones.RightHand);

            var rigGo = new GameObject("HandRig");
            rigGo.transform.SetParent(model.transform, false);
            var rig = rigGo.AddComponent<Rig>();
            var ikGo = new GameObject("RightArmIK");
            ikGo.transform.SetParent(rigGo.transform, false);
            var target = new GameObject("RightHandTarget").transform;
            target.SetParent(ikGo.transform, false);
            target.SetPositionAndRotation(wrist.position, wrist.rotation);
            var hint = new GameObject("RightElbowHint").transform;
            hint.SetParent(ikGo.transform, false);
            hint.position = lower.position + root.transform.right * 0.3f;

            var ik = ikGo.AddComponent<TwoBoneIKConstraint>();
            ik.data.root = upper;
            ik.data.mid = lower;
            ik.data.tip = wrist;
            ik.data.target = target;
            ik.data.hint = hint;
            ik.data.targetPositionWeight = 1f;
            ik.data.targetRotationWeight = 1f;
            ik.data.hintWeight = 1f;
            ik.weight = 0f;
            model.AddComponent<RigBuilder>().layers.Add(new RigLayer(rig));

            var hand = model.AddComponent<CharacterHand>();
            var so = new SerializedObject(hand);
            so.FindProperty("_ik").objectReferenceValue = ik;
            so.FindProperty("_holdRoot").objectReferenceValue = root.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            return hand;
        }

        /// <summary>Blend thresholds = movement speeds; each clip plays at its playback multiplier.</summary>
        private static void SetLocomotionThresholds(AnimatorController controller, float walkSpeed, float runSpeed)
        {
            var tree = (BlendTree)controller.layers[0].stateMachine.defaultState.motion;
            var children = tree.children;
            for (var i = 0; i < children.Length; i++)
            {
                var name = children[i].motion != null ? children[i].motion.name : "";
                if (name == "Walk")
                {
                    children[i].threshold = walkSpeed;
                    children[i].timeScale = WalkPlayback;
                }
                else if (name == "Run")
                {
                    children[i].threshold = runSpeed;
                    children[i].timeScale = RunPlayback;
                }
            }

            tree.children = children;
            EditorUtility.SetDirty(tree);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        private static (float min, float max) MeshHeightRange(SkinnedMeshRenderer smr)
        {
            var mesh = new Mesh();
            smr.BakeMesh(mesh);
            var m = smr.transform.localToWorldMatrix;
            float min = float.MaxValue, max = float.MinValue;
            foreach (var v in mesh.vertices)
            {
                var y = m.MultiplyPoint3x4(v).y;
                min = Mathf.Min(min, y);
                max = Mathf.Max(max, y);
            }

            Object.DestroyImmediate(mesh);
            return (min, max);
        }

        /// <summary>Lowest point of the skinned mesh over a second of the locomotion blend at the given speed.</summary>
        private static float AnimatedMeshBottom(Animator animator, SkinnedMeshRenderer smr, float speed)
        {
            var mesh = new Mesh();
            var lowest = float.MaxValue;
            animator.Rebind();
            animator.SetFloat("Speed", speed);
            animator.Update(0f);
            for (var i = 0; i < 10; i++)
            {
                animator.Update(0.1f);
                smr.BakeMesh(mesh);
                var m = smr.transform.localToWorldMatrix;
                var v = mesh.vertices;
                for (var k = 0; k < v.Length; k += 3)
                    lowest = Mathf.Min(lowest, m.MultiplyPoint3x4(v[k]).y);
            }

            Object.DestroyImmediate(mesh);
            animator.Rebind();
            return lowest;
        }

        private static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t.name == name)
                    return t;
            return null;
        }
    }
}
