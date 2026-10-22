using System.IO;
using Stasik.Characters;
using UnityEditor;
using UnityEngine;

namespace Stasik.EditorTools
{
    /// <summary>
    /// Builds the streamer character prefab from the generated model in one go:
    /// stand it up facing -Z at <see cref="Height"/>, fit the head to the concept front and profile, smooth the face,
    /// bake the four concept views into the texture (ConceptProjectionBaker), add shell fur,
    /// save Prefabs/Characters/Streamer.prefab.
    /// Run again after replacing the model or the concept. ApartmentSceneBuilder places the prefab in the flat.
    /// </summary>
    public static class StreamerCharacterBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Characters/Streamer.prefab";

        private const string SourceModel = "Assets/_Project/Art/Models/Characters/Streamer/MultiView/Streamer_Hunyuan_MV.glb";
        private const string FurMaterialPath = "Assets/_Project/Art/Materials/M_Fur_Streamer.mat";
        private const float Height = 1.3f;
        private const int FurShellCount = 24;

        // The Hunyuan GLB imports lying on its back; this stands it up with the face towards -Z.
        private static readonly Vector3 SourceRotation = new(90f, 180f, 0f);

        [MenuItem("Stasik/Characters/Build Streamer Prefab")]
        private static void BuildMenu() => Debug.Log("[Stasik] " + Build());

        public static string Build()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(SourceModel);
            if (asset == null)
                return $"Source model not found: {SourceModel}";

            var model = (GameObject)Object.Instantiate(asset);
            model.name = "Streamer";
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(SourceRotation));
            var renderer = model.GetComponentInChildren<MeshRenderer>();
            model.transform.localScale *= Height / renderer.bounds.size.y;
            var b = renderer.bounds;
            model.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);

            var log = ConceptProjectionBaker.FitToView(model, ConceptProjectionBaker.ViewId.Front) + "\n" +
                      ConceptProjectionBaker.FitToView(model, ConceptProjectionBaker.ViewId.Left) + "\n" +
                      ConceptProjectionBaker.Bake(model);
            // SmoothFace is not used any more: it flattened the nose in profile, and with four views plus the soft
            // CharacterPhoto shading the face relief no longer fights the photo.

            // Root at the feet, facing -Z, so placing the prefab is just position + yaw.
            var root = new GameObject("Streamer");
            model.name = "Model";
            model.transform.SetParent(root.transform, true);

            var fur = root.AddComponent<FurShells>();
            var so = new SerializedObject(fur);
            so.FindProperty("_source").objectReferenceValue = renderer;
            so.FindProperty("_furMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(FurMaterialPath);
            so.FindProperty("_shellCount").intValue = FurShellCount;
            so.ApplyModifiedPropertiesWithoutUndo();
            // Shells are runtime-only children; disable while saving so none end up in the prefab.
            fur.enabled = false;

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            var prefabFur = prefab.GetComponent<FurShells>();
            var prefabSo = new SerializedObject(prefabFur);
            prefabSo.FindProperty("m_Enabled").boolValue = true;
            prefabSo.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            return $"{log}\nSaved {PrefabPath}";
        }
    }
}
