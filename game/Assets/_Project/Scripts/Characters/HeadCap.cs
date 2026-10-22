using UnityEngine;

namespace Stasik.Characters
{
    public enum CapStyle
    {
        Off,
        Forward,  // peak to the front
        Backward, // peak to the back
    }

    /// <summary>
    /// The cap on the Head bone (built by CapBuilder): off, peak forward or peak backward. Each way of wearing has its
    /// own mesh fitted to the head (a head isn't the same front and back) and its own place on the bone.
    ///
    /// In Play mode the character changes it with his right hand (CapHandIK drives the arm): to put it on he reaches
    /// behind his back, brings the cap up over his head and puts it on; to take it off he lifts it and tucks it behind
    /// his back; to turn it round he takes the peak and turns the cap round his head. In edit mode it just switches.
    ///
    /// While it is on, fur under it is hidden: Stasik/FurShell reads the cap globals set here, otherwise the strands
    /// poke through the crown. One worn cap at a time (the globals are shared).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class HeadCap : MonoBehaviour
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

        /// <summary>Where the right hand should be this frame (world space), for CapHandIK.</summary>
        public struct HandGoal
        {
            public Vector3 Position;    // wrist
            public Quaternion Rotation; // hand bone: fingers along its +Y, palm towards its -Z
            public Vector3 ElbowHint;
            public float Weight;
        }

        private enum Change
        {
            None,
            PutOn,
            TakeOff,
            TurnRound,
        }

        private static readonly int WorldToLocalId = Shader.PropertyToID("_CapWorldToLocal");
        private static readonly int RadiiId = Shader.PropertyToID("_CapRadii");

        // Fractions of each change: reach / carry / place for putting on, reach / lift / carry for taking off, reach /
        // turn for turning round; the rest of each is the hand going back to the animation.
        private const float PutOnReach = 0.25f, PutOnCarry = 0.68f, PutOnPlace = 0.8f;
        private const float TakeOffReach = 0.22f, TakeOffLift = 0.36f, TakeOffCarry = 0.74f;
        private const float TurnReach = 0.25f, TurnDone = 0.75f;
        private const float HandLength = 0.07f; // m from the wrist to the fingers holding the peak

        [SerializeField] private Placement _forward;
        [SerializeField] private Placement _backward;
        [SerializeField] private CapStyle _style = CapStyle.Forward;
        [SerializeField] private float _bandMargin = 0.02f; // fur rooted this far below the band is hidden too

        [Header("Hands (Play mode)")]
        [SerializeField] private float _putOnTime = 1.5f;
        [SerializeField] private float _takeOffTime = 1.4f;
        [SerializeField] private float _turnTime = 1.2f;
        [SerializeField] private float _lift = 0.14f;       // m above the head the cap passes when put on / taken off
        [SerializeField] private float _turnHop = 0.04f;    // m the cap lifts while it turns round

        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Animator _animator;
        private Transform _hand;
        private Transform _hips;
        private Change _change;
        private CapStyle _from;
        private float _elapsed;
        private int _ikFrame = -10;
        private bool _masked;

        public CapStyle Style => _style;

        public bool Worn => _style != CapStyle.Off;

        public bool IsChanging => _change != Change.None;

        /// <summary>Fills in both ways of wearing (CapBuilder); applies <paramref name="style"/> at once.</summary>
        public void Setup(Placement forward, Placement backward, CapStyle style)
        {
            _forward = forward;
            _backward = backward;
            _style = style;
            Settle();
        }

        /// <summary>
        /// Changes the cap: by hand in Play mode (when the character has a humanoid animator), at once otherwise.
        /// A change while one is running finishes that one at once first.
        /// </summary>
        public void SetStyle(CapStyle style)
        {
            if (style != CapStyle.Off && !Placed(style).IsValid)
                return;
            if (IsChanging)
                Settle();
            if (style == _style)
                return;

            _from = _style;
            _style = style;
            if (!Application.isPlaying || !isActiveAndEnabled || !FindHand())
            {
                Settle();
                return;
            }

            _change = _from == CapStyle.Off ? Change.PutOn : style == CapStyle.Off ? Change.TakeOff : Change.TurnRound;
            _elapsed = 0f;
            if (_change == Change.PutOn)
                _renderer.enabled = false; // still behind his back
        }

        /// <summary>The cap button: off -> peak forward -> peak backward -> off.</summary>
        public void Next() => SetStyle(_style switch
        {
            CapStyle.Off => CapStyle.Forward,
            CapStyle.Forward => CapStyle.Backward,
            _ => CapStyle.Off,
        });

        public void PutOn() => SetStyle(CapStyle.Forward);

        public void TakeOff() => SetStyle(CapStyle.Off);

