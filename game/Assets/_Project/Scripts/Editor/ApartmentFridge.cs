using System.Collections.Generic;
using Stasik.Interaction;
using UnityEditor;
using UnityEngine;

namespace Stasik.EditorTools
{
    /// <summary>
    /// The fridge you can open (ApartmentFurniture.PlaceInteraction runs this). Kenney's kitchenFridgeLarge stays as
    /// the furniture (its box collider, its place), but its solid body and doors are hidden: a hollow body with shelves
    /// is built in its place, its two doors become Doors (E opens them, the hand takes the handle), and food stands on
    /// the shelves: hold spots with item prefabs built from primitives (Prefabs/Items), so he can take it and put it
    /// back. Shelves and food are data: the tables below.
    /// </summary>
    public static class ApartmentFridge
    {
        private const string HostName = "Fridge";
        private const string ItemsDir = "Assets/_Project/Prefabs/Items";

        // The model's own units (Kenney: 1 = 0.2 m). x: from its left side seen from the front, y: up, z: from the front
        // of the body backwards. The left door covers the freezer (x 0.15..2.13), the right one the fridge (2.28..5.05).
        private const float BodyWidth = 5.2f, BodyHeight = 9.2f, BodyFront = -0.2f, BodyBack = 3.42f;
        private const float Wall = 0.15f, Plinth = 0.8f, Divider = 2.2f, ShelfFront = 0.35f, ShelfThickness = 0.08f;

        // Shelf tops (y) in each compartment: (x from, x to).
        private static readonly (float From, float To, float[] Tops)[] Shelves =
        {
            (Wall, Divider - Wall / 2f, new[] { 3.4f, 6.0f }),
            (Divider + Wall / 2f, BodyWidth - Wall, new[] { 2.4f, 4.4f, 6.4f }),
        };

        // What's in it on day 1: spot name, where it stands (model units: x, shelf top, z), item prefab.
        private static readonly (string Name, Vector3 Local, string Item)[] Food =
        {
            ("Fridge_Beer1", new Vector3(2.9f, 4.4f, 0.9f), "Beer"),
            ("Fridge_Beer2", new Vector3(3.5f, 4.4f, 0.9f), "Beer"),
            ("Fridge_Beer3", new Vector3(4.1f, 4.4f, 0.9f), "Beer"),
            ("Fridge_Kolbasa", new Vector3(3.6f, 6.4f, 1.2f), "Kolbasa"),
            ("Fridge_Pickles", new Vector3(3.0f, 2.4f, 1.1f), "Pickles"),
            ("Fridge_Cheese", new Vector3(4.2f, 2.4f, 1.2f), "Cheese"),
            ("Fridge_Freezer", new Vector3(1.15f, 3.4f, 0.9f), "Beer"), // chilling in the freezer
            ("Fridge_Shelf", new Vector3(3.6f, 2.4f, 2.4f), null),      // a free place to put things back
        };

        private readonly struct Part
        {
            public readonly PrimitiveType Shape;
            public readonly Vector3 Position, Euler, Scale;
            public readonly string Material;

            public Part(PrimitiveType shape, Vector3 position, Vector3 scale, string material, Vector3 euler = default)
            {
                Shape = shape;
                Position = position;
                Euler = euler;
                Scale = scale;
                Material = material;
            }
        }

