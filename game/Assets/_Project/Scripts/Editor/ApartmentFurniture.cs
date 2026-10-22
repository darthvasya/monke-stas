using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Stasik.EditorTools
{
    /// <summary>
    /// Furniture and props of the apartment, placed by ApartmentSceneBuilder.
    /// Coordinates are world metres: X right, Z towards the back of the flat (terrace at Z = 0), see docs/apartment-plan.md.
    /// Each item is placed by its bounds, not its pivot: (X, Z) is the footprint centre, Y is the bottom.
    /// Y = Drop puts the item on whatever surface is below it (sofa seat, table top, another box...).
    /// Yaw is where the item's front faces: 0 = -Z (towards the terrace), 90 = -X, -90 = +X, 180 = +Z.
    /// Models are Kenney Furniture Kit names; "prop:..." are simple props built from primitives (see BuildProp).
    /// </summary>
    public static class ApartmentFurniture
    {
        private const string KitDir = "Assets/ThirdParty/KenneyFurnitureKit";
        private const string MaterialsDir = "Assets/_Project/Art/Materials";
        private const float KitScale = 0.2f; // Kenney units -> metres
        private const float Drop = -1f;    // onto the surface below, searched from 3 m
        private const float DropLow = -2f; // same, but searched from 1.3 m: under wall cabinets and hoods

        private readonly struct Item
        {
            public readonly string Name;
            public readonly string Model;
            public readonly float X, Y, Z, Yaw, Scale;

            public Item(string name, string model, float x, float z, float yaw, float y = 0f, float scale = KitScale)
            {
                Name = name;
                Model = model;
                X = x;
                Y = y;
                Z = z;
                Yaw = yaw;
                Scale = scale;
            }
        }

        // Interaction points for Day 1: Bed, Fridge, Stove, BorschPot, Table, StreamDesk.
        private static readonly (string Room, Item[] Items)[] Layout =
        {
            ("Kitchen", new[]
            {
                new Item("Fridge", "kitchenFridgeLarge", 8.34f, 9.08f, 90f),
                new Item("Stove", "kitchenStove", 8.30f, 8.13f, 90f), // oven is built into the stove, no microwave
                new Item("Counter_1", "kitchenCabinet", 8.30f, 7.27f, 90f),
                new Item("Sink", "kitchenSink", 8.30f, 6.41f, 90f),
                new Item("Counter_2", "kitchenCabinetDrawer", 8.30f, 5.55f, 90f),
                new Item("Hood", "hoodModern", 8.47f, 8.13f, 90f, 1.55f),
                new Item("UpperCabinet_1", "kitchenCabinetUpper", 8.53f, 6.41f, 90f, 1.45f),
                new Item("UpperCabinet_2", "kitchenCabinetUpper", 8.53f, 7.27f, 90f, 1.45f),
                new Item("UpperCabinet_3", "kitchenCabinetUpper", 8.53f, 5.55f, 90f, 1.45f),
                new Item("CoffeeMachine", "kitchenCoffeeMachine", 8.45f, 7.27f, 90f, DropLow),
                new Item("Toaster", "toaster", 8.45f, 5.30f, 90f, DropLow),
                new Item("BorschPot", "prop:borschPot", 8.25f, 7.97f, 0f, DropLow),
                new Item("Trash_Kitchen", "trashcan", 8.55f, 4.85f, 90f),
            }),
            ("Dining", new[]
            {
                new Item("Table", "tableRound", 5.50f, 7.20f, 0f, 0f, 0.16f),
                new Item("Chair_NE", "chair", 6.07f, 7.77f, 45f),
                new Item("Chair_NW", "chair", 4.93f, 7.77f, -45f),
                new Item("Chair_SW", "chair", 4.93f, 6.63f, -135f),
                new Item("Chair_SE", "chair", 6.07f, 6.63f, 135f),
                new Item("TablePlant", "plantSmall1", 5.50f, 7.20f, 0f, Drop),
            }),
            ("Lounge", new[]
            {
                new Item("Sofa", "loungeSofa", 8.34f, 3.00f, 90f), // ends at the glass front
                new Item("Pillow_1", "pillow", 8.45f, 2.45f, 90f, Drop),
                new Item("Pillow_2", "pillowBlue", 8.45f, 3.55f, 90f, Drop),
                new Item("TvStand", "cabinetTelevision", 4.75f, 3.45f, -90f),
                new Item("Tv", "televisionModern", 4.70f, 3.45f, -90f, 0.62f),
                new Item("PS5", "prop:ps5", 4.65f, 4.42f, -90f),
                new Item("Gamepad", "prop:dualSense", 4.92f, 3.95f, -70f, Drop), // on the TV stand, in front of the TV
                new Item("Rug", "rugRectangle", 6.50f, 3.60f, 90f),
                new Item("FloorLamp", "lampSquareFloor", 8.60f, 4.30f, 90f),
                new Item("CoatRack", "coatRackStanding", 4.80f, 2.25f, 0f),
                new Item("Plant", "pottedPlant", 4.75f, 4.85f, 0f),
            }),
            ("Corridor", new[]
            {
                new Item("Box_1", "cardboardBoxClosed", 6.75f, 10.20f, 10f),
                new Item("Box_2", "cardboardBoxClosed", 6.75f, 10.20f, -5f, Drop),
                new Item("Box_3", "cardboardBoxOpen", 6.75f, 10.68f, 0f),
            }),
            ("StreamerRoom", new[]
            {
                new Item("StreamDesk", "desk", 2.665f, 13.49f, 0f),
                new Item("StreamChair", "chairDesk", 2.665f, 12.85f, 180f),
                new Item("Monitor", "computerScreen", 2.665f, 13.62f, 0f, Drop),
                new Item("Keyboard", "computerKeyboard", 2.665f, 13.30f, 0f, Drop),
                new Item("Mouse", "computerMouse", 3.05f, 13.30f, 0f, Drop),
                new Item("Speaker_L", "speakerSmall", 2.10f, 13.62f, 0f, Drop),
                new Item("Speaker_R", "speakerSmall", 3.23f, 13.62f, 0f, Drop),
                new Item("StreamRug", "rugRound", 2.665f, 12.75f, 0f, 0f, 0.13f),
                new Item("Bookcase", "bookcaseOpen", 1.67f, 12.40f, -90f),
                new Item("Books", "books", 1.67f, 12.40f, -90f, Drop),
                new Item("Trash_Stream", "trashcan", 3.70f, 13.55f, 90f),
            }),
            ("Bedroom", new[]
            {
                new Item("Bed", "bedDouble", 6.595f, 12.60f, 90f),
                new Item("BedPillow_1", "pillowLong", 7.40f, 12.60f, 90f, Drop),
                new Item("Bear", "bear", 6.90f, 13.10f, 60f, Drop, 0.08f),
                new Item("Nightstand", "cabinetBed", 7.505f, 11.35f, 90f),
                new Item("NightLamp", "lampRoundTable", 7.505f, 11.35f, 90f, Drop),
                new Item("Wardrobe", "bookcaseClosedDoors", 4.31f, 12.90f, -90f),
                new Item("BedRug", "rugRounded", 5.00f, 12.55f, 90f, 0f, 0.09f),
                new Item("BedPlant", "pottedPlant", 4.32f, 13.62f, 0f),
            }),
            ("Bathroom", new[]
            {
                new Item("Shower", "shower", 2.00f, 8.54f, 180f), // glass doors face the room
                new Item("Toilet", "toilet", 3.05f, 8.44f, 180f),
                new Item("BathSink", "bathroomSink", 4.06f, 8.35f, 90f), // pedestal sink, stands on the floor
                new Item("BathMirror", "bathroomMirror", 4.21f, 8.35f, 90f, 1.20f),
                new Item("Washer", "washer", 3.94f, 9.26f, 0f),
                new Item("BathMat", "rugDoormat", 2.00f, 9.38f, 0f),
            }),
            ("Terrace", new[]
            {
                new Item("Doormat", "rugDoormat", 5.66f, 1.55f, 0f),
                new Item("Bench", "benchCushion", 2.60f, 1.30f, 0f),
                new Item("TerraceTable", "sideTable", 3.70f, 1.35f, 0f),
                new Item("TerracePlant_1", "pottedPlant", 0.40f, 1.45f, 0f),
                new Item("TerracePlant_2", "pottedPlant", 11.30f, 1.45f, 0f),
            }),
        };

        private static readonly List<MeshCollider> TempColliders = new();

        public static void Place()
        {
            var root = new GameObject("Furniture").transform;
            foreach (var (roomName, items) in Layout)
            {
                var room = new GameObject(roomName).transform;
                room.SetParent(root, false);
                foreach (var item in items)
                    PlaceItem(item, room);
            }

            foreach (var c in TempColliders)
                Object.DestroyImmediate(c);
            TempColliders.Clear();
        }

        private static void PlaceItem(Item item, Transform parent)
        {
            GameObject go;
            if (item.Model.StartsWith("prop:"))
            {
                go = BuildProp(item.Model.Substring(5));
                if (go == null)
                    return;
                go.transform.SetParent(parent, false);
                go.transform.rotation = Quaternion.Euler(0f, item.Yaw, 0f);
            }
            else
            {
                var path = $"{KitDir}/{item.Model}.fbx";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null)
                {
                    Debug.LogWarning($"[Stasik] Furniture model not found: {path}");
                    return;
                }

                go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
                go.transform.localScale = model.transform.localScale * item.Scale;
                UseProjectGlass(go);
                // Keep the model's own root rotation (some Kenney models have one baked in) under our yaw.
                go.transform.rotation = Quaternion.Euler(0f, item.Yaw, 0f) * model.transform.rotation;
            }

            go.name = item.Name;
            var t = go.transform;
            t.position = Vector3.zero;
            var bounds = WorldBounds(go);
            var bottom = item.Y == Drop ? SurfaceBelow(item.X, item.Z, 3f)
                : item.Y == DropLow ? SurfaceBelow(item.X, item.Z, 1.3f)
                : item.Y;
            t.position = new Vector3(item.X - bounds.center.x, bottom - bounds.min.y, item.Z - bounds.center.z);

            // Simple box collider so the player bumps into furniture. Measured in the item's own axes: world-axis
            // bounds of a rotated item (the 45° chairs) turn into a paper-thin box when mapped back to local space.
            var localBounds = LocalBounds(go);
            var box = go.AddComponent<BoxCollider>();
            box.center = localBounds.center;
            box.size = localBounds.size;

            // Exact mesh colliders, only while placing, so later items can be dropped onto this one.
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                TempColliders.Add(mc);
            }

            foreach (var child in go.GetComponentsInChildren<Transform>())
                child.gameObject.isStatic = true;
        }

        /// <summary>Bounds of all meshes of the item in the item root's local space.</summary>
        private static Bounds LocalBounds(GameObject go)
        {
            var root = go.transform;
            var first = true;
            var result = new Bounds();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null)
                    continue;
                var b = mf.sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (first)
                    {
                        result = new Bounds(p, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        result.Encapsulate(p);
                    }
                }
            }

            return result;
        }

        // Kenney's "glass" material imports opaque; swap it for the project's transparent glass.
        private static void UseProjectGlass(GameObject go)
        {
            var glass = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsDir}/M_Glass.mat");
            if (glass == null)
                return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                var changed = false;
                for (var i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].name == "glass")
                    {
                        mats[i] = glass;
                        changed = true;
                    }
                }

                if (changed)
                    r.sharedMaterials = mats;
            }
        }

        private static float SurfaceBelow(float x, float z, float from)
        {
            Physics.SyncTransforms();
            var top = 0f;
            foreach (var hit in Physics.RaycastAll(new Vector3(x, from, z), Vector3.down, from + 1f))
            {
                if (hit.collider is MeshCollider && TempColliders.Contains((MeshCollider)hit.collider))
                    top = Mathf.Max(top, hit.point.y);
            }

            return top;
        }

        private static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);
            return bounds;
        }

        // ------------------------------------------------------------------ primitive props
        private readonly struct Part
        {
            public readonly PrimitiveType Shape;
            public readonly Vector3 Position, Scale;
            public readonly string Material;

            public Part(PrimitiveType shape, Vector3 position, Vector3 scale, string material)
            {
                Shape = shape;
                Position = position;
                Scale = scale;
                Material = material;
            }
        }

        // Props that are not in the Kenney kit. Local metres, origin at the bottom centre, front = -Z.
        private static readonly Dictionary<string, Part[]> Props = new()
        {
            ["ps5"] = new[] // standing console: white side panels around a black core
            {
                new Part(PrimitiveType.Cube, new Vector3(0f, 0.01f, 0f), new Vector3(0.12f, 0.02f, 0.14f), "PropBlack"),
                new Part(PrimitiveType.Cube, new Vector3(0f, 0.205f, 0f), new Vector3(0.05f, 0.37f, 0.24f), "PropBlack"),
                new Part(PrimitiveType.Cube, new Vector3(-0.04f, 0.215f, 0f), new Vector3(0.03f, 0.39f, 0.26f), "PropWhite"),
                new Part(PrimitiveType.Cube, new Vector3(0.04f, 0.215f, 0f), new Vector3(0.03f, 0.39f, 0.26f), "PropWhite"),
            },
            ["dualSense"] = new[]
            {
                new Part(PrimitiveType.Cube, new Vector3(0f, 0.02f, 0f), new Vector3(0.16f, 0.04f, 0.065f), "PropWhite"),
                new Part(PrimitiveType.Cube, new Vector3(-0.06f, 0.02f, 0.04f), new Vector3(0.045f, 0.04f, 0.06f), "PropWhite"),
                new Part(PrimitiveType.Cube, new Vector3(0.06f, 0.02f, 0.04f), new Vector3(0.045f, 0.04f, 0.06f), "PropWhite"),
                new Part(PrimitiveType.Cube, new Vector3(0f, 0.042f, -0.005f), new Vector3(0.06f, 0.004f, 0.035f), "PropBlack"),
            },
            ["borschPot"] = new[] // Unity cylinder is 2 units tall: scale.y is half the height
            {
                new Part(PrimitiveType.Cylinder, new Vector3(0f, 0.09f, 0f), new Vector3(0.26f, 0.09f, 0.26f), "PotMetal"),
                new Part(PrimitiveType.Cylinder, new Vector3(0f, 0.176f, 0f), new Vector3(0.24f, 0.004f, 0.24f), "Borsch"),
                new Part(PrimitiveType.Cube, new Vector3(-0.15f, 0.15f, 0f), new Vector3(0.05f, 0.02f, 0.03f), "PotMetal"),
                new Part(PrimitiveType.Cube, new Vector3(0.15f, 0.15f, 0f), new Vector3(0.05f, 0.02f, 0.03f), "PotMetal"),
            },
        };

        private static readonly Dictionary<string, (Color Color, float Metallic, float Smoothness)> PropMaterials = new()
        {
            ["PropWhite"] = (new Color(0.95f, 0.95f, 0.95f), 0f, 0.6f),
            ["PropBlack"] = (new Color(0.08f, 0.08f, 0.09f), 0f, 0.7f),
            ["PotMetal"] = (new Color(0.55f, 0.56f, 0.58f), 0.8f, 0.6f),
            ["Borsch"] = (new Color(0.61f, 0.09f, 0.16f), 0f, 0.8f),
        };

        private static GameObject BuildProp(string id)
        {
            if (!Props.TryGetValue(id, out var parts))
            {
                Debug.LogWarning($"[Stasik] Unknown prop: {id}");
                return null;
            }

            var root = new GameObject(id);
            foreach (var part in parts)
            {
                var p = GameObject.CreatePrimitive(part.Shape);
                Object.DestroyImmediate(p.GetComponent<Collider>());
                p.transform.SetParent(root.transform, false);
                p.transform.localPosition = part.Position;
                p.transform.localScale = part.Scale;
                p.GetComponent<Renderer>().sharedMaterial = PropMaterial(part.Material);
            }

            return root;
        }

        private static Material PropMaterial(string name)
        {
            var path = $"{MaterialsDir}/M_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }

            var (color, metallic, smoothness) = PropMaterials[name];
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