        public void Toggle() => SetStyle(Worn ? CapStyle.Off : CapStyle.Forward);

        /// <summary>
        /// The hand goal for this frame; false when the hand is free. Called by CapHandIK from OnAnimatorIK (bones are
        /// still in last frame's pose there - one frame behind is fine for a target). Its calls also tell the cap that
        /// the IK runs: without them (no IK pass on the layer) the cap follows the targets instead of the hand.
        /// </summary>
        public bool GetHandGoal(out HandGoal goal)
        {
            _ikFrame = Time.frameCount;
            return ComputeHandGoal(out goal);
        }

        /// <summary>The same goal without counting as an IK pass (CapHandIK turns the hand after the IK with it).</summary>
        public bool PeekHandGoal(out HandGoal goal) => ComputeHandGoal(out goal);

        private bool ComputeHandGoal(out HandGoal goal)
        {
            goal = default;
            if (!IsChanging || _hand == null)
                return false;

            var u = Progress;
            Vector3 grip;
            float weight;
            switch (_change)
            {
                case Change.PutOn:
                    if (u < PutOnReach)
                        (grip, weight) = (BackSpot(), Ease(u / PutOnReach));
                    else if (u < PutOnCarry)
                        (grip, weight) = (Bezier(BackSpot(), SideSpot(), Grip(_style) + Up * _lift, Ease(Span(u, PutOnReach, PutOnCarry))), 1f);
                    else if (u < PutOnPlace)
                        (grip, weight) = (Vector3.Lerp(Grip(_style) + Up * _lift, Grip(_style), Ease(Span(u, PutOnCarry, PutOnPlace))), 1f);
                    else
                        (grip, weight) = (Grip(_style), 1f - Ease(Span(u, PutOnPlace, 1f)));
                    break;
                case Change.TakeOff:
                    if (u < TakeOffReach)
                        (grip, weight) = (Grip(_from), Ease(u / TakeOffReach));
                    else if (u < TakeOffLift)
                        (grip, weight) = (Vector3.Lerp(Grip(_from), Grip(_from) + Up * _lift, Ease(Span(u, TakeOffReach, TakeOffLift))), 1f);
                    else if (u < TakeOffCarry)
                        (grip, weight) = (Bezier(Grip(_from) + Up * _lift, SideSpot(), BackSpot(), Ease(Span(u, TakeOffLift, TakeOffCarry))), 1f);
                    else
                        (grip, weight) = (BackSpot(), 1f - Ease(Span(u, TakeOffCarry, 1f)));
                    break;
                default:
                    if (u < TurnReach)
                        (grip, weight) = (Grip(_from), Ease(u / TurnReach));
                    else if (u < TurnDone)
                        (grip, weight) = (TurnGrip(Ease(Span(u, TurnReach, TurnDone))), 1f);
                    else
                        (grip, weight) = (Grip(_style), 1f - Ease(Span(u, TurnDone, 1f)));
                    break;
            }

            // Fingers on the peak pointing at the crown, palm down on the cap (back of the hand up). Behind the back the
            // cap is hidden; the hand keeps the turn it carries the cap with.
            var capRotation = _renderer.enabled ? transform.rotation : CarryRotation();
            var fingers = capRotation * Vector3.back;
            var backOfHand = Vector3.ProjectOnPlane(capRotation * Vector3.up, fingers).normalized;
            var body = BodyRotation;
            goal = new HandGoal
            {
                Position = grip - fingers * HandLength,
                Rotation = Quaternion.LookRotation(backOfHand, fingers),
                // Elbow up and out to the side, so the forearm doesn't cover his face.
                ElbowHint = _hips.position + body * new Vector3(0.5f, 0.55f, -0.15f),
                Weight = weight,
            };
            return true;
        }

        private void Awake() => Cache();

        private void Cache()
        {
            if (_filter != null)
                return;
            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
        }

        private void OnEnable() => Settle();

