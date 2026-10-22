using Stasik.Interaction;
using UnityEngine;

namespace Stasik.Characters
{
    public enum CapStyle
    {
        Off,      // not on a head: in a hand, on a hook, on a table, on the floor
        Forward,  // peak to the front
        Backward, // peak to the back
    }

    /// <summary>
    /// The cap: an item (Holdable) that can also be worn, peak forward or peak backward. Each way of wearing has its own
    /// mesh fitted to the player's head (a head isn't the same front and back) and its own place on the Head bone
    /// (built by CapBuilder into Prefabs/Items/Cap.prefab).
    ///
    /// The character's hand (CharacterHand) puts it on, takes it off and carries it; turning it round on the head is a
    /// move of the cap's own: the hand holds the peak while the cap spins round the head past the right side.
    /// C cycles: in hand -> peak forward -> peak backward -> in hand.
    ///
    /// While it is on, fur under it is hidden: Stasik/FurShell reads the cap globals set here, otherwise the strands
    /// poke through the crown. One worn cap at a time (the globals are shared).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Holdable), typeof(MeshFilter), typeof(MeshRenderer))]
    public class HeadCap : MonoBehaviour, CharacterHand.IGoalSource
    {
        /// <summary>One way of wearing: the mesh fitted for it and where it sits on the head bone.</summary>
        [System.Serializable]
        public struct Placement
        {
            [SerializeField] private Mesh _mesh;
            [SerializeField] private Vector3 _localPosition;
            [SerializeField] private Quaternion _localRotation;
            [SerializeField] private Vector3 _radii; // crown radii in cap space (m): the fur mask

            public Placement(Mesh mesh, Vector3 localPosition, Quaternion localRotation, Vector3 radii)
            {
                _mesh = mesh;
                _localPosition = localPosition;
                _localRotation = localRotation;
                _radii = radii;
            }

            public Mesh Mesh => _mesh;
            public Vector3 LocalPosition => _localPosition;
            public Quaternion LocalRotation => _localRotation;
            public Vector3 Radii => _radii;
            public bool IsValid => _mesh != null;
        }

        private static readonly int WorldToLocalId = Shader.PropertyToID("_CapWorldToLocal");
        private static readonly int RadiiId = Shader.PropertyToID("_CapRadii");

        // Turning round: reach the peak / turn / let go, as fractions of the turn time.
        private const float TurnReach = 0.25f, TurnDone = 0.75f;
        private const float HandLength = 0.07f; // m from the wrist to the fingers holding the peak

        [SerializeField] private Placement _forward;
        [SerializeField] private Placement _backward;
        [SerializeField] private CapStyle _style;
        [SerializeField] private float _bandMargin = 0.02f; // fur rooted this far below the band is hidden too
        [SerializeField] private float _lift = 0.14f;       // m above the head the cap passes when put on / taken off
        [SerializeField] private float _turnTime = 1.2f;
        [SerializeField] private float _turnHop = 0.04f;    // m the cap lifts while it turns round

        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Holdable _holdable;
        private bool _masked;
        private bool _turning;
        private CapStyle _turnFrom;
        private CapStyle _turnTo;
        private float _turnStart;

        public CapStyle Style => _style;

        public bool Worn => _style != CapStyle.Off;

        public bool IsTurning => _turning;

        public Holdable Holdable
        {
            get
            {
                Cache();
                return _holdable;
            }
        }

        /// <summary>Fills in both ways of wearing (CapBuilder). Off leaves the cap where it is.</summary>
        public void Setup(Placement forward, Placement backward, CapStyle style)
        {
            _forward = forward;
            _backward = backward;
            _style = style;
            Settle();
        }

        /// <summary>On <paramref name="head"/> at once (edit mode, the test scene, the end of putting it on).</summary>
        public void Wear(Transform head, CapStyle style)
        {
            if (head == null || style == CapStyle.Off || !Placed(style).IsValid)
                return;
            _turning = false;
            _style = style;
            Holdable.SnapTo(HeadRest(head, style));
            Show(style);
        }

        /// <summary>The rest on a head: the placement on the bone; the hand comes in and leaves beside the head.</summary>
        public ItemRest HeadRest(Transform head, CapStyle style)
        {
            var placed = Placed(style);
            return ItemRest.At(head, placed.LocalPosition, placed.LocalRotation, Vector3.up * _lift, true);
        }

