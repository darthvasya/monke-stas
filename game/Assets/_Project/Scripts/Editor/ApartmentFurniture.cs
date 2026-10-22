using System.Collections.Generic;
using Stasik.Interaction;
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
                new Item("Fridge", "kitchenFridgeLarge", 10.55f, 11.51f, 90f),
                new Item("Stove", "kitchenStove", 10.51f, 10.56f, 90f), // oven is built into the stove, no microwave
                new Item("Counter_1", "kitchenCabinet", 10.51f, 9.7f, 90f),
                new Item("Sink", "kitchenSink", 10.51f, 8.84f, 90f),
                new Item("Counter_2", "kitchenCabinetDrawer", 10.51f, 7.98f, 90f),
                new Item("Hood", "hoodModern", 10.68f, 10.56f, 90f, 1.55f),
                new Item("UpperCabinet_1", "kitchenCabinetUpper", 10.74f, 8.84f, 90f, 1.45f),
                new Item("UpperCabinet_2", "kitchenCabinetUpper", 10.74f, 9.7f, 90f, 1.45f),
                new Item("UpperCabinet_3", "kitchenCabinetUpper", 10.74f, 7.98f, 90f, 1.45f),
                new Item("CoffeeMachine", "kitchenCoffeeMachine", 10.66f, 9.7f, 90f, DropLow),
                new Item("Toaster", "toaster", 10.66f, 7.73f, 90f, DropLow),
                new Item("BorschPot", "prop:borschPot", 10.46f, 10.4f, 0f, DropLow),
                new Item("Trash_Kitchen", "trashcan", 10.76f, 7.28f, 90f),
            }),
            ("Dining", new[]
            {
                new Item("Table", "tableRound", 6.874f, 9.004f, 0f, 0f, 0.16f),
                new Item("Chair_NE", "chair", 7.444f, 9.574f, 45f),
                new Item("Chair_NW", "chair", 6.304f, 9.574f, -45f),
                new Item("Chair_SW", "chair", 6.304f, 8.434f, -135f),
                new Item("Chair_SE", "chair", 7.444f, 8.434f, 135f),
                new Item("TablePlant", "plantSmall1", 6.874f, 9.004f, 0f, Drop),
            }),
            ("Lounge", new[]
            {
                new Item("Sofa", "loungeSofa", 10.55f, 3.47f, 90f), // ends at the glass front
                new Item("Pillow_1", "pillow", 10.66f, 2.92f, 90f, Drop),
                new Item("Pillow_2", "pillowBlue", 10.66f, 4.02f, 90f, Drop),
                new Item("TvStand", "cabinetTelevision", 5.85f, 3.92f, -90f),
                new Item("Tv", "televisionModern", 5.8f, 3.92f, -90f, 0.62f),
                new Item("PS5", "prop:ps5", 5.75f, 4.89f, -90f),
                new Item("Gamepad", "prop:dualSense", 6.02f, 4.42f, -70f, Drop), // on the TV stand, in front of the TV
                new Item("Rug", "rugRectangle", 8.124f, 4.503f, 90f),
                new Item("FloorLamp", "lampSquareFloor", 10.81f, 4.77f, 90f),
                new Item("CoatRack", "coatRackStanding", 5.9f, 2.72f, 0f),
                new Item("Plant", "pottedPlant", 5.85f, 5.32f, 0f),
            }),
            ("Corridor", new[]
            {
                new Item("Box_1", "cardboardBoxClosed", 8.52f, 12.63f, 10f),
                new Item("Box_2", "cardboardBoxClosed", 8.52f, 12.63f, -5f, Drop),
                new Item("Box_3", "cardboardBoxOpen", 8.52f, 13.57f, 0f),
            }),
            ("StreamerRoom", new[]
            {
                new Item("StreamDesk", "desk", 3.33f, 17.12f, 0f),
                new Item("StreamChair", "chairDesk", 3.33f, 16.48f, 180f),
                new Item("Monitor", "computerScreen", 3.33f, 17.25f, 0f, Drop),
                new Item("Keyboard", "computerKeyboard", 3.33f, 16.93f, 0f, Drop),
                new Item("Mouse", "computerMouse", 3.715f, 16.93f, 0f, Drop),
                new Item("Speaker_L", "speakerSmall", 2.765f, 17.25f, 0f, Drop),
                new Item("Speaker_R", "speakerSmall", 3.895f, 17.25f, 0f, Drop),
                new Item("StreamRug", "rugRound", 3.33f, 16.38f, 0f, 0f, 0.13f),
                new Item("Bookcase", "bookcaseOpen", 2f, 15.66f, -90f),
                new Item("Books", "books", 2f, 15.66f, -90f, Drop),
                new Item("Trash_Stream", "trashcan", 4.7f, 17.18f, 90f),
            }),
            ("Bedroom", new[]
            {
                new Item("Bed", "bedDouble", 8.555f, 15.49f, 90f),
                new Item("BedPillow_1", "pillowLong", 9.36f, 15.49f, 90f, Drop),
                new Item("Bear", "bear", 8.86f, 15.99f, 60f, Drop, 0.08f),
                new Item("Nightstand", "cabinetBed", 9.465f, 14.24f, 90f),
                new Item("NightLamp", "lampRoundTable", 9.465f, 14.24f, 90f, Drop),
                new Item("Wardrobe", "bookcaseClosedDoors", 5.31f, 16.16f, -90f),
                new Item("BedRug", "rugRounded", 6.249f, 15.81f, 90f, 0f, 0.09f),
                new Item("BedPlant", "pottedPlant", 5.32f, 17.25f, 0f),
            }),
            ("Bathroom", new[]
            {
                new Item("Shower", "shower", 2.33f, 10.36f, 180f), // glass doors face the room
                new Item("Toilet", "toilet", 3.38f, 10.26f, 180f),
                new Item("BathSink", "bathroomSink", 5.16f, 10.17f, 90f), // pedestal sink, stands on the floor
                new Item("BathMirror", "bathroomMirror", 5.31f, 10.17f, 90f, 1.20f),
                new Item("Washer", "washer", 5.04f, 11.69f, 0f),
                new Item("BathMat", "rugDoormat", 2.33f, 11.2f, 0f),
            }),
            ("Terrace", new[]
            {
                new Item("Doormat", "rugDoormat", 7.075f, 2.02f, 0f),
                new Item("Bench", "benchCushion", 3.251f, 1.627f, 0f),
                new Item("TerraceTable", "sideTable", 4.626f, 1.689f, 0f),
                new Item("TerracePlant_1", "pottedPlant", 0.4f, 1.81f, 0f),
                new Item("TerracePlant_2", "pottedPlant", 14.24f, 1.81f, 0f),
            }),
        };

        private static readonly List<MeshCollider> TempColliders = new();

        // Places items rest on (Stasik.Interaction.HoldSpot), and what lies there when the day starts. Position = the
        // spot (a hook: where the strap hangs), Yaw: the spot's +Z = out of the hook / towards the person using it.
        // Stand = where he stands to use it (clear of the furniture for his 0.34 m capsule).
        private static readonly (string Name, string Host, Vector3 Position, float Yaw, HoldSpot.Kind Kind, Vector3 Stand, string Start)[] Spots =
        {
            // The cap hangs by its strap at the tip of the coat rack's arm towards the room (+X): the pegs on top (1.5 m)
            // are out of the monkey's reach, the arms (1.25 m) are not; the other arms point at the TV stand and the facade.
            ("CapHook", "CoatRack", new Vector3(6.12f, 1.245f, 2.715f), 90f, HoldSpot.Kind.Hook, new Vector3(6.55f, 0f, 2.87f), CapBuilder.PrefabPath),
        };

        // Doors in the interior doorways (tools/build_apartment.py, OPENINGS): Kenney doorway frame + panel, stretched
        // to the opening. Position = the middle of the doorway on the floor (wall centre line). Yaw: the door's +Z
        // points into the room (0 = +Z, rooms north of the corridor; 180 = -Z, south of it). Hinge: the jamb it hangs
        // on, seen from the corridor looking into the room. Swing: Inward = into the room only, so open doors never
        // block the corridor (from inside it's a pull: he steps aside); Away = away from whoever opens it.
        private enum Hinge
        {
            Left,
            Right,
        }

        private const string DoorModel = "doorway";
        private const float DoorHeight = 2.1f;
        private const float FrameInset = 0.015f; // the frame's inside, in from the opening's cut edges

        private static readonly (string Name, Vector3 Position, float Width, float Yaw, Hinge Hinge, DoorSwing Swing)[] Doors =
        {
            ("Door_StreamerRoom", new Vector3(2.81f, 0f, 13.93f), 0.9f, 0f, Hinge.Right, DoorSwing.Inward),
            ("Door_Bedroom", new Vector3(6.875f, 0f, 13.93f), 0.9f, 0f, Hinge.Left, DoorSwing.Inward), // swings clear of the bed
            ("Door_Shower", new Vector3(3.125f, 0f, 12.13f), 0.9f, 180f, Hinge.Right, DoorSwing.Inward),
            ("Door_Living", new Vector3(7.125f, 0f, 12.13f), 1.0f, 180f, Hinge.Left, DoorSwing.Inward),
        };

        /// <summary>
        /// The Interaction root (unscaled, not static, unlike the furniture): doors, hold spots and the items on them.
        /// Run after Place() (spots name their furniture) and after the Player prefab build (it writes Cap.prefab).
        /// </summary>
        public static void PlaceInteraction()
        {
            var root = new GameObject("Interaction").transform;
            var spots = new GameObject("Spots").transform;
            spots.SetParent(root, false);
            var items = new GameObject("Items").transform;
            items.SetParent(root, false);
            PlaceDoors(root);
            foreach (var (name, host, position, yaw, kind, stand, start) in Spots)
            {
                var spotGo = new GameObject(name);
                spotGo.transform.SetParent(spots, false);
                spotGo.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
                var spot = spotGo.AddComponent<HoldSpot>();
                spot.SpotKind = kind;
                spot.Host = GameObject.Find(host);
                var standPoint = new GameObject("Stand").transform;
                standPoint.SetParent(spotGo.transform, false);
                standPoint.position = stand;
                spot.StandPoint = standPoint;
                if (string.IsNullOrEmpty(start))
                    continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(start);
                if (prefab == null)
                {
                    Debug.LogWarning($"[Stasik] {start} is missing: build the Player prefab first (it writes the cap).");
                    continue;
                }

                var item = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                item.transform.SetParent(items, false);
                var holdable = item.GetComponent<Holdable>();
                holdable.SnapTo(spot.RestFor(holdable));
                PrefabUtility.RecordPrefabInstancePropertyModifications(item.transform);
            }
        }

        private static void PlaceDoors(Transform root)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitDir}/{DoorModel}.fbx");
            if (model == null)
            {
                Debug.LogWarning($"[Stasik] Door model not found: {KitDir}/{DoorModel}.fbx");
                return;
            }

            // The kit's doorway: the frame is the root mesh (origin at a corner), the panel its child "door" (origin at
            // the hinge, runs along +X; sub-mesh 0 is the leaf, 1 the handles).
            var frameMesh = model.GetComponent<MeshFilter>().sharedMesh;
            var frameMats = model.GetComponent<MeshRenderer>().sharedMaterials;
            var door = model.transform.Find("door");
            var panelMesh = door.GetComponent<MeshFilter>().sharedMesh;
            var panelMats = door.GetComponent<MeshRenderer>().sharedMaterials;
            var leaf = panelMesh.GetSubMesh(0).bounds;
            var handle = panelMesh.GetSubMesh(1).bounds.center;

            var doors = new GameObject("Doors").transform;
            doors.SetParent(root, false);
            foreach (var (name, position, width, yaw, hinge, swing) in Doors)
            {
                // Stretched so the leaf (= the frame's inside) is a bit inside the opening: the frame then covers the cut
                // edges of the wall instead of sharing their planes (those flickered, z-fighting).
                var leafWidth = width - 2f * FrameInset;
                var scale = new Vector3(leafWidth / leaf.size.x, (DoorHeight - FrameInset) / leaf.size.y, KitScale);
                var go = new GameObject(name);
                go.transform.SetParent(doors, false);
                go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

                var frame = MeshObject("Frame", go.transform, frameMesh, frameMats, scale);
                var fb = frameMesh.bounds;
                frame.localPosition = -Vector3.Scale(scale, new Vector3(fb.center.x, 0f, fb.center.z));

                var hingeT = new GameObject("Hinge").transform;
                hingeT.SetParent(go.transform, false);
                var right = hinge == Hinge.Right;
                hingeT.localPosition = new Vector3((right ? 1f : -1f) * leafWidth / 2f, 0f, 0f);
                hingeT.localRotation = Quaternion.Euler(0f, right ? 180f : 0f, 0f);

                var panel = MeshObject("Panel", hingeT, panelMesh, panelMats, scale);
                var box = panel.gameObject.AddComponent<BoxCollider>();
                box.center = leaf.center;
                box.size = leaf.size;
                var body = panel.gameObject.AddComponent<Rigidbody>(); // moved by script: kinematic
                body.isKinematic = true;
                body.useGravity = false;
                var handleT = new GameObject("Handle").transform;
                handleT.SetParent(panel, false);
                handleT.localPosition = new Vector3(handle.x, handle.y, 0f);

                go.AddComponent<Door>().Setup(hingeT, box, handleT, swing);
            }
        }

        private static Transform MeshObject(string name, Transform parent, Mesh mesh, Material[] materials, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return go.transform;
        }

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
