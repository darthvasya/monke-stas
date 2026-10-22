using Stasik.Characters;
using Stasik.Interaction;
using UnityEngine;

namespace Stasik.Player
{
    /// <summary>
    /// The use button with something to eat or drink in the hand: the hand brings it up to the lips; a drink is tipped
    /// up for a few glugs, the head going back with it; food gets three bites; then back down to carrying. No clips: the
    /// item is placed at the mouth and the wrist follows from its grip. While this runs the hand doesn't carry the item,
    /// so PlayerInteract keeps it in the fist (LateUpdate), tilts the head (HeadPitch) and ends it (Done).
    /// </summary>
    internal sealed class HeldItemUse : CharacterHand.IGoalSource
    {
        private const float Raise = 0.45f, Tip = 0.3f, Glug = 1.4f, Lower = 0.45f; // s, drinking
        private const float Bite = 0.3f, Chew = 0.35f;                             // s, eating
        private const int Bites = 3;
        private const float AtLips = 35f;    // degrees the drink is tipped on its way up and down
        private const float HeadBack = 18f;  // degrees the head goes back while drinking
        private const float BiteNod = 6f;    // degrees the head comes forward for a bite
        private const float BiteReach = 0.03f; // m the food comes and goes for each bite
        private const float WristBend = 53f;   // just inside the hand's own 55-degree wrist limit

        // The lips in the head bone's frame (+Z out of the face, +Y up), measured from the mesh.
        private static readonly Vector3 Lips = new(0f, 0.025f, 0.165f);
        // The way up bends out in front of him (body frame, added to the half-way point).
        private static readonly Vector3 Arc = new(0.06f, -0.05f, 0.15f);
        // At the mouth the forearm comes up from the elbow, out to his right and in front (body frame: x right, y up,
        // z forward); the fist wraps the item across it, so the wrist stays nearly straight.
        private static readonly Vector3 DrinkForearm = new(-0.7f, 0.35f, 0.6f);
        private static readonly Vector3 EatForearm = new(-0.8f, 0.45f, 0.4f);

        private readonly Holdable _item;
        private readonly Consumable _food;
        private float _time;

        public HeldItemUse(Holdable item, Consumable food)
        {
            _item = item;
            _food = food;
        }

        /// <summary>The frame the hand last asked for a goal: if it stops asking, this was dropped.</summary>
        public int AskedFrame { get; private set; } = -1;

        /// <summary>Back in the carrying pose: time to hand the arm back.</summary>
        public bool Done => _time >= Total;

        /// <summary>Degrees to turn the head about his right (negative = looking up).</summary>
        public float HeadPitch { get; private set; }

        private bool Drinking => _food.Kind == ConsumeKind.Drink;

        private float Total => Drinking ? Raise + Tip + Glug + Tip + Lower : Raise + Bites * Bite + Chew + Lower;

