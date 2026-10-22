using System.Collections.Generic;
using System.IO;
using Stasik.Characters;
using Stasik.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Stasik.EditorTools
{
    /// <summary>
    /// The streamer's baseball cap (washed 6-panel dad cap: "nymf71" in front, "маленький / хуй" on the back).
    /// The mesh is generated to fit the character's head: an elliptic crown that encloses every head vertex above the
    /// band, a back opening with the strap, a curved brim and the top button. Saved as the item prefab
    /// Prefabs/Items/Cap.prefab (Holdable + HeadCap): it lives on the coat rack hook, the hand takes it, and worn it sits
    /// on the Head bone peak forward or peak backward (a second mesh, the cap turned round on the same band and fitted
    /// again), fur under it hidden. Fitted to the Player model's head on every PlayerCharacterBuilder run.
    ///
    /// The texture is drawn by tools/make_cap_texture.py from the dimensions this builder writes to cap_dims.json; the
    /// UV layout below and the script must agree:
    ///   crown outside: u = azimuth (front 0.25, back 0.75, seam on the right side; u grows towards the character's
    ///   left, so text drawn left to right reads correctly from outside), v = 0.30 + 0.70 * elevation/90°
    ///   brim top: u 0..0.5 across the brim, v = 0.02 + 0.24 * (0 at the crown .. 1 at the edge); brim bottom: u 0.5..1
    ///   inside, sweatband, brim edge: flat colours at fixed uv points.
    /// </summary>
    public static class CapBuilder
    {
        public const string Dir = "Assets/_Project/Art/Models/Characters/Streamer/Cap";
        public const string PrefabPath = "Assets/_Project/Prefabs/Items/Cap.prefab";
        private const string MeshPath = Dir + "/Cap.asset";
        private const string BackwardMeshPath = Dir + "/Cap_Backward.asset";
        private const string MaterialPath = Dir + "/M_Cap.mat";
        private const string TexturePath = Dir + "/Cap_Albedo.png";
        private const string DimsPath = Dir + "/cap_dims.json";

        /// <summary>How the cap sits on the head. The defaults are the approved fit; other values are for previews.</summary>
        public sealed class Fit
        {
            public float Tilt = 15f;              // degrees: band higher at the forehead than at the nape
            // Eyes at ~0.09 and the skull is widest at ~0.15: a band on the forehead makes the cap as big as the monkey
            // skull ("огромная"), so it sits higher, on the narrower top - a normal cap on a big head.
            public float BandHeight = 0.225f;     // m above the head bone in the tilted frame
            public float Clearance = 0.008f;      // m between skin and cap; the fur under a worn cap is hidden (HeadCap)
            public float CrownRatio = 0.82f;      // min crown height / band half-width: a cap sitting high stays a cap
            public float BrimLengthFactor = 0.6f; // of the crown's front depth
            public float BrimDroop = 10f;         // degrees down from the band plane
            public bool Backward;                 // peak to the back: same band, the cap turned round and fitted again
        }

        private const float Thickness = 0.004f;
        // Crown meridian is a superellipse (cos^P, sin^P): upright front panels and a flatter top, not a helmet dome.
        private const float Profile = 0.88f;
        private const float BrimHalfAngle = 1.0f;      // rad: the brim ends inside the crown's width, seen from the front
        private const float BrimSpread = 0.45f;        // 1 = sides grow along the crown normal, 0 = straight forward
        private const float BrimCurve = 0.9f;          // how much the sides of the brim bend down (pre-curved brim)

        // Back opening: azimuth half-width (rad) and heights above the band (m); the strap runs below StrapHeight.
        private const float OpeningHalfWidth = 0.36f;
        private const float StrapHeight = 0.022f;
        private const float OpeningArchStart = 0.03f;
        private const float OpeningHeight = 0.058f;

        private static readonly Vector2 InsideUv = new(0.75f, 0.14f);
        private static readonly Vector2 EdgeUv = new(0.25f, 0.27f);

        private const int Segments = 160;
        private const int Rings = 40;

        public const string TestScenePath = "Assets/_Project/Scenes/Sandbox/CapTest.unity";
        // Away from the apartment, so the test scene can also be opened additively next to it.
        private static readonly Vector3 TestSpot = new(0f, 0f, -40f);

        [MenuItem("Stasik/Characters/Cap Test Scene")]
        private static void TestSceneMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                Debug.Log("[Stasik] " + BuildTestScene(NewSceneMode.Single));
        }

        /// <summary>
        /// Scenes/Sandbox/CapTest: the player model in Idle wearing the cap, with a camera on the head. Refits the cap
        /// first (so a changed Fit shows up here), then puts a Cap.prefab instance on the mannequin peak forward.
        /// Additive mode leaves the open scenes alone: no light and the camera disabled.
        /// </summary>
        public static string BuildTestScene(NewSceneMode mode)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "Cap: stop Play mode first.";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCharacterBuilder.PrefabPath) == null)
                return "Cap: build the Player prefab first.";
            var result = RefitCap();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCharacterBuilder.PrefabPath);
            var capPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (capPrefab == null)
                return result;

            var open = SceneManager.GetSceneByPath(TestScenePath);
            if (open.IsValid() && mode == NewSceneMode.Additive)
                EditorSceneManager.CloseScene(open, true);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            player.transform.SetPositionAndRotation(TestSpot, Quaternion.identity);
            // A mannequin: in Play mode it only idles (no floor here, and it must not walk or act with the real player).
            var movement = player.GetComponent<Stasik.Player.PlayerMovement>();
            var input = player.GetComponent<Stasik.Player.PlayerInput>();
            var controller = player.GetComponent<CharacterController>();
            var interact = player.GetComponent<Stasik.Player.PlayerInteract>();
            movement.enabled = false;
            input.enabled = false;
            controller.enabled = false;
            interact.enabled = false;
            var model = player.GetComponentInChildren<Animator>().gameObject;
            var head = model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
            var cap = ((GameObject)PrefabUtility.InstantiatePrefab(capPrefab, scene)).GetComponent<HeadCap>();
            cap.Wear(head, CapStyle.Forward);
            // Set from code on prefab instances: record the overrides, or the scene reloads them as the prefabs have them.
            foreach (var component in new Component[]
                     {
                         movement, input, controller, interact, cap, cap.transform, cap.GetComponent<MeshFilter>(),
                         cap.GetComponent<MeshRenderer>(), cap.Holdable,
                     })
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);

            // Idle pose: in edit mode the animator doesn't run.
            foreach (var clip in model.GetComponent<Animator>().runtimeAnimatorController.animationClips)
                if (clip.name == "Idle")
                    clip.SampleAnimation(model, 0f);

            var camGo = new GameObject("CapCamera");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 15f;
            cam.nearClipPlane = 0.1f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.82f, 0.83f, 0.85f);
            cam.enabled = mode == NewSceneMode.Single;
            camGo.transform.position = head.position + player.transform.forward * 3.2f + Vector3.up * 0.25f;
            camGo.transform.LookAt(head.position + Vector3.up * 0.15f);

            if (mode == NewSceneMode.Single)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.6f;
                sun.color = new Color(1f, 0.96f, 0.9f);
                sun.transform.rotation = Quaternion.Euler(35f, 160f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.57f);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(TestScenePath));
            EditorSceneManager.SaveScene(scene, TestScenePath);
            return result + $" - test scene {TestScenePath}";
        }

        [MenuItem("Stasik/Characters/Refit Cap")]
        private static void Menu() => Debug.Log("[Stasik] " + RefitCap());

        /// <summary>
        /// Fits the cap to the Player prefab's head again and rewrites Cap.prefab (PlayerCharacterBuilder does it on
        /// every rebuild; this is for working on the cap alone). Removes a cap left on the player by older builds.
        /// </summary>
        public static string RefitCap()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "Cap: stop Play mode first.";
            var contents = PrefabUtility.LoadPrefabContents(PlayerCharacterBuilder.PrefabPath);
            try
            {
                var legacy = false;
                foreach (var t in contents.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name == "Cap")
                    {
                        Object.DestroyImmediate(t.gameObject);
                        legacy = true;
                    }

                var result = BuildCapPrefab(contents);
                if (legacy)
                    PrefabUtility.SaveAsPrefabAsset(contents, PlayerCharacterBuilder.PrefabPath);
                return result;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Fits both ways of wearing to the player's head (peak forward and peak backward, each fitted to the head),
        /// saves the meshes in place (their GUIDs stay), the material and cap_dims.json (for tools/make_cap_texture.py),
        /// and writes Prefabs/Items/Cap.prefab over the same path (the scene's instance keeps working). Adds nothing to
        /// the player.
        /// </summary>
        public static string BuildCapPrefab(GameObject player)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "Cap: stop Play mode first.";
            var model = player.GetComponentInChildren<Animator>().gameObject;
            var forward = FitCap(model, new Fit());
            var backward = FitCap(model, new Fit { Backward = true });
            if (forward == null || backward == null)
                return "Cap: head bones not found.";
            if (Mathf.Abs(forward.Head.lossyScale.x - 1f) > 0.01f)
                Debug.LogWarning("[Stasik] Cap: the head bone is scaled; the cap is fitted in world metres.");

            Directory.CreateDirectory(Dir);
            forward.Mesh = SaveMesh(forward.Mesh, MeshPath);
            backward.Mesh = SaveMesh(backward.Mesh, BackwardMeshPath);
            File.WriteAllText(DimsPath, DimsJson(forward.Radii));
            AssetDatabase.ImportAsset(DimsPath);

            var cap = new GameObject("Cap");
            cap.AddComponent<MeshFilter>();
            cap.AddComponent<MeshRenderer>().sharedMaterial = Material();
            // Carried at the right hip, peak forward and level, held by the peak.
            cap.AddComponent<Holdable>().SetCarry(new Vector3(0.2f, 0.12f, 0.22f), Vector3.zero, 0.85f);
            cap.AddComponent<HeadCap>().Setup(Placed(forward.Head, forward), Placed(forward.Head, backward), CapStyle.Off);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(cap, PrefabPath);
            Object.DestroyImmediate(cap);
            AssetDatabase.SaveAssets();

            var r = forward.Radii;
            var texture = File.Exists(TexturePath) ? "" : " - no texture yet: run tools/make_cap_texture.py";
            return $"Cap: radii {r.x:F3} x {r.y:F3} x {r.z:F3} m, forward and backward -> {PrefabPath}{texture}";
        }

        /// <summary>
        /// The "Cap" object on the Head bone (with HeadCap) for one fit and an in-memory mesh, no material; null without
        /// head bones. Works in any pose: the frame comes from the head bones.
        /// </summary>
        public static GameObject Build(GameObject model, Fit fit, out Vector3 radii)
        {
            radii = Vector3.zero;
            var fitted = FitCap(model, fit);
            if (fitted == null)
                return null;
            radii = fitted.Radii;
            return Create(fitted, fitted, fit.Backward ? CapStyle.Backward : CapStyle.Forward);
        }

        /// <summary>A cap mesh fitted to the head and where it sits (world space, in the model's current pose).</summary>
        private sealed class Fitted
        {
            public Transform Head;
            public Mesh Mesh;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Radii;
        }

        private static Fitted FitCap(GameObject model, Fit fit)
        {
            var animator = model.GetComponentInChildren<Animator>();
            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var headEnd = Find(model.transform, "head_end");
            var headFront = Find(model.transform, "headfront");
            if (head == null || headEnd == null || headFront == null)
                return null;

            // Head frame: up through the top of the skull, forward through the face; then tilted so the front of the
            // band is higher (caps sit on the forehead and low on the nape).
            var up = (headEnd.position - head.position).normalized;
            var fwd = Vector3.ProjectOnPlane(headFront.position - head.position, up).normalized;
            var right = Vector3.Cross(up, fwd);
            var tilt = Quaternion.AngleAxis(-fit.Tilt, right);
            up = tilt * up;
            fwd = tilt * fwd;
            // Backward: the same band, the cap turned round it - its front (the brim) now over the nape. The head
            // isn't the same front and back, so the crown is fitted again in this frame.
            if (fit.Backward)
            {
                fwd = -fwd;
                right = -right;
            }

            var origin = head.position + up * fit.BandHeight;
            var points = HeadPoints(smr, head, origin, right, up, fwd);
            var (centre, r) = FitCrown(points, fit);
            var rotation = Quaternion.LookRotation(fwd, up);
            return new Fitted
            {
                Head = head,
                Mesh = BuildMesh(r, EnvelopeScale(points, centre, r, fit.Clearance), fit, OpeningFor(r)),
                Position = origin + rotation * new Vector3(centre.x, 0f, centre.y),
                Rotation = rotation,
                Radii = r,
            };
        }

        private static GameObject Create(Fitted forward, Fitted backward, CapStyle style)
        {
            var head = forward.Head;
            var cap = new GameObject("Cap");
            cap.transform.SetParent(head, false);
            cap.AddComponent<MeshFilter>();
            cap.AddComponent<MeshRenderer>();
            cap.AddComponent<HeadCap>().Setup(Placed(head, forward), Placed(head, backward), style);
            return cap;
        }

        private static HeadCap.Placement Placed(Transform head, Fitted fitted) =>
            new(fitted.Mesh, head.InverseTransformPoint(fitted.Position),
                Quaternion.Inverse(head.rotation) * fitted.Rotation, fitted.Radii);

        /// <summary>Saves the mesh at the path; an existing asset is overwritten in place, so its GUID stays.</summary>
        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            EditorUtility.CopySerialized(mesh, existing);
            existing.name = Path.GetFileNameWithoutExtension(path);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        /// <summary>What tools/make_cap_texture.py needs to place the seams, the opening and the text.</summary>
        public static string DimsJson(Vector3 radii)
        {
            var o = OpeningFor(radii);
            return JsonUtility.ToJson(new Dims
            {
                rx = radii.x, ry = radii.y, rz = radii.z, profile = Profile, strapTop = o.StrapTop,
                openingHalfWidth = OpeningHalfWidth, openingArchStart = o.ArchStart, openingTop = o.Top,
            }, true);
        }

        [System.Serializable]
        private struct Dims
        {
            public float rx, ry, rz, profile, strapTop, openingHalfWidth, openingArchStart, openingTop;
        }

        /// <summary>Back opening elevations (rad) for this crown: the heights in metres stay the same on any size.</summary>
        private readonly struct Opening
        {
            public readonly float StrapTop, ArchStart, Top;

            public Opening(float strapTop, float archStart, float top)
            {
                StrapTop = strapTop;
                ArchStart = archStart;
                Top = top;
            }
        }

        private static Opening OpeningFor(Vector3 r) =>
            new(ElevationAt(r, StrapHeight), ElevationAt(r, OpeningArchStart), ElevationAt(r, OpeningHeight));

        /// <summary>Crown elevation parameter where the crown is <paramref name="height"/> m above the band.</summary>
        private static float ElevationAt(Vector3 r, float height) =>
            Mathf.Asin(Mathf.Pow(Mathf.Clamp01(height / r.y), 1f / Profile));

        // ------------------------------------------------------------------ fit

        /// <summary>Head-weighted vertices (bind pose) in the band frame: x right, y up from the band, z forward.</summary>
        private static List<Vector3> HeadPoints(SkinnedMeshRenderer smr, Transform head, Vector3 origin, Vector3 right,
            Vector3 up, Vector3 fwd)
        {
            var headIndex = System.Array.IndexOf(smr.bones, head);
            var mesh = new Mesh();
            smr.BakeMesh(mesh, true);
            var vertices = mesh.vertices;
            var weights = smr.sharedMesh.boneWeights;
            var toWorld = smr.transform.localToWorldMatrix;
            var result = new List<Vector3>();
            for (var i = 0; i < vertices.Length; i++)
            {
                var w = weights[i];
                var hw = (w.boneIndex0 == headIndex ? w.weight0 : 0f) + (w.boneIndex1 == headIndex ? w.weight1 : 0f) +
                         (w.boneIndex2 == headIndex ? w.weight2 : 0f) + (w.boneIndex3 == headIndex ? w.weight3 : 0f);
                if (hw < 0.6f)
                    continue;
                var d = toWorld.MultiplyPoint3x4(vertices[i]) - origin;
                result.Add(new Vector3(Vector3.Dot(d, right), Vector3.Dot(d, up), Vector3.Dot(d, fwd)));
            }

            Object.DestroyImmediate(mesh);
            return result;
        }

        /// <summary>
        /// Base half-ellipsoid: the band hugs the head's cross-section just above the band plane, the height reaches the
        /// top of the skull (at least CrownRatio of the band); all plus clearance. Where the head bulges out of it,
        /// <see cref="EnvelopeScale"/> pushes the crown out locally - a cap follows the head instead of floating around
        /// it like a helmet.
        /// </summary>
        private static (Vector2 centre, Vector3 radii) FitCrown(List<Vector3> points, Fit fit)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue, top = 0f;
            foreach (var p in points)
            {
                if (p.y < 0f)
                    continue;
                top = Mathf.Max(top, p.y);
                if (p.y > 0.04f)
                    continue;
                x0 = Mathf.Min(x0, p.x);
                x1 = Mathf.Max(x1, p.x);
                z0 = Mathf.Min(z0, p.z);
                z1 = Mathf.Max(z1, p.z);
            }

            var centre = new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
            var radii = new Vector3((x1 - x0) * 0.5f, top, (z1 - z0) * 0.5f) + Vector3.one * fit.Clearance;
            radii.y = Mathf.Max(radii.y, fit.CrownRatio * radii.x);
            return (centre, radii);
        }

        /// <summary>
        /// Per crown vertex (grid Segments x Rings) how much to scale the ellipsoid so every head point in that
        /// direction stays inside with clearance; smoothed so the crown stays a clean surface.
        /// </summary>
        private static float[,] EnvelopeScale(List<Vector3> points, Vector2 centre, Vector3 radii, float clearance)
        {
            var need = new float[Segments + 1, Rings + 1];
            for (var i = 0; i <= Segments; i++)
            for (var j = 0; j <= Rings; j++)
                need[i, j] = 1f;

            foreach (var p in points)
            {
                if (p.y < 0f)
                    continue;
                var q = new Vector3(p.x - centre.x, p.y, p.z - centre.y);
                // Direction in ellipsoid space -> grid cell; normalized radius with clearance added along the normal.
                var e = new Vector3(q.x / radii.x, q.y / radii.y, q.z / radii.z);
                var len = e.magnitude;
                if (len < 1e-4f)
                    continue;
                var dir = e / len;
                // The crown parameter whose surface point lies in this direction: tan(direction) = tan(a)^Profile.
                var direction = Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude);
                var a = Mathf.Atan(Mathf.Pow(Mathf.Tan(Mathf.Clamp(direction, 0f, 1.5707f)), 1f / Profile));
                var u = AzimuthU(Mathf.Atan2(dir.x, dir.z));
                var i = Mathf.RoundToInt(u * Segments);
                var j = Mathf.Clamp(Mathf.RoundToInt(a / (Mathf.PI * 0.5f) * Rings), 0, Rings);
                // The point pushed out by the clearance (radially from the crown centre) must be inside; the
                // superellipse reaches further than the ellipse in that direction.
                var pushed = q + q.normalized * clearance;
                var required = new Vector3(pushed.x / radii.x, pushed.y / radii.y, pushed.z / radii.z).magnitude;
                var surface = new Vector2(Meridian(Mathf.Cos(a)), Meridian(Mathf.Sin(a))).magnitude;
                need[i, j] = Mathf.Max(need[i, j], required / surface);
            }

            // Spread each requirement to its neighbours (max), then smooth (average) twice.
            var result = need;
            for (var pass = 0; pass < 3; pass++)
            {
                var next = new float[Segments + 1, Rings + 1];
                for (var i = 0; i <= Segments; i++)
                for (var j = 0; j <= Rings; j++)
                {
                    float sum = 0f, max = 0f;
                    var n = 0;
                    for (var di = -2; di <= 2; di++)
                    for (var dj = -2; dj <= 2; dj++)
                    {
                        var ii = (i + di + Segments) % Segments;
                        var jj = Mathf.Clamp(j + dj, 0, Rings);
                        sum += result[ii, jj];
                        max = Mathf.Max(max, result[ii, jj]);
                        n++;
                    }

                    next[i, j] = pass == 0 ? max : sum / n;
                }

                result = next;
            }

            for (var j = 0; j <= Rings; j++)
                result[Segments, j] = result[0, j];
            return result;
        }

        // ------------------------------------------------------------------ mesh

        private sealed class Builder
        {
            public readonly List<Vector3> V = new();
            public readonly List<Vector2> Uv = new();
            public readonly List<int> T = new();

            public int Add(Vector3 v, Vector2 uv)
            {
                V.Add(v);
                Uv.Add(uv);
                return V.Count - 1;
            }

            public void Quad(int a, int b, int c, int d, bool flip)
            {
                if (flip)
                    T.AddRange(new[] { a, c, b, a, d, c });
                else
                    T.AddRange(new[] { a, b, c, a, c, d });
            }
        }

        /// <summary>Texture u of an azimuth (0 = front, + = character's right) and back; see the class summary.</summary>
        private static float AzimuthU(float theta) => Mathf.Repeat((Mathf.PI * 0.5f - theta) / (2f * Mathf.PI), 1f);

        private static float AzimuthOf(float u) => Mathf.PI * 0.5f - u * 2f * Mathf.PI;

        private static float Meridian(float c) => Mathf.Pow(Mathf.Max(0f, c), Profile);

        private static Vector3 Crown(Vector3 r, float theta, float a)
        {
            var c = Meridian(Mathf.Cos(a));
            return new Vector3(r.x * c * Mathf.Sin(theta), r.y * Meridian(Mathf.Sin(a)), r.z * c * Mathf.Cos(theta));
        }

        private static bool IsOpening(float theta, float a, Opening o)
        {
            var dt = Mathf.Abs(Mathf.DeltaAngle(theta * Mathf.Rad2Deg, 180f)) * Mathf.Deg2Rad;
            if (a < o.StrapTop || a > o.Top)
                return false;
            var halfWidth = a < o.ArchStart
                ? OpeningHalfWidth
                : OpeningHalfWidth * Mathf.Sqrt(1f - Mathf.Pow((a - o.ArchStart) / (o.Top - o.ArchStart), 2f));
            return dt < halfWidth;
        }

        private static Mesh BuildMesh(Vector3 r, float[,] env, Fit fit, Opening opening)
        {
            var b = new Builder();
            var inner = r - Vector3.one * Thickness;
            var outerIdx = new int[Segments + 1, Rings + 1];
            var innerIdx = new int[Segments + 1, Rings + 1];
            // Grid vertices inside the back opening are moved onto its edge: the cut is a smooth arch, not a staircase
            // of missing quads. Quads with every corner inside are left out.
            var inside = new bool[Segments + 1, Rings + 1];
            for (var i = 0; i <= Segments; i++)
            {
                var u = (float)i / Segments;
                for (var j = 0; j <= Rings; j++)
                {
                    var theta = AzimuthOf(u);
                    var a = j * Mathf.PI * 0.5f / Rings;
                    var uv = new Vector2(u, 0.30f + 0.70f * a / (Mathf.PI * 0.5f));
                    if (IsOpening(theta, a, opening))
                    {
                        inside[i, j] = true;
                        SnapToOpeningEdge(ref theta, ref a, opening, r);
                        uv = new Vector2(AzimuthU(theta), 0.30f + 0.70f * a / (Mathf.PI * 0.5f));
                    }

                    outerIdx[i, j] = b.Add(Crown(r, theta, a) * env[i, j], uv);
                    innerIdx[i, j] = b.Add(Crown(inner, theta, a) * env[i, j], InsideUv);
                }
            }

            for (var i = 0; i < Segments; i++)
            for (var j = 0; j < Rings; j++)
            {
                if (inside[i, j] && inside[i, j + 1] && inside[i + 1, j + 1] && inside[i + 1, j])
                    continue;
                // i goes to the character's left, j up: clockwise seen from outside.
                b.Quad(outerIdx[i, j], outerIdx[i, j + 1], outerIdx[i + 1, j + 1], outerIdx[i + 1, j], false);
                b.Quad(innerIdx[i, j], innerIdx[i, j + 1], innerIdx[i + 1, j + 1], innerIdx[i + 1, j], true);
            }

            // Sweatband: closes the bottom edge between outside and inside.
            for (var i = 0; i < Segments; i++)
            {
                var o0 = b.Add(b.V[outerIdx[i, 0]], InsideUv);
                var o1 = b.Add(b.V[outerIdx[i + 1, 0]], InsideUv);
                var i0 = b.Add(b.V[innerIdx[i, 0]], InsideUv);
                var i1 = b.Add(b.V[innerIdx[i + 1, 0]], InsideUv);
                b.Quad(o0, o1, i1, i0, false); // faces down
            }

            BuildBrim(b, r, env, fit);
            BuildButton(b, r, env);

            var mesh = new Mesh { name = "Cap", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(b.V);
            mesh.SetUVs(0, b.Uv);
            mesh.SetTriangles(b.T, 0);
            mesh.RecalculateNormals();
            // The texture seam (u 0/1) duplicates a column of vertices: give both copies the same normal, or the
            // side of the crown shows a hard shading step.
            var normals = mesh.normals;
            for (var j = 0; j <= Rings; j++)
            {
                foreach (var idx in new[] { outerIdx, innerIdx })
                {
                    var n = (normals[idx[0, j]] + normals[idx[Segments, j]]).normalized;
                    normals[idx[0, j]] = n;
                    normals[idx[Segments, j]] = n;
                }
            }

            mesh.normals = normals;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Nearest point (crown parameters) on the edge of the back opening for a point inside it.</summary>
        private static void SnapToOpeningEdge(ref float theta, ref float a, Opening o, Vector3 r)
        {
            var dt = Mathf.DeltaAngle(180f, theta * Mathf.Rad2Deg) * Mathf.Deg2Rad; // signed, from the back centre
            var side = dt < 0f ? -1f : 1f;
            if (a >= o.ArchStart)
            {
                // Arch: an ellipse in (azimuth, elevation) - project radially from its centre.
                var x = dt / OpeningHalfWidth;
                var y = (a - o.ArchStart) / (o.Top - o.ArchStart);
                var len = Mathf.Max(new Vector2(x, y).magnitude, 1e-4f);
                theta = Mathf.PI + x / len * OpeningHalfWidth;
                a = o.ArchStart + y / len * (o.Top - o.ArchStart);
                return;
            }

            // Straight part: to the side edge or down to the strap, whichever is closer in metres.
            var toSide = (OpeningHalfWidth - Mathf.Abs(dt)) * r.x;
            var toStrap = (a - o.StrapTop) * r.y;
            if (toSide < toStrap)
                theta = Mathf.PI + side * OpeningHalfWidth;
            else
                a = o.StrapTop;
        }

        private static void BuildBrim(Builder b, Vector3 r, float[,] env, Fit fit)
        {
            const int across = 40;
            const int along = 12;
            var length = r.z * fit.BrimLengthFactor;
            var droop = Mathf.Tan(fit.BrimDroop * Mathf.Deg2Rad);
            var top = new int[across + 1, along + 1];
            var bottom = new int[across + 1, along + 1];
            for (var i = 0; i <= across; i++)
            {
                var s = -1f + 2f * i / across;
                var theta = s * BrimHalfAngle;
                var column = Mathf.RoundToInt(AzimuthU(theta) * Segments);
                var root = Crown(r, theta, 0f) * env[column, 0] * 1.003f;
                var outward = new Vector3(BrimSpread * Mathf.Sin(theta) / r.x, 0f, Mathf.Cos(theta) / r.z).normalized;
                var reach = length * Mathf.Pow(1f - s * s, 0.55f);
                for (var j = 0; j <= along; j++)
                {
                    var w = (float)j / along;
                    var d = reach * w;
                    var p = root + outward * d + Vector3.down * (d * droop + BrimCurve * s * s * d);
                    top[i, j] = b.Add(p, new Vector2(0.25f * (s + 1f), 0.02f + 0.24f * w));
                    bottom[i, j] = b.Add(p + Vector3.down * Thickness * 1.5f, new Vector2(0.5f + 0.25f * (s + 1f), 0.02f + 0.24f * w));
                }
            }

            for (var i = 0; i < across; i++)
            for (var j = 0; j < along; j++)
            {
                b.Quad(top[i, j], top[i, j + 1], top[i + 1, j + 1], top[i + 1, j], false);
                b.Quad(bottom[i, j], bottom[i, j + 1], bottom[i + 1, j + 1], bottom[i + 1, j], true);
            }

            // Outer edge.
            for (var i = 0; i < across; i++)
            {
                var a0 = b.Add(b.V[top[i, along]], EdgeUv);
                var a1 = b.Add(b.V[top[i + 1, along]], EdgeUv);
                var c0 = b.Add(b.V[bottom[i, along]], EdgeUv);
                var c1 = b.Add(b.V[bottom[i + 1, along]], EdgeUv);
                b.Quad(a0, c0, c1, a1, false);
            }
        }

        private static void BuildButton(Builder b, Vector3 r, float[,] env)
        {
            const int seg = 16;
            const int rings = 5;
            var radius = Mathf.Min(r.x, r.z) * 0.07f;
            var centre = new Vector3(0f, r.y * env[0, Rings] - radius * 0.35f, 0f);
            var idx = new int[seg + 1, rings + 1];
            var uv = new Vector2(0.75f, 0.985f);
            for (var i = 0; i <= seg; i++)
            for (var j = 0; j <= rings; j++)
            {
                var phi = i * 2f * Mathf.PI / seg;
                var a = j * Mathf.PI * 0.5f / rings;
                idx[i, j] = b.Add(centre + new Vector3(Mathf.Cos(a) * Mathf.Sin(phi), Mathf.Sin(a) * 0.6f, Mathf.Cos(a) * Mathf.Cos(phi)) * radius, uv);
            }

            for (var i = 0; i < seg; i++)
            for (var j = 0; j < rings; j++)
                b.Quad(idx[i, j], idx[i, j + 1], idx[i + 1, j + 1], idx[i + 1, j], true);
        }

        // ------------------------------------------------------------------ material

        private static Material Material()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", tex != null ? Color.white : new Color(0.36f, 0.4f, 0.33f));
            mat.SetFloat("_Smoothness", 0.08f); // washed cotton
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t.name == name)
                    return t;
            return null;
        }
    }
}
