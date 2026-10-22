using UnityEngine;

namespace Stasik.Interaction
{
    /// <summary>What a character can do with a target; a future prompt maps it to text (no text in code).</summary>
    public enum InteractVerb
    {
        None,
        Take,
        Hang,
        Put,
        PutOn,
        TakeOff,
        Turn,
        Open,
        Close,
        Sit,
    }

    /// <summary>A pose relative to an object, editable in the inspector.</summary>
    [System.Serializable]
    public struct LocalPose
    {
        public Vector3 Position;
        public Vector3 Euler;

        public LocalPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Euler = rotation.eulerAngles;
        }

        public Quaternion Rotation => Quaternion.Euler(Euler);

        /// <summary>This pose (given in <paramref name="parent"/>'s space) in world space.</summary>
        public Pose Of(Pose parent) => new(parent.position + parent.rotation * Position, parent.rotation * Rotation);

        /// <summary>The parent pose that puts this pose at <paramref name="world"/>.</summary>
        public Pose ParentFor(Pose world)
        {
            var rotation = world.rotation * Quaternion.Inverse(Rotation);
            return new Pose(world.position - rotation * Position, rotation);
        }
    }

    /// <summary>
    /// Where an item rests and which way a hand lifts it off / brings it in: a spot, a head, a point on a surface, or
    /// the hand itself (carried).
    /// </summary>
    public readonly struct ItemRest
    {
        public readonly Transform Parent;          // spot, head bone; null = world
        public readonly Vector3 LocalPosition;
        public readonly Quaternion LocalRotation;
        public readonly Vector3 Exit;              // world offset the hand lifts the item off along
        public readonly bool BySide;               // the hand passes beside the head (a head rest)
        public readonly HoldSpot Spot;             // the spot it occupies, if any
        public readonly bool InHand;

        private ItemRest(Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 exit, bool bySide,
            HoldSpot spot, bool inHand)
        {
            Parent = parent;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
            Exit = exit;
            BySide = bySide;
            Spot = spot;
            InHand = inHand;
        }

        public static ItemRest Hand => new(null, Vector3.zero, Quaternion.identity, Vector3.zero, false, null, true);

        public static ItemRest At(Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 exit,
            bool bySide = false, HoldSpot spot = null) =>
            new(parent, localPosition, localRotation, exit, bySide, spot, false);

        public static ItemRest InWorld(Pose pose, Vector3 exit) => new(null, pose.position, pose.rotation, exit, false, null, false);

        /// <summary>Evaluated now: a head moves with the animation.</summary>
        public Pose World => Parent == null
            ? new Pose(LocalPosition, LocalRotation)
            : new Pose(Parent.TransformPoint(LocalPosition), Parent.rotation * LocalRotation);
    }
}
