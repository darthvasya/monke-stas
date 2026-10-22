using UnityEngine;

namespace Stasik.Interaction
{
    /// <summary>
    /// A place to sit: a chair, a place on the sofa, the bench. Its frame: on the seat's surface where the sitter's
    /// bottom goes, +Z = the way the sitter faces. StandPoint: where he stands before sitting down and after getting up.
    /// One sitter at a time.
    /// </summary>
    public class Seat : InteractionTarget
    {
        private object _occupant;

        public bool IsFree => _occupant == null;

        // Where the sitter's hips go, a bit over the seat: chairs stand half under the table's box collider.
        public override Vector3 FocusPoint => transform.position + Vector3.up * 0.2f;

        public override InteractVerb VerbFor(Holdable held) => IsFree ? InteractVerb.Sit : InteractVerb.None;

        public bool TryTake(object sitter)
        {
            if (_occupant != null && _occupant != sitter)
                return false;
            _occupant = sitter;
            return true;
        }

        public void Leave(object sitter)
        {
            if (_occupant == sitter)
                _occupant = null;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, 0.05f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.3f);
            if (StandPoint != null)
                Gizmos.DrawLine(transform.position, StandPoint.position);
        }
    }
}