        /// <summary>From the hand onto the head.</summary>
        public bool TryPutOn(CharacterHand hand, CapStyle style = CapStyle.Forward)
        {
            if (hand == null || hand.Held != Holdable || style == CapStyle.Off || !Placed(style).IsValid)
                return false;
            var head = hand.Head;
            return hand.TryMove(Holdable, HeadRest(head, style), () => Wear(head, style));
        }

        /// <summary>From the head into the hand.</summary>
        public bool TryTakeOff(CharacterHand hand) =>
            hand != null && Worn && !_turning && hand.Held == null && transform.parent == hand.Head && hand.TryPickUp(Holdable);

        /// <summary>Turns it round on the head (peak forward to backward or back), the hand holding the peak.</summary>
        public bool TryTurnRound(CharacterHand hand)
        {
            if (hand == null || !Worn || _turning || hand.Held != null || transform.parent != hand.Head)
                return false;
            var to = _style == CapStyle.Forward ? CapStyle.Backward : CapStyle.Forward;
            if (!Placed(to).IsValid || !hand.TryRunCustom(this))
                return false;
            _turning = true;
            _turnFrom = _style;
            _turnTo = to;
            _turnStart = Time.time;
            return true;
        }

        /// <summary>The cap button: in hand -> peak forward -> peak backward -> in hand.</summary>
        public bool TryNext(CharacterHand hand)
        {
            if (hand == null || hand.IsBusy)
                return false;
            if (hand.Held == Holdable)
                return TryPutOn(hand);
            return _style switch
            {
                CapStyle.Forward => TryTurnRound(hand),
                CapStyle.Backward => TryTakeOff(hand),
                _ => false,
            };
        }

        /// <summary>The hand while the cap turns round: reach the peak, follow it round the head, let go.</summary>
        public bool GetHandGoal(CharacterHand hand, out CharacterHand.Goal goal)
        {
            goal = default;
            if (!_turning)
                return false;
            var u = TurnProgress;
            if (u >= 1f)
                return false;

            Pose wrist;
            float weight;
            if (u < TurnReach)
                (wrist, weight) = (Holdable.GripAt(RestPose(_turnFrom)), CharacterHand.Ease(u / TurnReach));
            else if (u < TurnDone)
                (wrist, weight) = (Holdable.GripAt(TurnPose(CharacterHand.Ease(CharacterHand.Span(u, TurnReach, TurnDone)))), 1f);
            else
                (wrist, weight) = (Holdable.GripAt(RestPose(_turnTo)), 1f - CharacterHand.Ease(CharacterHand.Span(u, TurnDone, 1f)));

            goal = new CharacterHand.Goal
            {
                Position = wrist.position,
                Rotation = wrist.rotation,
                ElbowHint = hand.ElbowHintFor(wrist.position),
                Weight = weight,
            };
            return true;
        }

        private float TurnProgress => Mathf.Clamp01((Time.time - _turnStart) / Mathf.Max(_turnTime, 1e-3f));

        private void Awake()
        {
            Cache();
            if (_holdable != null)
                _holdable.Lifted += OnLifted;
        }

        private void OnDestroy()
        {
            if (_holdable != null)
                _holdable.Lifted -= OnLifted;
        }

        private void Cache()
        {
            if (_filter != null)
                return;
            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
            _holdable = GetComponent<Holdable>();
        }

        private void OnEnable() => Settle();

        private void OnDisable()
        {
            if (_turning)
                FinishTurn();
            SetMask(CapStyle.Off);
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            // Meshes can't be switched inside OnValidate itself.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled)
                    Settle();
            };
#endif
        }

        // After the animation: the cap turning round follows the head; the mask follows the cap's place.
        private void LateUpdate()
        {
            if (_turning)
            {
                var u = TurnProgress;
                var s = u < TurnReach ? 0f : u < TurnDone ? CharacterHand.Ease(CharacterHand.Span(u, TurnReach, TurnDone)) : 1f;
                if (u >= 1f)
                {
                    FinishTurn();
                }
                else
                {
                    var pose = TurnPose(s);
                    Show(s < 0.5f ? _turnFrom : _turnTo);
                    transform.SetPositionAndRotation(pose.position, pose.rotation);
                }
            }

            SetMask(_turning ? _turnTo : _style);
        }

