using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Stasik.EditorTools
{
    /// <summary>
    /// Turns the generated character model into the concept: reshapes it to the concept's silhouettes and bakes the
    /// concept views (front, left, right, back; cut-out images) into its texture.
    /// Each texel takes its colour from the views it faces (weighted by visibility and facing), the front photo wins on
    /// the face. The model must stand upright facing -Z and still use its generated material (work on a fresh copy).
    /// Order: <see cref="FitToView"/> front, then left, <see cref="SmoothFace"/>, <see cref="Bake"/>.
    /// </summary>
    public static class ConceptProjectionBaker
    {
        private const string ConceptDir = "Assets/_Project/Art/Models/Characters/Streamer/Concept";
        private const string OutDir = "Assets/_Project/Art/Models/Characters/Streamer/Baked";

        // Face (forehead to mouth) on the front concept, in uv: the photo always wins here and no fur grows.
        private static readonly Vector4 FaceEllipse = new(0.50f, 0.82f, 0.10f, 0.075f);

        public enum ViewId { Front, Left, Right, Back }

        private readonly struct View
        {
            public readonly ViewId Id;
            public readonly string Image;
            public readonly Vector3 Forward; // direction the camera looks
            public readonly float Boost;

            public View(ViewId id, string image, Vector3 forward, float boost)
            {
                Id = id;
                Image = image;
                Forward = forward;
                Boost = boost;
            }
        }

        // The character faces -Z: the front camera looks along +Z, the left one (his left side, +X) along -X, etc.
        private static readonly View[] Views =
        {
            new(ViewId.Front, "Streamer_Front_Cutout.png", Vector3.forward, 1f),
            new(ViewId.Left, "Streamer_Left_Cutout.png", Vector3.left, 0.8f),
            new(ViewId.Right, "Streamer_Right_Cutout.png", Vector3.right, 0.8f),
            new(ViewId.Back, "Streamer_Back_Cutout.png", Vector3.back, 0.8f),
        };

        /// <summary>Orthographic view frame around the model: image x = along Right, image y = world up.</summary>
        private sealed class Frame
        {
            public Vector3 Right, Forward;
            public Vector4 Rect; // min right, min y, size right, size y
            public float MinForward, SizeForward;
        }

        private sealed class ViewData
        {
            public Frame Frame;
            public RenderTexture Depth;
            public Texture2D ModelRows, ImageRows, Image;
            public Color[] ModelRowPixels, ImageRowPixels;
            public Vector4 ImageRect;

            public void Release()
            {
                Depth.Release();
                Object.DestroyImmediate(ModelRows);
                Object.DestroyImmediate(ImageRows);
            }
        }

        // ------------------------------------------------------------------ shape

        /// <summary>
        /// Reshapes the head (rows above <paramref name="headFrom"/>) so its silhouette in the given view matches the
        /// view image row by row. For the front view also remaps heights so the neck sits where it is on the concept.
        /// The first call saves a fitted copy of the mesh next to the bakes and assigns it; later calls edit that copy.
        /// </summary>
        public static string FitToView(GameObject go, ViewId viewId, float headFrom = 0.7f)
        {
            var renderer = go.GetComponentInChildren<MeshRenderer>();
            var filter = renderer.GetComponent<MeshFilter>();
            var mesh = EnsureFittedCopy(go, filter);
            var view = System.Array.Find(Views, v => v.Id == viewId);
            var data = Prepare(mesh, renderer.localToWorldMatrix, view);
            var f = data.Frame;

            var neckModel = 0f;
            var neckImage = 0f;
            var remapHeight = viewId == ViewId.Front;
            if (remapHeight)
            {
                neckModel = NeckRow(data.ModelRowPixels, 0f, 1f);
                neckImage = NeckRow(data.ImageRowPixels, data.ImageRect.y, data.ImageRect.y + data.ImageRect.w);
            }

            var toWorld = renderer.localToWorldMatrix;
            var toLocal = renderer.worldToLocalMatrix;
            var verts = mesh.vertices;
            var moved = 0;
            for (var i = 0; i < verts.Length; i++)
            {
                var wp = toWorld.MultiplyPoint3x4(verts[i]);
                var px = (Vector3.Dot(wp, f.Right) - f.Rect.x) / f.Rect.z;
                var py = Mathf.Clamp01((wp.y - f.Rect.y) / f.Rect.w);
                var m = Row(data.ModelRowPixels, py);

                if (remapHeight)
                {
                    py = py < neckModel
                        ? py / neckModel * neckImage
                        : neckImage + (py - neckModel) / (1f - neckModel) * (1f - neckImage);
                    wp.y = f.Rect.y + py * f.Rect.w;
                }

                var head = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(headFrom - 0.03f, headFrom + 0.03f, py));
                var c = Row(data.ImageRowPixels, data.ImageRect.y + py * data.ImageRect.w);
                if (head > 0f && m.r >= 0 && c.r >= 0 && m.g > m.r)
                {
                    var cl = (c.r - data.ImageRect.x) / data.ImageRect.z;
                    var cr = (c.g - data.ImageRect.x) / data.ImageRect.z;
                    var target = Mathf.Lerp(cl, cr, (px - m.r) / (m.g - m.r));
                    var lateral = f.Rect.x + Mathf.Lerp(px, target, head) * f.Rect.z;
                    wp += f.Right * (lateral - Vector3.Dot(wp, f.Right));
                    moved++;
                }

                verts[i] = toLocal.MultiplyPoint3x4(wp);
            }

            mesh.vertices = verts;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();
            data.Release();
            return remapHeight
                ? $"Fit {viewId}: {moved} head vertices, neck {neckModel:F3} -> {neckImage:F3}"
                : $"Fit {viewId}: {moved} head vertices";
        }

        private static Mesh EnsureFittedCopy(GameObject go, MeshFilter filter)
        {
            var path = $"{OutDir}/{go.name}_Fitted.asset";
            if (AssetDatabase.GetAssetPath(filter.sharedMesh) == path)
                return filter.sharedMesh;

            var copy = Object.Instantiate(filter.sharedMesh);
            copy.name = go.name + "_Fitted";
            Directory.CreateDirectory(OutDir);
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(copy, path);
            filter.sharedMesh = copy;
            return copy;
        }

        /// <summary>
        /// Smooths the face relief (eye sockets, nose, brow) so the photo, which carries its own lighting, draws the
        /// features. Taubin smoothing (no shrinking) inside the face ellipse on the front of the head.
        /// </summary>
        public static string SmoothFace(GameObject go, int iterations = 12, float faceFrom = 0.79f, float faceTo = 0.97f,
            float halfWidth = 0.14f)
        {
            var renderer = go.GetComponentInChildren<MeshRenderer>();
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var bounds = renderer.bounds;
            var toWorld = renderer.localToWorldMatrix;
            var verts = mesh.vertices;
            var normals = mesh.normals;
            var tris = mesh.triangles;

            // Weld split vertices (UV seams) by position so smoothing doesn't tear the mesh.
            var weld = new int[verts.Length];
            var map = new Dictionary<Vector3Int, int>();
            var welded = new List<Vector3>();
            for (var i = 0; i < verts.Length; i++)
            {
                var key = Vector3Int.RoundToInt(verts[i] * 100000f);
                if (!map.TryGetValue(key, out var w))
                {
                    w = welded.Count;
                    map[key] = w;
                    welded.Add(verts[i]);
                }

                weld[i] = w;
            }

            var neighbours = new HashSet<int>[welded.Count];
            for (var i = 0; i < neighbours.Length; i++)
                neighbours[i] = new HashSet<int>();
            for (var t = 0; t < tris.Length; t += 3)
            {
                int a = weld[tris[t]], b = weld[tris[t + 1]], c = weld[tris[t + 2]];
                neighbours[a].Add(b); neighbours[a].Add(c);
                neighbours[b].Add(a); neighbours[b].Add(c);
                neighbours[c].Add(a); neighbours[c].Add(b);
            }

            var weight = new float[welded.Count];
            var cx = bounds.center.x;
            var cy = bounds.min.y + (faceFrom + faceTo) * 0.5f * bounds.size.y;
            var ry = (faceTo - faceFrom) * 0.5f * bounds.size.y;
            var rx = halfWidth * bounds.size.x;
            for (var i = 0; i < verts.Length; i++)
            {
                var wp = toWorld.MultiplyPoint3x4(verts[i]);
                var n = toWorld.MultiplyVector(normals[i]).normalized;
                var e = Mathf.Sqrt(Mathf.Pow((wp.x - cx) / rx, 2) + Mathf.Pow((wp.y - cy) / ry, 2));
                var w = Mathf.Clamp01((1f - e) / 0.3f) * Mathf.Clamp01((-n.z - 0.2f) / 0.3f);
                weight[weld[i]] = Mathf.Max(weight[weld[i]], w);
            }

            var p = welded.ToArray();
            var tmp = new Vector3[p.Length];
            for (var it = 0; it < iterations * 2; it++)
            {
                var factor = it % 2 == 0 ? 0.5f : -0.53f;
                for (var i = 0; i < p.Length; i++)
                {
                    if (weight[i] <= 0f || neighbours[i].Count == 0)
                    {
                        tmp[i] = p[i];
                        continue;
                    }

                    var avg = Vector3.zero;
                    foreach (var j in neighbours[i])
                        avg += p[j];
                    avg /= neighbours[i].Count;
                    tmp[i] = p[i] + (avg - p[i]) * (factor * weight[i]);
                }

                (p, tmp) = (tmp, p);
            }

            for (var i = 0; i < verts.Length; i++)
                verts[i] = p[weld[i]];
            mesh.vertices = verts;

            var copy = Object.Instantiate(mesh);
            copy.RecalculateNormals();
            var fresh = copy.normals;
            Object.DestroyImmediate(copy);
            var result = mesh.normals;
            for (var i = 0; i < verts.Length; i++)
            {
                var w = weight[weld[i]];
                if (w > 0f)
                    result[i] = Vector3.Slerp(normals[i], fresh[i], w).normalized;
            }

            mesh.normals = result;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();
            return $"Smoothed face on {mesh.name}";
        }

        // ------------------------------------------------------------------ texture

        /// <summary>
        /// Bakes the views into a new texture + Stasik/CharacterPhoto material and assigns it. The side and back views
        /// cover the head only; on the body the front view fades (facing^bodySharpness) into the original texture, since
        /// the photos' shirt stripes don't line up with each other, and the shirt takes the front photo all the way
        /// round (its stripes are horizontal).
        /// </summary>
        public static string Bake(GameObject go, float headFrom = 0.7f, float sharpness = 2f, float faceBoost = 6f,
            float coverage = 0.35f, float bodySharpness = 4f)
        {
            var renderer = go.GetComponentInChildren<MeshRenderer>();
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var original = renderer.sharedMaterial.mainTexture;
            if (original == null)
                return "The model's material has no base texture.";
            var model = renderer.localToWorldMatrix;
            var prev = RenderTexture.active;

            var accum = new RenderTexture(original.width, original.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var faceMask = new RenderTexture(original.width, original.height, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
            Graphics.SetRenderTarget(accum);
            GL.Clear(true, true, Color.clear);
            Graphics.SetRenderTarget(faceMask);
            GL.Clear(true, true, Color.clear);

            var bakeMat = new Material(Shader.Find("Hidden/Stasik/ProjBake"));
            foreach (var view in Views)
            {
                var data = Prepare(mesh, model, view);
                var f = data.Frame;
                bakeMat.SetTexture("_DepthTex", data.Depth);
                bakeMat.SetTexture("_ViewTex", data.Image);
                bakeMat.SetTexture("_ModelRows", data.ModelRows);
                bakeMat.SetTexture("_ImageRows", data.ImageRows);
                bakeMat.SetMatrix("_ModelMatrix", model);
                bakeMat.SetVector("_Right", f.Right);
                bakeMat.SetVector("_Forward", f.Forward);
                bakeMat.SetVector("_ViewRect", f.Rect);
                bakeMat.SetFloat("_MinForward", f.MinForward);
                bakeMat.SetVector("_ImgRect", data.ImageRect);
                bakeMat.SetFloat("_DepthEps", f.SizeForward * 0.02f);
                bakeMat.SetFloat("_HeadFrom", headFrom);
                bakeMat.SetFloat("_Sharpness", sharpness);
                bakeMat.SetFloat("_BodySharpness", bodySharpness);
                bakeMat.SetFloat("_HeadOnly", view.Id == ViewId.Front ? 0f : 1f);
                bakeMat.SetTexture("_OrigTex", original);
                bakeMat.SetFloat("_ShirtWeight", view.Id == ViewId.Front ? coverage : 0f);
                var stripes = view.Id == ViewId.Front ? StripeProfile(data.Image, data.ImageRowPixels, data.ImageRect, headFrom) : null;
                bakeMat.SetTexture("_StripeTex", stripes != null ? stripes : Texture2D.blackTexture);
                bakeMat.SetFloat("_Boost", view.Boost);
                bakeMat.SetFloat("_FaceBoost", view.Id == ViewId.Front ? faceBoost : 0f);
                bakeMat.SetVector("_FaceEllipse", FaceEllipse);

                bakeMat.SetFloat("_MaskMode", 0f);
                DrawUv(mesh, bakeMat, accum);
                if (view.Id == ViewId.Front)
                {
                    bakeMat.SetFloat("_MaskMode", 1f);
                    DrawUv(mesh, bakeMat, faceMask);
                }

                data.Release();
                if (stripes != null)
                    Object.DestroyImmediate(stripes);
            }

            var bakeRt = new RenderTexture(original.width, original.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var compositeMat = new Material(Shader.Find("Hidden/Stasik/ProjComposite"));
            compositeMat.SetTexture("_AccumTex", accum);
            compositeMat.SetTexture("_FaceMaskTex", faceMask);
            compositeMat.SetFloat("_Coverage", coverage);
            Graphics.Blit(original, bakeRt, compositeMat);

            var result = new Texture2D(bakeRt.width, bakeRt.height, TextureFormat.RGBA32, false, false);
            RenderTexture.active = bakeRt;
            result.ReadPixels(new Rect(0, 0, bakeRt.width, bakeRt.height), 0, 0);
            result.Apply();

            // Grow the baked islands so UV seams don't show stray texels.
            var coverageMask = RenderCoverage(mesh, bakeRt.width, bakeRt.height);
            var pixels = result.GetPixels32();
            Dilate(pixels, coverageMask, bakeRt.width, bakeRt.height, 16);
            result.SetPixels32(pixels);
            result.Apply();
            RenderTexture.active = prev;

            var texPath = $"{OutDir}/{go.name}_Baked.png";
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(texPath, result.EncodeToPNG());
            AssetDatabase.ImportAsset(texPath);
            if (AssetImporter.GetAtPath(texPath) is TextureImporter ti)
            {
                ti.maxTextureSize = 4096;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }

            // The bake carries the photos' own lighting: soft shading, no highlights.
            var matPath = $"{OutDir}/M_{go.name}.mat";
            var shader = Shader.Find("Stasik/CharacterPhoto");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }

            mat.shader = shader;
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            mat.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            renderer.sharedMaterial = mat;

            Object.DestroyImmediate(bakeMat);
            Object.DestroyImmediate(compositeMat);
            Object.DestroyImmediate(result);
            accum.Release();
            faceMask.Release();
            bakeRt.Release();
            return $"Baked {Views.Length} views -> {texPath}";
        }

        private static void DrawUv(Mesh mesh, Material mat, RenderTexture target)
        {
            var prev = RenderTexture.active;
            Graphics.SetRenderTarget(target);
            mat.SetPass(0);
            for (var s = 0; s < mesh.subMeshCount; s++)
                Graphics.DrawMeshNow(mesh, Matrix4x4.identity, s);
            RenderTexture.active = prev;
        }

        // ------------------------------------------------------------------ views

        private static ViewData Prepare(Mesh mesh, Matrix4x4 model, View view)
        {
            var frame = MakeFrame(mesh, model, view.Forward);
            var depth = RenderDepth(mesh, model, frame);
            var (modelRows, modelRowPixels) = ModelSilhouetteRows(depth);
            var image = LoadReadable($"{ConceptDir}/{view.Image}");
            var (imageRect, imageRows, imageRowPixels) = ImageSilhouette(image);
            return new ViewData
            {
                Frame = frame, Depth = depth, ModelRows = modelRows, ModelRowPixels = modelRowPixels,
                Image = image, ImageRows = imageRows, ImageRowPixels = imageRowPixels, ImageRect = imageRect,
            };
        }

        private static Frame MakeFrame(Mesh mesh, Matrix4x4 model, Vector3 forward)
        {
            var right = Vector3.Cross(Vector3.up, forward);
            float minR = float.MaxValue, maxR = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            float minF = float.MaxValue, maxF = float.MinValue;
            foreach (var v in mesh.vertices)
            {
                var w = model.MultiplyPoint3x4(v);
                var r = Vector3.Dot(w, right);
                var f = Vector3.Dot(w, forward);
                minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
                minY = Mathf.Min(minY, w.y); maxY = Mathf.Max(maxY, w.y);
                minF = Mathf.Min(minF, f); maxF = Mathf.Max(maxF, f);
            }

            return new Frame
            {
                Right = right, Forward = forward,
                Rect = new Vector4(minR, minY, maxR - minR, maxY - minY),
                MinForward = minF, SizeForward = maxF - minF,
            };
        }

        private static RenderTexture RenderDepth(Mesh mesh, Matrix4x4 model, Frame frame)
        {
            var format = SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.RFloat)
                ? RenderTextureFormat.RFloat
                : RenderTextureFormat.RHalf;
            var rt = new RenderTexture(1024, Mathf.RoundToInt(1024 * frame.Rect.w / frame.Rect.z), 0, format)
                { filterMode = FilterMode.Point };
            var mat = new Material(Shader.Find("Hidden/Stasik/ProjDepth"));
            mat.SetMatrix("_ModelMatrix", model);
            mat.SetVector("_Right", frame.Right);
            mat.SetVector("_Forward", frame.Forward);
            mat.SetVector("_ViewRect", frame.Rect);
            mat.SetFloat("_MinForward", frame.MinForward);
            var prev = RenderTexture.active;
            Graphics.SetRenderTarget(rt);
            GL.Clear(true, true, new Color(100f, 0, 0, 0));
            mat.SetPass(0);
            for (var s = 0; s < mesh.subMeshCount; s++)
                Graphics.DrawMeshNow(mesh, Matrix4x4.identity, s);
            RenderTexture.active = prev;
            Object.DestroyImmediate(mat);
            return rt;
        }

        /// <summary>Model silhouette left/right edge per row of a depth map (0..1 of the frame).</summary>
        private static (Texture2D, Color[]) ModelSilhouetteRows(RenderTexture depthRt)
        {
            var tex = new Texture2D(depthRt.width, depthRt.height, TextureFormat.RFloat, false, true);
            var prev = RenderTexture.active;
            RenderTexture.active = depthRt;
            tex.ReadPixels(new Rect(0, 0, depthRt.width, depthRt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var d = tex.GetPixels();
            int w = tex.width, h = tex.height;
            var left = new float[h];
            var right = new float[h];
            for (var y = 0; y < h; y++)
            {
                int l = -1, r = -1;
                for (var x = 0; x < w; x++)
                {
                    if (d[y * w + x].r > 50f)
                        continue;
                    if (l < 0)
                        l = x;
                    r = x;
                }

                left[y] = l < 0 ? -1f : (float)l / w;
                right[y] = l < 0 ? -1f : (float)r / w;
            }

            Object.DestroyImmediate(tex);
            return RowsTexture(Smooth(left, h / 250), Smooth(right, h / 250));
        }

        /// <summary>Character rect in a cut-out image (uv) and its left/right edge per image row.</summary>
        private static (Vector4, Texture2D, Color[]) ImageSilhouette(Texture2D image)
        {
            var px = image.GetPixels32();
            int w = image.width, h = image.height;
            var left = new float[h];
            var right = new float[h];
            int minY = h, maxY = -1, minX = w, maxX = -1;
            for (var y = 0; y < h; y++)
            {
                int l = -1, r = -1;
                for (var x = 0; x < w; x++)
                {
                    if (px[y * w + x].a < 128)
                        continue;
                    if (l < 0)
                        l = x;
                    r = x;
                }

                left[y] = l < 0 ? -1f : (float)l / w;
                right[y] = l < 0 ? -1f : (float)r / w;
                if (l < 0)
                    continue;
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
                minX = Mathf.Min(minX, l);
                maxX = Mathf.Max(maxX, r);
            }

            var rect = new Vector4((float)minX / w, (float)minY / h, (float)(maxX - minX) / w, (float)(maxY - minY) / h);
            var (tex, pixels) = RowsTexture(Smooth(left, h / 250), Smooth(right, h / 250));
            return (rect, tex, pixels);
        }

        private static Color Row(Color[] rows, float v) =>
            rows[Mathf.Clamp(Mathf.RoundToInt(v * (rows.Length - 1)), 0, rows.Length - 1)];

        /// <summary>Neck height (0..1 of the character): the narrowest silhouette row in the 60-85% band.</summary>
        private static float NeckRow(Color[] rows, float from, float to)
        {
            var best = 0.72f;
            var bestWidth = float.MaxValue;
            for (var q = 0.6f; q <= 0.85f; q += 0.002f)
            {
                var row = Row(rows, from + q * (to - from));
                if (row.r < 0)
                    continue;
                if (row.g - row.r < bestWidth)
                {
                    bestWidth = row.g - row.r;
                    best = q;
                }
            }

            return best;
        }

        private static float[] Smooth(float[] v, int radius)
        {
            var o = new float[v.Length];
            for (var i = 0; i < v.Length; i++)
            {
                if (v[i] < 0)
                {
                    o[i] = -1f;
                    continue;
                }

                float sum = 0;
                var n = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    var j = i + k;
                    if (j < 0 || j >= v.Length || v[j] < 0)
                        continue;
                    sum += v[j];
                    n++;
                }

                o[i] = sum / n;
            }

            return o;
        }

        private static (Texture2D, Color[]) RowsTexture(float[] left, float[] right)
        {
            var t = new Texture2D(left.Length, 1, TextureFormat.RGFloat, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var c = new Color[left.Length];
            for (var i = 0; i < left.Length; i++)
                c[i] = new Color(left[i], right[i], 0, 0);
            t.SetPixels(c);
            t.Apply();
            return (t, c);
        }

        /// <summary>
        /// The shirt's stripes as one colour per image row (1 x image height, sRGB like the image): the median of the
        /// colourless pixels in the middle of the body. Rows without a clean shirt above the hem (behind the chin,
        /// above the head line) repeat the row one or more stripe periods away. Alpha 0 = no data (below the hem).
        /// </summary>
        private static Texture2D StripeProfile(Texture2D image, Color[] rows, Vector4 rect, float headFrom)
        {
            var px = image.GetPixels32();
            int w = image.width, h = image.height;
            var top = Mathf.Clamp(Mathf.RoundToInt((rect.y + headFrom * rect.w) * h), 0, h);
            var colors = new Color32[h];
            var valid = new bool[h];
            var lum = new float[h];
            var r = new List<byte>();
            var g = new List<byte>();
            var b = new List<byte>();
            for (var y = 0; y < top; y++)
            {
                if (rows[y].r < 0f)
                    continue;
                // Middle quarter of the row's silhouette: the torso, away from the arms and the folds at the sides.
                float left = rows[y].r * w, right = rows[y].g * w;
                float centre = (left + right) * 0.5f, half = (right - left) * 0.12f;
                int x0 = Mathf.Max(0, Mathf.RoundToInt(centre - half)), x1 = Mathf.Min(w - 1, Mathf.RoundToInt(centre + half));
                r.Clear();
                g.Clear();
                b.Clear();
                for (var x = x0; x <= x1; x++)
                {
                    var p = px[y * w + x];
                    if (p.a < 128 || Mathf.Max(p.r, Mathf.Max(p.g, p.b)) - Mathf.Min(p.r, Mathf.Min(p.g, p.b)) > 18)
                        continue;
                    r.Add(p.r);
                    g.Add(p.g);
                    b.Add(p.b);
                }

                if (r.Count == 0 || r.Count < (x1 - x0 + 1) * 0.7f)
                    continue;
                r.Sort();
                g.Sort();
                b.Sort();
                var m = r.Count / 2;
                colors[y] = new Color32(r[m], g[m], b[m], 255);
                lum[y] = (r[m] + g[m] + b[m]) / 3f;
                valid[y] = true;
            }

            var period = StripePeriod(lum, valid, rect.w * h);
            var filled = (Color32[])colors.Clone();
            var hem = System.Array.IndexOf(valid, true); // nothing below the shirt: hands and feet hang there
            if (period > 0 && hem >= 0)
                for (var y = hem; y < h; y++)
                {
                    if (valid[y])
                        continue;
                    for (var k = 1; k <= 12; k++)
                    {
                        int below = y - k * period, above = y + k * period;
                        if (below >= 0 && valid[below])
                            filled[y] = colors[below];
                        else if (above < h && valid[above])
                            filled[y] = colors[above];
                        else
                            continue;
                        break;
                    }
                }

            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false, false)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(filled);
            tex.Apply();
            return tex;
        }

        /// <summary>Row lag (pixels) at which the stripe profile repeats best; 0 if it doesn't repeat clearly.</summary>
        private static int StripePeriod(float[] lum, bool[] valid, float bodyPixels)
        {
            int minLag = Mathf.Max(2, Mathf.RoundToInt(bodyPixels * 0.01f)), maxLag = Mathf.RoundToInt(bodyPixels * 0.08f);
            var best = 0.5f; // weaker correlation = no clear stripes
            var bestLag = 0;
            for (var lag = minLag; lag <= maxLag; lag++)
            {
                double n = 0, sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
                for (var y = 0; y + lag < lum.Length; y++)
                {
                    if (!valid[y] || !valid[y + lag])
                        continue;
                    double a = lum[y], c = lum[y + lag];
                    n++;
                    sa += a;
                    sb += c;
                    saa += a * a;
                    sbb += c * c;
                    sab += a * c;
                }

                if (n < 50)
                    continue;
                var cov = sab / n - sa / n * (sb / n);
                var den = System.Math.Sqrt((saa / n - sa / n * (sa / n)) * (sbb / n - sb / n * (sb / n)));
                var corr = den > 1e-6 ? (float)(cov / den) : 0f;
                if (corr > best)
                {
                    best = corr;
                    bestLag = lag;
                }
            }

            return bestLag;
        }

        private static Texture2D LoadReadable(string path)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter ti && (!ti.isReadable || ti.maxTextureSize < 4096 ||
                                                                         ti.textureCompression != TextureImporterCompression.Uncompressed))
            {
                ti.isReadable = true;
                ti.maxTextureSize = 4096;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ seams

        private static bool[] RenderCoverage(Mesh mesh, int width, int height)
        {
            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            var mat = new Material(Shader.Find("Hidden/Stasik/UVCoverage"));
            var prev = RenderTexture.active;
            Graphics.SetRenderTarget(rt);
            GL.Clear(true, true, Color.clear);
            mat.SetPass(0);
            for (var s = 0; s < mesh.subMeshCount; s++)
                Graphics.DrawMeshNow(mesh, Matrix4x4.identity, s);
            var tex = new Texture2D(width, height, TextureFormat.R8, false, true);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            var raw = tex.GetRawTextureData<byte>();
            var covered = new bool[width * height];
            for (var i = 0; i < covered.Length; i++)
                covered[i] = raw[i] > 127;
            RenderTexture.active = prev;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(mat);
            rt.Release();
            return covered;
        }

        /// <summary>Each pass fills uncovered texels next to covered ones with their neighbours' average.</summary>
        private static void Dilate(Color32[] px, bool[] covered, int w, int h, int passes)
        {
            var next = (bool[])covered.Clone();
            for (var pass = 0; pass < passes; pass++)
            {
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var i = y * w + x;
                    if (covered[i])
                        continue;
                    int r = 0, g = 0, b = 0, a = 0, n = 0;
                    if (x > 0 && covered[i - 1]) Add(px[i - 1]);
                    if (x < w - 1 && covered[i + 1]) Add(px[i + 1]);
                    if (y > 0 && covered[i - w]) Add(px[i - w]);
                    if (y < h - 1 && covered[i + w]) Add(px[i + w]);
                    if (n == 0)
                        continue;
                    px[i] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
                    next[i] = true;

                    void Add(Color32 c)
                    {
                        r += c.r; g += c.g; b += c.b; a += c.a; n++;
                    }
                }

                System.Array.Copy(next, covered, covered.Length);
            }
        }
    }
}
