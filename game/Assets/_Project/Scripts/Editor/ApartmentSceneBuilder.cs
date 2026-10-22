using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stasik.EditorTools
{
    /// <summary>
    /// Builds Scenes/Apartment from Art/Models/Apartment/Apartment.obj (exported by tools/build_apartment.py):
    /// URP materials with textures, colliders, sun and an overview camera.
    /// Run again after the model is rebuilt.
    /// </summary>
    public static class ApartmentSceneBuilder
    {
        private const string Root = "Assets/_Project";
        private const string ModelPath = Root + "/Art/Models/Apartment/Apartment.obj";
        private const string MaterialsDir = Root + "/Art/Materials";
        private const string TexturesDir = Root + "/Art/Textures";
        private const string ScenePath = Root + "/Scenes/Apartment.unity";

        private readonly struct MaterialSpec
        {
            public readonly string ObjName;     // material name in Apartment.mtl
            public readonly string Asset;       // material asset name
            public readonly string TextureSet;  // folder in Art/Textures, or null for a plain colour
            public readonly Color Color;
            public readonly Vector2 Tiling;     // texture repeats per metre
            public readonly float Smoothness;
            public readonly float Alpha;

            public MaterialSpec(string objName, string asset, string textureSet, Color color, Vector2 tiling,
                float smoothness, float alpha = 1f)
            {
                ObjName = objName;
                Asset = asset;
                TextureSet = textureSet;
                Color = color;
                Tiling = tiling;
                Smoothness = smoothness;
                Alpha = alpha;
            }
        }

        private static readonly MaterialSpec[] Materials =
        {
            new("Wall", "M_Wall", "Plaster", new Color(0.95f, 0.93f, 0.90f), new Vector2(0.5f, 1f), 0.1f),
            new("Floor", "M_Parquet", "Parquet", Color.white, new Vector2(0.5f, 0.5f), 0.45f),
            new("Tiles", "M_Tiles", "Tiles", Color.white, new Vector2(1f, 1f), 0.7f),
            new("Terrace", "M_Decking", "Decking", Color.white, new Vector2(0.5f, 0.5f), 0.3f),
            new("Glass", "M_Glass", null, new Color(0.62f, 0.78f, 0.85f), Vector2.one, 0.95f, 0.25f),
            new("Frame", "M_DoorFrame", null, new Color(0.17f, 0.17f, 0.17f), Vector2.one, 0.5f),
            new("WindowFrame", "M_WindowFrame", null, new Color(0.96f, 0.95f, 0.93f), Vector2.one, 0.4f),
        };

        [MenuItem("Stasik/Build Apartment Scene")]
        private static void BuildMenu()
        {
            // The scene is recreated from scratch: let the user save edits in the open scenes first.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Build();
        }

        /// <summary>Rebuilds the scene without asking (batch mode, tools). Unsaved changes in open scenes are lost.</summary>
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[Stasik] Stop Play mode before rebuilding the apartment scene.");
                return;
            }

            var materials = Materials.ToDictionary(s => s.ObjName, CreateMaterial);
            AssetDatabase.SaveAssets();
            SetupModel(materials);
            BuildScene();
            Debug.Log($"[Stasik] Apartment scene built: {ScenePath}");
        }

        // Unity.exe -batchmode -projectPath game -executeMethod Stasik.EditorTools.ApartmentSceneBuilder.BuildBatch -quit
        public static void BuildBatch() => Build();

        private static Material CreateMaterial(MaterialSpec spec)
        {
            var path = $"{MaterialsDir}/{spec.Asset}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }

            var color = spec.Color;
            color.a = spec.Alpha;
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", spec.Smoothness);

            if (spec.TextureSet != null)
            {
                mat.SetTexture("_BaseMap", LoadTexture(spec.TextureSet, "Color", false));
                mat.SetTextureScale("_BaseMap", spec.Tiling);
                var normal = LoadTexture(spec.TextureSet, "NormalGL", true);
                if (normal != null)
                {
                    mat.SetTexture("_BumpMap", normal);
                    mat.EnableKeyword("_NORMALMAP");
                }
            }

            if (spec.Alpha < 1f)
                MakeTransparent(mat);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Texture2D LoadTexture(string set, string map, bool isNormal)
        {
            var path = $"{TexturesDir}/{set}/{set}_{map}.jpg";
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                var type = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                // Floors are seen at grazing angles from the camera: without anisotropic filtering they smear.
                if (importer.textureType != type || importer.anisoLevel != 16 || importer.filterMode != FilterMode.Trilinear)
                {
                    importer.textureType = type;
                    importer.anisoLevel = 16;
                    importer.filterMode = FilterMode.Trilinear;
                    importer.SaveAndReimport();
                }
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                Debug.LogWarning($"[Stasik] Texture not found: {path}");
            return texture;
        }

        private static void MakeTransparent(Material mat)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            // Let the sun through the windows.
            mat.SetShaderPassEnabled("ShadowCaster", false);
        }

        private static void SetupModel(Dictionary<string, Material> materials)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.addCollider = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var (objName, mat) in materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), objName), mat);
            importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var ours = new HashSet<Material>(materials.Values);
            var unmapped = model.GetComponentsInChildren<Renderer>()
                .SelectMany(r => r.sharedMaterials)
                .Where(m => m != null && !ours.Contains(m))
                .Select(m => m.name)
                .Distinct()
                .ToList();
            if (unmapped.Count > 0)
                Debug.LogWarning($"[Stasik] Materials not remapped: {string.Join(", ", unmapped)}");
        }

        private static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var apartment = (GameObject)PrefabUtility.InstantiatePrefab(model);
            apartment.name = "Apartment";
            foreach (var t in apartment.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = true;

            // Sun from the terrace side, so light falls through the glass front into the living room.
            var sun = Object.FindAnyObjectByType<Light>();
            sun.name = "Sun";
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.85f);
            sun.intensity = 2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(40f, 25f, 0f);

            // Neutral-warm ambient: the default sky ambient tints shaded walls blue.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.78f, 0.80f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.60f, 0.57f);
            RenderSettings.ambientGroundColor = new Color(0.35f, 0.33f, 0.30f);

            // Overview camera above the flat (no ceiling yet). The game camera comes with the player.
            var cam = Camera.main;
            cam.name = "OverviewCamera";
            cam.transform.position = new Vector3(6.9f, 17.5f, -3.75f);
            cam.transform.LookAt(new Vector3(6.9f, 0f, 9.4f));
            cam.fieldOfView = 50f;

            ApartmentFurniture.Place();
            ApartmentFurniture.PlaceInteraction();

            PlacePlayer(cam);

            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>
        /// The playable streamer (Stasik/Characters/Build Player Prefab) in the living room plus the Witcher-style
        /// camera. The overview camera stays in the scene, disabled, for top-down shots.
        /// </summary>
        private static void PlacePlayer(Camera overview)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCharacterBuilder.PrefabPath);
            if (prefab == null)
                return;

            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            player.transform.SetPositionAndRotation(new Vector3(8.25f, 0f, 6.5f), Quaternion.Euler(0f, 180f, 0f));

            overview.tag = "Untagged";
            overview.enabled = false;
            var overviewListener = overview.GetComponent<AudioListener>();
            if (overviewListener != null)
                overviewListener.enabled = false;

            var camGo = new GameObject("PlayerCamera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();
            var follow = camGo.AddComponent<Stasik.Player.ThirdPersonCamera>();
            var so = new SerializedObject(follow);
            so.FindProperty("_target").objectReferenceValue = player.transform;
            so.FindProperty("_input").objectReferenceValue = player.GetComponent<Stasik.Player.PlayerInput>();
            so.ApplyModifiedPropertiesWithoutUndo();
            // Start where ThirdPersonCamera will put it (same formula and defaults), so the first frame doesn't jump.
            var rot = Quaternion.Euler(12f, player.transform.eulerAngles.y, 0f);
            var pivot = player.transform.position + Vector3.up * 1.05f;
            camGo.transform.SetPositionAndRotation(pivot + rot * Vector3.right * 0.3f + rot * Vector3.back * 1.9f, rot);

            var movement = new SerializedObject(player.GetComponent<Stasik.Player.PlayerMovement>());
            movement.FindProperty("_camera").objectReferenceValue = camGo.transform;
            movement.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