        // Food from primitives. Metres, origin at the bottom centre, front = -Z. Unity's cylinder and capsule are 2 units
        // tall: scale.y is half the height.
        private static readonly Dictionary<string, Part[]> Items = new()
        {
            ["Beer"] = new[]
            {
                new Part(PrimitiveType.Cylinder, new Vector3(0f, 0.075f, 0f), new Vector3(0.065f, 0.075f, 0.065f), "BeerGlass"),
                new Part(PrimitiveType.Cylinder, new Vector3(0f, 0.08f, 0f), new Vector3(0.068f, 0.035f, 0.068f), "BeerLabel"),
                new Part(PrimitiveType.Sphere, new Vector3(0f, 0.15f, 0f), new Vector3(0.065f, 0.04f, 0.065f), "BeerGlass"),
                new Part(PrimitiveType.Cylinder, new Vector3(0f, 0.185f, 0f), new Vector3(0.026f, 0.035f, 0.026f), "BeerGlass"),
                new Part(PrimitiveType.Cylinder, new Vector3(0f, 0.222f, 0f), new Vector3(0.03f, 0.005f, 0.03f), "Gold"),
            },
            ["Kolbasa"] = new[] // a stick of sausage lying on the shelf
            {
                new Part(PrimitiveType.Capsule, new Vector3(0f, 0.025f, 0f), new Vector3(0.05f, 0.14f, 0.05f), "Kolbasa",
                    new Vector3(0f, 0f, 90f)),
                new Part(PrimitiveType.Cylinder, new Vector3(0.155f, 0.025f, 0f), new Vector3(0.03f, 0.012f, 0.03f), "KolbasaEnd",
                    new Vector3(0f, 0f, 90f)),
            },
            ["Cheese"] = new[] // a baby gouda in red wax (a monkey's handful), a slice cut out on top
            {
                new Part(PrimitiveType.Cylinder, new Vector3(0f, 0.025f, 0f), new Vector3(0.1f, 0.025f, 0.1f), "CheeseWax"),
                new Part(PrimitiveType.Cube, new Vector3(0f, 0.052f, -0.02f), new Vector3(0.03f, 0.008f, 0.045f), "Cheese"),
            },
            ["Pickles"] = new[]
            {
                new Part(PrimitiveType.Cylinder, new Vector3(0f, 0.07f, 0f), new Vector3(0.09f, 0.07f, 0.09f), "PickleJar"),
                new Part(PrimitiveType.Cylinder, new Vector3(0f, 0.146f, 0f), new Vector3(0.094f, 0.008f, 0.094f), "Gold"),
            },
        };

        public static void Place(Transform interaction)
        {
            var host = GameObject.Find(HostName);
            if (host == null)
            {
                Debug.LogWarning($"[Stasik] No {HostName} to fill.");
                return;
            }

            Built.Clear();
            var root = new GameObject("Fridge").transform;
            root.SetParent(interaction, false);
            var front = host.transform.rotation * Vector3.back;

            // Kenney's solid body and doors out of sight; the furniture (collider, static) stays.
            var shell = host.GetComponent<MeshRenderer>().sharedMaterials;
            foreach (var r in host.GetComponentsInChildren<MeshRenderer>())
                r.enabled = false;

            BuildBody(host.transform, root, shell);
            foreach (Transform kenneyDoor in host.transform)
                BuildDoor(host, kenneyDoor, root, front);
            PlaceFood(host, root, front);
        }

        private static void BuildBody(Transform host, Transform root, Material[] shell)
        {
            var body = new GameObject("Body").transform;
            body.SetParent(root, false);
            body.SetPositionAndRotation(host.position, host.rotation);
            body.localScale = host.lossyScale;
            var inside = ApartmentFurniture.PropMaterial("FridgeInside");
            var glass = ApartmentFurniture.PropMaterial("FridgeShelf");
            var depth = BodyBack - BodyFront;
            var midZ = (BodyBack + BodyFront) / 2f;

            var up = BodyHeight / 2f;
            Box(body, "Back", new Vector3(BodyWidth / 2f, up, BodyBack - Wall / 2f), new Vector3(BodyWidth, BodyHeight, Wall), shell);
            Box(body, "SideLeft", new Vector3(Wall / 2f, up, midZ), new Vector3(Wall, BodyHeight, depth), shell);
            Box(body, "SideRight", new Vector3(BodyWidth - Wall / 2f, up, midZ), new Vector3(Wall, BodyHeight, depth), shell);
            Box(body, "Top", new Vector3(BodyWidth / 2f, BodyHeight - Wall / 2f, midZ), new Vector3(BodyWidth, Wall, depth), shell);
            Box(body, "Bottom", new Vector3(BodyWidth / 2f, Plinth / 2f, midZ), new Vector3(BodyWidth, Plinth, depth), shell);
            Box(body, "Divider", new Vector3(Divider, up, midZ), new Vector3(Wall, BodyHeight, depth), shell);
            // Light lining in front of the back wall, so the inside reads as a lit fridge.
            Box(body, "Lining", new Vector3(BodyWidth / 2f, BodyHeight / 2f, BodyBack - Wall - 0.02f),
                new Vector3(BodyWidth - 2f * Wall, BodyHeight - 2f * Wall, 0.02f), new[] { inside });

            foreach (var (from, to, tops) in Shelves)
            {
                foreach (var top in tops)
                {
                    var shelfDepth = BodyBack - Wall - ShelfFront;
                    Box(body, "Shelf", new Vector3((from + to) / 2f, top - ShelfThickness / 2f, ShelfFront + shelfDepth / 2f),
                        new Vector3(to - from, ShelfThickness, shelfDepth), new[] { glass });
                }
            }
        }

