using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stasik.Characters
{
    /// <summary>
    /// Shell fur: draws the source mesh again <see cref="_shellCount"/> times with the Stasik/FurShell material,
    /// each shell pushed further out along the normals (_ShellHeight per shell).
    ///
    /// In play mode the shells are extra material slots on the source renderer itself: Unity draws the last submesh
    /// once per extra material, so a skinned mesh is skinned once per frame, not once per shell.
    /// In edit mode (so the scene and prefab assets are never modified) the shells are temporary child renderers.
    /// </summary>
    [ExecuteAlways]
    public class FurShells : MonoBehaviour
    {
        private const string ShellPrefix = "FurShell_";
        private static readonly int ShellHeightId = Shader.PropertyToID("_ShellHeight");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        [SerializeField] private Renderer _source;
        [SerializeField] private Material _furMaterial;
        [SerializeField, Range(4, 32)] private int _shellCount = 16;

        private Material[] _originalMaterials;

        private void OnEnable() => Build();

        private void OnDisable() => Clear();

        private void OnValidate()
        {
            if (!isActiveAndEnabled)
                return;
#if UNITY_EDITOR
            // Objects can't be created inside OnValidate itself.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled)
                    Build();
            };
#endif
        }

        public void Build()
        {
            Clear();
            if (_source == null)
                _source = GetComponentInChildren<SkinnedMeshRenderer>() ?? (Renderer)GetComponentInChildren<MeshRenderer>();
            if (_source == null || _furMaterial == null)
                return;

            if (Application.isPlaying)
                BuildMaterialSlots();
            else
                BuildChildShells();
        }

        private Texture BaseMap() =>
            _source.sharedMaterial != null ? _source.sharedMaterial.mainTexture : null;

        private MaterialPropertyBlock ShellBlock(int shell, Texture baseMap)
        {
            var block = new MaterialPropertyBlock();
            block.SetFloat(ShellHeightId, (float)shell / _shellCount);
            if (baseMap != null)
                block.SetTexture(BaseMapId, baseMap);
            return block;
        }

        private void BuildMaterialSlots()
        {
            if (_source.sharedMaterials.Length == 0)
                return; // nothing to put fur on; never store an empty list to "restore" later
            _originalMaterials = _source.sharedMaterials;
            var baseMap = BaseMap();
            var materials = new List<Material>(_originalMaterials);
            for (var i = 0; i < _shellCount; i++)
                materials.Add(_furMaterial);
            _source.sharedMaterials = materials.ToArray();
            for (var i = 1; i <= _shellCount; i++)
                _source.SetPropertyBlock(ShellBlock(i, baseMap), _originalMaterials.Length + i - 1);
        }

        private void BuildChildShells()
        {
            var baseMap = BaseMap();
            for (var i = 1; i <= _shellCount; i++)
            {
                var shell = new GameObject(ShellPrefix + i) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
                shell.transform.SetParent(_source.transform, false);

                Renderer r;
                if (_source is SkinnedMeshRenderer skinned)
                {
                    var s = shell.AddComponent<SkinnedMeshRenderer>();
                    s.sharedMesh = skinned.sharedMesh;
                    s.bones = skinned.bones;
                    s.rootBone = skinned.rootBone;
                    s.localBounds = skinned.localBounds;
                    s.updateWhenOffscreen = skinned.updateWhenOffscreen;
                    r = s;
                }
                else
                {
                    shell.AddComponent<MeshFilter>().sharedMesh = _source.GetComponent<MeshFilter>().sharedMesh;
                    r = shell.AddComponent<MeshRenderer>();
                }

                r.sharedMaterial = _furMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.SetPropertyBlock(ShellBlock(i, baseMap));
            }
        }

        private void Clear()
        {
            if (_source == null)
                return;

            // Material slots exist only in play mode; edit-mode objects (scene, prefabs) are never modified here.
            if (_originalMaterials != null && _originalMaterials.Length > 0 && Application.isPlaying)
            {
                for (var i = 0; i < _source.sharedMaterials.Length; i++)
                    _source.SetPropertyBlock(null, i);
                _source.sharedMaterials = _originalMaterials;
                _originalMaterials = null;
            }

            // By name, not by a list: copies made by duplicating a furred object carry shells this instance never made.
            for (var i = _source.transform.childCount - 1; i >= 0; i--)
            {
                var child = _source.transform.GetChild(i).gameObject;
                if (!child.name.StartsWith(ShellPrefix))
                    continue;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }
        }
    }
}