        private void OnDisable()
        {
            if (IsChanging)
                Settle(); // don't leave it frozen in the hand
            SetMask(CapStyle.Off);
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            // Meshes and renderers can't be switched inside OnValidate itself.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled)
                    Settle();
            };
#endif
        }

        private void Update()
        {
            if (IsChanging)
                _elapsed += Time.deltaTime;
        }

        // After the animators and the IK: the cap follows the head, or the hand holding it, in the rendered pose.
        private void LateUpdate()
        {
            if (IsChanging)
            {
                if (Progress >= 1f)
                    Settle();
                else
                    PoseInHand(Progress);
            }

            SetMask(Masked);
        }

        private float Duration => _change switch
        {
            Change.PutOn => _putOnTime,
            Change.TakeOff => _takeOffTime,
            _ => _turnTime,
        };

        private float Progress => Mathf.Clamp01(_elapsed / Mathf.Max(Duration, 1e-3f));

        /// <summary>
        /// Whose place hides the fur now: the cap's while it sits on the head or turns round on it; nobody's while it is
        /// in the hand or behind his back.
        /// </summary>
        private CapStyle Masked => _change switch
        {
            Change.None => _style,
            Change.PutOn => Progress >= PutOnPlace ? _style : CapStyle.Off,
            Change.TakeOff => Progress < TakeOffReach ? _from : CapStyle.Off,
            _ => _style,
        };

        private void PoseInHand(float u)
        {
            switch (_change)
            {
                case Change.PutOn:
                    if (u < PutOnReach)
                    {
                        _renderer.enabled = false;
                        return;
                    }

                    Show(Placed(_style));
                    var rest = RestPose(Placed(_style));
                    var held = HeldPose(_style, Turn(CarryRotation(), rest.rotation, Ease(Span(u, PutOnReach, PutOnCarry)), -1f));
                    Blend(held, rest, u < PutOnCarry ? 0f : Ease(Span(u, PutOnCarry, PutOnPlace)));
                    break;
                case Change.TakeOff:
                    if (u >= TakeOffCarry)
                    {
                        _renderer.enabled = false;
                        return;
                    }

                    var from = RestPose(Placed(_from));
                    var carried = Turn(from.rotation, CarryRotation(), Ease(Span(u, TakeOffReach, TakeOffCarry)), 1f);
                    Blend(from, HeldPose(_from, carried), u < TakeOffReach ? 0f : Ease(Span(u, TakeOffReach, TakeOffLift)));
                    break;
                default:
                    var s = u < TurnReach ? 0f : u < TurnDone ? Ease(Span(u, TurnReach, TurnDone)) : 1f;
                    var (position, rotation) = TurnPose(s);
                    Show(Placed(s < 0.5f ? _from : _style));
                    transform.SetPositionAndRotation(position, rotation);
                    break;
            }
        }

        /// <summary>The cap turning round on the head: s 0..1 = 0..180 degrees round the band's up, a little hop.</summary>
        private (Vector3 position, Quaternion rotation) TurnPose(float s)
        {
            var a = RestPose(Placed(_from));
            var b = RestPose(Placed(_style));
            // The peak passes the character's right, where the right hand holds it: from the front that's a positive
            // turn round the band's up, from the back a negative one.
            var spin = Quaternion.AngleAxis((_from == CapStyle.Backward ? -180f : 180f) * s, a.rotation * Vector3.up);
            return (Vector3.Lerp(a.position, b.position, s) + Up * (_turnHop * Mathf.Sin(Mathf.PI * s)), spin * a.rotation);
        }

        private Vector3 TurnGrip(float s)
        {
            var (position, rotation) = TurnPose(s);
            return position + rotation * GripLocal(s < 0.5f ? _from : _style);
        }

        /// <summary>Cap held by the peak: the grip point at the real hand (after IK), or at the target without IK.</summary>
        private (Vector3 position, Quaternion rotation) HeldPose(CapStyle style, Quaternion rotation)
        {
            Vector3 grip;
            if (Time.frameCount - _ikFrame <= 1)
            {
                var fingers = rotation * Vector3.back;
                grip = _hand.position + fingers * HandLength;
            }
            else
            {
                ComputeHandGoal(out var goal);
                grip = goal.Position + rotation * Vector3.back * HandLength;
            }

            return (grip - rotation * GripLocal(style), rotation);
        }

        /// <summary>
        /// How the cap is carried in the hand: peak forward, level, the crown hanging behind the hand (so a cap worn
        /// backward doesn't swing its crown through his face, nor into his hip behind the back).
        /// </summary>
        private Quaternion CarryRotation() =>
            Quaternion.LookRotation(Vector3.ProjectOnPlane(BodyRotation * Vector3.forward, Vector3.up), Vector3.up);

        /// <summary>
        /// From one cap rotation to another, the turn round the vertical done explicitly and the small rest by slerp.
        /// Half a turn goes the way of <paramref name="halfTurnSign"/>: the crown swings round the outside of the hand
        /// (the character's right), not through his head.
        /// </summary>
        private static Quaternion Turn(Quaternion from, Quaternion to, float t, float halfTurnSign)
        {
            var yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(from * Vector3.forward, Vector3.up),
                Vector3.ProjectOnPlane(to * Vector3.forward, Vector3.up), Vector3.up);
            if (Mathf.Abs(yaw) > 170f)
                yaw = Mathf.Abs(yaw) * halfTurnSign;
            return Quaternion.Slerp(Quaternion.AngleAxis(yaw * t, Vector3.up) * from, to, t);
        }

        private void Blend((Vector3 position, Quaternion rotation) a, (Vector3 position, Quaternion rotation) b, float t) =>
            transform.SetPositionAndRotation(Vector3.Lerp(a.position, b.position, t), Quaternion.Slerp(a.rotation, b.rotation, t));

        /// <summary>Shows the settled state of _style: mesh and place on the head (LateUpdate keeps the mask).</summary>
        private void Settle()
        {
            Cache();
            _change = Change.None;
            var placed = Placed(_style);
            if (_style == CapStyle.Off || !placed.IsValid)
            {
                _renderer.enabled = false;
                SetMask(CapStyle.Off);
                return;
            }

            Show(placed);
            transform.localPosition = placed.LocalPosition;
            transform.localRotation = placed.LocalRotation;
        }

        private void Show(Placement placed)
        {
            if (_filter.sharedMesh != placed.Mesh)
                _filter.sharedMesh = placed.Mesh;
            _renderer.enabled = true;
        }

        private bool FindHand()
        {
            if (_hand != null)
                return true;
            _animator = GetComponentInParent<Animator>();
            if (_animator == null || !_animator.isHuman)
                return false;
            _hand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            if (_hand == null || _hips == null)
            {
                _hand = null;
                return false;
            }

            if (_animator.GetComponent<CapHandIK>() == null)
                _animator.gameObject.AddComponent<CapHandIK>();
            return true;
        }

        private Placement Placed(CapStyle style) => style == CapStyle.Backward ? _backward : _forward;

        /// <summary>Where the cap sits for this placement right now (the head moves with the animation).</summary>
        private (Vector3 position, Quaternion rotation) RestPose(Placement placed)
        {
            var head = transform.parent;
            return head == null
                ? (placed.LocalPosition, placed.LocalRotation)
                : (head.TransformPoint(placed.LocalPosition), head.rotation * placed.LocalRotation);
        }

        /// <summary>Where the hand holds the peak's edge, in cap space (the peak points along +z of each placement).</summary>
        private Vector3 GripLocal(CapStyle style)
        {
            var mesh = Placed(style).Mesh;
            // A little to the right of the middle: the right hand takes the peak from its side.
            return mesh == null ? Vector3.forward * 0.2f : new Vector3(0.04f, -0.015f, mesh.bounds.max.z - 0.03f);
        }

        private Vector3 Grip(CapStyle style)
        {
            var (position, rotation) = RestPose(Placed(style));
            return position + rotation * GripLocal(style);
        }

        // Character frame: facing the way the player root faces (the model under it is turned to match), level.
        // From transforms, not Animator.bodyRotation: that one may only be read inside OnAnimatorIK.
        private Quaternion BodyRotation =>
            Quaternion.LookRotation(Vector3.ProjectOnPlane(_animator.transform.root.forward, Vector3.up), Vector3.up);
        private Vector3 Up => Vector3.up;

        /// <summary>Behind his back at the right hip, where the cap is kept while it's off.</summary>
        private Vector3 BackSpot() => _hips.position + BodyRotation * new Vector3(0.15f, 0.04f, -0.17f);

        /// <summary>Beside the head on the right: the curve the hand takes between the back and over the head.</summary>
        private Vector3 SideSpot() => transform.parent.position + BodyRotation * new Vector3(0.32f, 0.02f, -0.05f);

        private static Vector3 Bezier(Vector3 a, Vector3 control, Vector3 b, float t) =>
            Vector3.Lerp(Vector3.Lerp(a, control, t), Vector3.Lerp(control, b, t), t);

        private static float Span(float u, float from, float to) => Mathf.Clamp01((u - from) / (to - from));

        private static float Ease(float t) => t * t * (3f - 2f * t);

        /// <summary>
        /// Hides the fur under <paramref name="style"/>'s place, or shows it again for Off. Clears only a mask this cap
        /// set, so a cap that is off doesn't fight a worn one over the shared globals.
        /// </summary>
        private void SetMask(CapStyle style)
        {
            var placed = Placed(style);
            if (style == CapStyle.Off || !placed.IsValid)
            {
                if (_masked)
                    Shader.SetGlobalVector(RadiiId, Vector4.zero);
                _masked = false;
                return;
            }

            // Cap space in metres: origin on the band, y up (the shader compares it with the radii).
            var (position, rotation) = RestPose(placed);
            var worldToCap = Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
            Shader.SetGlobalMatrix(WorldToLocalId, Matrix4x4.Translate(Vector3.up * _bandMargin) * worldToCap);
            Shader.SetGlobalVector(RadiiId, new Vector4(placed.Radii.x, placed.Radii.y, placed.Radii.z, 1f));
            _masked = true;
        }
    }
}
