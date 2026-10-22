using UnityEngine;

namespace Stasik.Interaction
{
    public enum ConsumeKind
    {
        Drink, // tipped up at the lips: beer, pickle brine
        Eat,   // bitten: sausage, cheese
    }

    /// <summary>
    /// A held item he can drink from or bite (the use button). Only data: which end goes to the lips and how far it's
    /// tipped there; the motion is the player's.
    /// </summary>
    [RequireComponent(typeof(Holdable))]
    [DisallowMultipleComponent]
    public class Consumable : MonoBehaviour
    {
        [SerializeField] private ConsumeKind _kind;
        [SerializeField] private LocalPose _mouth;                    // item space: the point at the lips, +Y = into the mouth
        [SerializeField, Range(0f, 150f)] private float _tilt = 115f; // that +Y off straight up while drinking/biting

        public ConsumeKind Kind => _kind;
        public LocalPose Mouth => _mouth;
        public float Tilt => _tilt;

        public void Setup(ConsumeKind kind, LocalPose mouth, float tilt)
        {
            _kind = kind;
            _mouth = mouth;
            _tilt = tilt;
        }
    }
}