        private static void Box(Transform parent, string name, Vector3 position, Vector3 size, Material[] materials)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterials = materials;
        }

        /// <summary>
        /// One of Kenney's doors as a Door: the hinge at its pivot, the panel running along the hinge's +X (the right
        /// door runs the other way, so its hinge is turned round), opening out of the fridge (the Door's +Z).
        /// </summary>
        private static void BuildDoor(GameObject host, Transform kenneyDoor, Transform root, Vector3 front)
        {
            var filter = kenneyDoor.GetComponent<MeshFilter>();
            if (filter == null)
                return;
            var mesh = filter.sharedMesh;
            var materials = kenneyDoor.GetComponent<MeshRenderer>().sharedMaterials;
            var runsLeft = mesh.bounds.center.x < 0f; // the right door's leaf goes from its hinge to the left

            var go = new GameObject("FridgeDoor_" + kenneyDoor.name);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(kenneyDoor.position, Quaternion.LookRotation(front));

            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(go.transform, false);
            hinge.SetPositionAndRotation(kenneyDoor.position,
                kenneyDoor.rotation * Quaternion.Euler(0f, runsLeft ? 180f : 0f, 0f));

            var panel = new GameObject("Panel").transform;
            panel.SetParent(hinge, false);
            var model = ApartmentFurniture.MeshObject("Mesh", panel, mesh, materials, Vector3.one);
            model.SetPositionAndRotation(kenneyDoor.position, kenneyDoor.rotation);
            model.localScale = kenneyDoor.lossyScale;

            // The leaf (the bigger sub-mesh) in the hinge's frame; the handle is the other one.
            var leafIndex = Volume(mesh.GetSubMesh(0).bounds) >= Volume(mesh.GetSubMesh(1).bounds) ? 0 : 1;
            var box = panel.gameObject.AddComponent<BoxCollider>();
            var leaf = BoundsIn(panel, model, mesh.GetSubMesh(leafIndex).bounds);
            box.center = leaf.center;
            box.size = leaf.size;
            var body = panel.gameObject.AddComponent<Rigidbody>(); // moved by script: kinematic
            body.isKinematic = true;
            body.useGravity = false;
            var handle = new GameObject("Handle").transform;
            handle.SetParent(model, false);
            handle.localPosition = mesh.GetSubMesh(1 - leafIndex).bounds.center;

            var door = go.AddComponent<Door>();
            door.Setup(hinge, box, handle, DoorSwing.Inward);
            door.Host = host;
        }

        private static float Volume(Bounds b) => b.size.x * b.size.y * b.size.z;

        /// <summary><paramref name="local"/> (in <paramref name="from"/>'s space) as a box along <paramref name="to"/>'s axes.</summary>
        private static Bounds BoundsIn(Transform to, Transform from, Bounds local)
        {
            var result = new Bounds(to.InverseTransformPoint(from.TransformPoint(local.min)), Vector3.zero);
            for (var i = 1; i < 8; i++)
            {
                var corner = new Vector3(i % 2 == 0 ? local.min.x : local.max.x,
                    i / 2 % 2 == 0 ? local.min.y : local.max.y, i / 4 == 0 ? local.min.z : local.max.z);
                result.Encapsulate(to.InverseTransformPoint(from.TransformPoint(corner)));
            }

            return result;
        }