        public bool GetHandGoal(CharacterHand hand, out CharacterHand.Goal goal)
        {
            goal = default;
            AskedFrame = Time.frameCount;
            if (hand.Held != _item || hand.Head == null || hand.Hips == null)
                return false;

            _time += Time.deltaTime;
            var body = hand.BodyRotation;
            var carryWrist = new Pose(hand.Hips.position + body * _item.CarryHand,
                body * _item.CarryRotation * _item.Grip.Rotation);
            var carry = _item.Grip.ParentFor(carryWrist);

            Pose item;
            var rise = 1f; // 0 = carrying, 1 = at the mouth (the arm's IK weight goes with it)
            HeadPitch = 0f;
            var t = _time;
            var tip = _food.Tilt;
            if (Drinking)
            {
                var lips = AtMouth(hand, AtLips, 0f);
                if (t < Raise)
                    item = Up(carry, lips, rise = Smooth(t / Raise), body);
                else if ((t -= Raise) < Tip)
                {
                    item = AtMouth(hand, Mathf.Lerp(AtLips, tip, Smooth(t / Tip)), 0f);
                    HeadPitch = -HeadBack * Smooth(t / Tip);
                }
                else if ((t -= Tip) < Glug)
                {
                    var glug = Mathf.Sin(t * Mathf.PI * 2f * 2.5f);
                    item = AtMouth(hand, tip + 4f * glug, 0.004f * glug);
                    HeadPitch = -HeadBack - 2f * glug;
                }
                else if ((t -= Glug) < Tip)
                {
                    item = AtMouth(hand, Mathf.Lerp(tip, AtLips, Smooth(t / Tip)), 0f);
                    HeadPitch = -HeadBack * (1f - Smooth(t / Tip));
                }
                else if ((t -= Tip) < Lower)
                    item = Up(carry, lips, rise = 1f - Smooth(t / Lower), body);
                else
                    item = Up(carry, lips, rise = 0f, body);
            }
            else
            {
                var away = AtMouth(hand, tip, -BiteReach);
                if (t < Raise)
                    item = Up(carry, away, rise = Smooth(t / Raise), body);
                else if ((t -= Raise) < Bites * Bite)
                {
                    var bite = Mathf.Sin(Mathf.PI * (t % Bite / Bite));
                    item = AtMouth(hand, tip, -BiteReach + (BiteReach + 0.01f) * bite);
                    HeadPitch = BiteNod * bite;
                }
                else if ((t -= Bites * Bite) < Chew)
                {
                    item = away;
                    HeadPitch = 2f * Mathf.Sin(t / Chew * Mathf.PI * 3f); // chewing: three half-bobs, 0 at both ends
                }
                else if ((t -= Chew) < Lower)
                    item = Up(carry, away, rise = 1f - Smooth(t / Lower), body);
                else
                    item = Up(carry, away, rise = 0f, body);
            }

            var weight = Mathf.Lerp(_item.CarryWeight, 1f, rise);
            var wrist = _item.GripAt(item);
            var elbow = wrist.position - Forearm(hand) * 0.3f + Vector3.up * 0.05f;
            goal = new CharacterHand.Goal
            {
                Position = wrist.position,
                Rotation = wrist.rotation,
                ElbowHint = Vector3.Lerp(hand.ElbowHintFor(wrist.position), elbow, rise),
                Weight = weight,
            };
            return true;
        }

        /// <summary>
        /// The item with its mouth end at his lips, <paramref name="push"/> m further in, that end's axis tipped
        /// <paramref name="tilt"/> degrees from his head's up back into the mouth; the fist round it across the forearm.
        /// </summary>
        private Pose AtMouth(CharacterHand hand, float tilt, float push)
        {
            var head = hand.Head;
            var mouthAxis = _food.Mouth.Rotation * Vector3.up;
            var into = Quaternion.AngleAxis(-tilt, head.right) * head.up;
            var fingers = Vector3.ProjectOnPlane(Forearm(hand), into);
            var rotation = Quaternion.LookRotation(-fingers, into) * Quaternion.FromToRotation(mouthAxis, Vector3.up);
            var lips = head.TransformPoint(Lips) + into * push;
            return WithinWrist(hand, lips, rotation);
        }

        /// <summary>
        /// The hand bends the fingers back to within 55 degrees of the forearm, turning about the wrist: that would swing
        /// the mouth end off the lips. Turn the item that far here instead, about its mouth end, so the hand's limit has
        /// nothing left to do. A few passes, as the wrist moves when the item turns.
        /// </summary>
        private Pose WithinWrist(CharacterHand hand, Vector3 lips, Quaternion rotation)
        {
            var elbow = hand.Animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            for (var i = 0; elbow != null && i < 4; i++)
            {
                var wrist = _item.GripAt(new Pose(lips - rotation * _food.Mouth.Position, rotation));
                var forearm = wrist.position - elbow.position;
                if (forearm.sqrMagnitude < 1e-6f)
                    break;
                var fingers = wrist.rotation * Vector3.up;
                var limited = Vector3.RotateTowards(forearm.normalized, fingers, WristBend * Mathf.Deg2Rad, 0f);
                rotation = Quaternion.FromToRotation(fingers, limited) * rotation;
            }

            return new Pose(lips - rotation * _food.Mouth.Position, rotation);
        }

        private Vector3 Forearm(CharacterHand hand) =>
            (hand.BodyRotation * (Drinking ? DrinkForearm : EatForearm)).normalized;

        /// <summary>Between carrying (<paramref name="s"/> = 0) and the mouth (1), bowed out in front.</summary>
        private static Pose Up(Pose carry, Pose mouth, float s, Quaternion body)
        {
            var control = Vector3.Lerp(carry.position, mouth.position, 0.5f) + body * Arc;
            var position = Vector3.Lerp(Vector3.Lerp(carry.position, control, s),
                Vector3.Lerp(control, mouth.position, s), s);
            return new Pose(position, Quaternion.Slerp(carry.rotation, mouth.rotation, s));
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
