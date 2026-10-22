using Stasik.Interaction;
using UnityEngine;

namespace Stasik.EditorTools
{
    /// <summary>
    /// Anchors for a Holdable from its bounds, so any prop can be taken in the hand the way people hold things: a fist
    /// round the body of a tall one (bottles, jars, a potted flower: upright, thumb up), or from above round a low or
    /// long one (a gamepad, a sausage: the thumb along it). The hand comes in from the item's front (+Z, where the one
    /// taking it stands) with the fingers pointing into it, so items are carried turned round (front to him).
    /// </summary>
    public static class HoldableFit
    {
        // Stas's hand has no finger bones: its mesh is a loose fist, fingers bent 90 degrees towards the palm. The hollow
        // of that fist, measured from the mesh in the hand bone's frame (fingers +Y, palm -Z, thumb +X): an item's axis
        // goes through (FistX, FistY) along the thumb, PalmGap + its half-thickness in front of the palm.
        private const float FistX = -0.02f, FistY = 0.158f, PalmGap = 0.01f;
        private const float Upright = 1.3f; // taller than this times its width: held round the body

        private static readonly Vector3 CarryHand = new(0.2f, 0.12f, 0.22f);
        private static readonly Vector3 CarryEuler = new(0f, 180f, 0f);

        /// <summary>Grip, base and carry for an item with <paramref name="bounds"/> in its own (unscaled) frame.</summary>
        public static void Setup(Holdable holdable, Bounds bounds)
        {
            var upright = bounds.size.y > Upright * Mathf.Max(bounds.size.x, bounds.size.z);
            holdable.SetAnchors(Grip(bounds, upright), default,
                new LocalPose(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), Quaternion.identity), false);
            holdable.SetCarry(CarryHand, CarryEuler, 0.85f);
        }

        /// <summary>
        /// The wrist pose (the hand bone: fingers +Y, palm -Z, thumb +X) that holds the item, in the item's frame: the
        /// item's axis in the hollow of the fist. The fingers point into the item (-Z), the wrist stays on his side (+Z).
        /// </summary>
        public static LocalPose Grip(Bounds bounds, bool upright)
        {
            Vector3 axis;        // a point on the axis that goes along the thumb
            float halfThickness; // from that axis to the palm
            Quaternion rotation;
            if (upright)
            {
                // Upright in the fist, the palm on its right side (-X seen from him), thumb up, a third of the way up.
                axis = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.35f, bounds.center.z);
                halfThickness = bounds.extents.x;
                rotation = Quaternion.LookRotation(Vector3.left, Vector3.back);
            }
            else
            {
                // From above, the palm on its top, the fingers curled round its far side; the thumb along X.
                axis = bounds.center;
                halfThickness = bounds.extents.y;
                rotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
            }

            var hollow = new Vector3(FistX, FistY, -(PalmGap + halfThickness)); // in the hand bone's frame
            return new LocalPose(axis - rotation * hollow, rotation);
        }
    }
}