        private static void PlaceFood(GameObject host, Transform root, Vector3 front)
        {
            var spots = new GameObject("Shelves").transform;
            spots.SetParent(root, false);
            var stands = new GameObject("Stands").transform;
            stands.SetParent(root, false);
            foreach (var (name, local, itemName) in Food)
            {
                var spotGo = new GameObject(name);
                spotGo.transform.SetParent(spots, false);
                spotGo.transform.SetPositionAndRotation(host.transform.TransformPoint(local), Quaternion.LookRotation(front));
                var spot = spotGo.AddComponent<HoldSpot>();
                spot.SpotKind = HoldSpot.Kind.Surface;
                spot.Host = host;
                // In front of the fridge, clear of its collider, in line with the spot.
                var stand = new GameObject(name + "_Stand").transform;
                stand.SetParent(stands, false);
                var face = host.transform.TransformPoint(new Vector3(local.x, 0f, BodyFront));
                stand.position = new Vector3(face.x, 0f, face.z) + front * 0.5f;
                spot.StandPoint = stand;
                if (itemName == null)
                    continue;

                var prefab = BuildItem(itemName);
                if (prefab == null)
                    continue;
                var item = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                item.transform.SetParent(root, false);
                var holdable = item.GetComponent<Holdable>();
                holdable.SnapTo(spot.RestFor(holdable));
                PrefabUtility.RecordPrefabInstancePropertyModifications(item.transform);
            }
        }

        /// <summary>The item's prefab, (re)built from its parts: Holdable held from the top, a box collider.</summary>
        private static GameObject BuildItem(string name)
        {
            if (!Items.TryGetValue(name, out var parts))
            {
                Debug.LogWarning($"[Stasik] Unknown fridge item: {name}");
                return null;
            }

            var path = $"{ItemsDir}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && Built.Contains(name))
                return existing;

            var root = new GameObject(name);
            foreach (var part in parts)
            {
                var p = GameObject.CreatePrimitive(part.Shape);
                Object.DestroyImmediate(p.GetComponent<Collider>());
                p.name = part.Material;
                p.transform.SetParent(root.transform, false);
                p.transform.SetLocalPositionAndRotation(part.Position, Quaternion.Euler(part.Euler));
                p.transform.localScale = part.Scale;
                p.GetComponent<Renderer>().sharedMaterial = ApartmentFurniture.PropMaterial(part.Material);
            }

            var bounds = new Bounds();
            var first = true;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (first)
                    bounds = r.bounds;
                else
                    bounds.Encapsulate(r.bounds);
                first = false;
            }

            var box = root.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            HoldableFit.Setup(root.AddComponent<Holdable>(), bounds);
            if (Uses.TryGetValue(name, out var use))
            {
                // The lips get the top (drinks) or the +X end (the sausage, the cheese's rim), that axis into the mouth.
                var mouth = use.End == MouthEnd.Top
                    ? new LocalPose(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z), Quaternion.identity)
                    : new LocalPose(new Vector3(bounds.max.x, bounds.center.y, bounds.center.z),
                        Quaternion.FromToRotation(Vector3.up, Vector3.right));
                root.AddComponent<Consumable>().Setup(use.Kind, mouth, use.Tilt);
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Built.Add(name);
            return prefab;
        }

        private enum MouthEnd
        {
            Top,
            PlusX,
        }

        // What he does with it at the mouth (the use button): its end at the lips and how far that end is tipped.
        private static readonly Dictionary<string, (ConsumeKind Kind, MouthEnd End, float Tilt)> Uses = new()
        {
            ["Beer"] = (ConsumeKind.Drink, MouthEnd.Top, 115f),
            ["Pickles"] = (ConsumeKind.Drink, MouthEnd.Top, 105f), // the brine, the best part
            ["Kolbasa"] = (ConsumeKind.Eat, MouthEnd.PlusX, 90f),
            ["Cheese"] = (ConsumeKind.Eat, MouthEnd.PlusX, 90f),
        };

        // Prefabs rebuilt during this scene build (several spots share one).
        private static readonly HashSet<string> Built = new();
    }
}