        private void OnLifted()
        {
            _turning = false;
            _style = CapStyle.Off;
            Show(CapStyle.Forward);
            SetMask(CapStyle.Off);
        }

        private void FinishTurn()
        {
            _turning = false;
            _style = _turnTo;
            Holdable.SnapTo(HeadRest(transform.parent, _style));
            Show(_style);
        }

        /// <summary>Mesh and anchors for the current state; on a head also its place. Off never moves the cap.</summary>
        private void Settle()
        {
            Cache();
            if (Worn && Placed(_style).IsValid && transform.parent != null)
            {
                transform.SetLocalPositionAndRotation(Placed(_style).LocalPosition, Placed(_style).LocalRotation);
                Show(_style);
            }
            else if (Placed(CapStyle.Forward).IsValid)
            {
                Show(CapStyle.Forward);
            }
        }

        /// <summary>The mesh for a way of wearing (Off = the forward one) and the hand / hook / surface anchors for it.</summary>
        private void Show(CapStyle style)
        {
            var mesh = Placed(style).Mesh;
            if (mesh == null)
                return;
            if (_filter.sharedMesh != mesh)
                _filter.sharedMesh = mesh;
            _renderer.enabled = true;

            var b = mesh.bounds;
            // The hand holds the peak's edge a little right of the middle (the right hand's side): fingers towards the
            // crown, palm down on the peak. The cap's +z is the peak in each placement.
            var peak = new Vector3(0.04f, -0.015f, b.max.z - 0.03f);
            var grip = new LocalPose(peak - Vector3.back * HandLength, Quaternion.LookRotation(Vector3.up, Vector3.back));
            // Hangs on a hook by the back strap: back up, peak down, the crown facing out of the hook.
            var hang = new LocalPose(new Vector3(0f, 0.011f, b.min.z + 0.005f), Quaternion.LookRotation(Vector3.up, Vector3.back));
            var stand = new LocalPose(new Vector3(0f, b.min.y, 0f), Quaternion.identity);
            if (_holdable != null) // a cap left on the player by an older build has none
                _holdable.SetAnchors(grip, hang, stand, true);
        }

        private Placement Placed(CapStyle style) => style == CapStyle.Backward ? _backward : _forward;

        /// <summary>Where the cap sits on the head for this way of wearing, right now (the head moves).</summary>
        private Pose RestPose(CapStyle style)
        {
            var placed = Placed(style);
            var head = transform.parent;
            return head == null
                ? new Pose(placed.LocalPosition, placed.LocalRotation)
                : new Pose(head.TransformPoint(placed.LocalPosition), head.rotation * placed.LocalRotation);
        }

        /// <summary>The cap turning round on the head: s 0..1 = 0..180 degrees round the band's up, a little hop.</summary>
        private Pose TurnPose(float s)
        {
            var a = RestPose(_turnFrom);
            var b = RestPose(_turnTo);
            // The peak passes the character's right, where the right hand holds it: from the front that's a positive
            // turn round the band's up, from the back a negative one.
            var spin = Quaternion.AngleAxis((_turnFrom == CapStyle.Backward ? -180f : 180f) * s, a.rotation * Vector3.up);
            return new Pose(Vector3.Lerp(a.position, b.position, s) + Vector3.up * (_turnHop * Mathf.Sin(Mathf.PI * s)),
                spin * a.rotation);
        }

        /// <summary>
        /// Hides the fur under <paramref name="style"/>'s place, or shows it again for Off. Clears only a mask this cap
        /// set, so a cap that is off doesn't fight a worn one over the shared globals.
        /// </summary>
        private void SetMask(CapStyle style)
        {
            var placed = Placed(style);
            if (style == CapStyle.Off || !placed.IsValid || transform.parent == null)
            {
                if (_masked)
                    Shader.SetGlobalVector(RadiiId, Vector4.zero);
                _masked = false;
                return;
            }

            // Cap space in metres: origin on the band, y up (the shader compares it with the radii).
            var pose = RestPose(style);
            var worldToCap = Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one).inverse;
            Shader.SetGlobalMatrix(WorldToLocalId, Matrix4x4.Translate(Vector3.up * _bandMargin) * worldToCap);
            Shader.SetGlobalVector(RadiiId, new Vector4(placed.Radii.x, placed.Radii.y, placed.Radii.z, 1f));
            _masked = true;
        }
    }
}
