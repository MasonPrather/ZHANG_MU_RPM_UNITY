using UnityEngine;

/// <summary>
/// Stable continuous locomotion for Quest/OVR:
/// - Left stick moves in head-yaw space
/// - Right stick smooth or snap turn
/// - Smooth capsule height from HMD
/// - Grounded spherecast + gentle snap to avoid pogo/jitter
/// Requires a CharacterController on the same GameObject.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class M_OVRContinuousLocomotion : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Meters per second walking speed.")]
    public float moveSpeed = 2.0f;
    [Tooltip("Hold L-stick click (or change the binding) if you add a run mode later.")]
    public float runSpeed = 3.5f;

    [Header("Turning (Right Stick)")]
    public bool smoothTurn = true;
    [Tooltip("Deg/sec for smooth turn.")]
    public float smoothTurnSpeed = 60f;
    [Tooltip("Degrees per snap turn.")]
    public float snapTurnAngle = 45f;
    [Tooltip("Cooldown between snap turns.")]
    public float snapCooldown = 0.35f;

    [Header("Capsule Height From HMD")]
    [Tooltip("Min and max capsule height clamped from the head local Y.")]
    public Vector2 heightClamp = new Vector2(1.2f, 2.2f);
    [Tooltip("How quickly the capsule height follows HMD height.")]
    public float heightSmooth = 12f;

    [Header("Grounding / Gravity")]
    [Tooltip("Meters/second^2; negative.")]
    public float gravity = -9.81f;
    [Tooltip("Extra downward snap when close to ground; 0 disables.")]
    public float groundSnap = 0.22f;
    [Tooltip("Layers considered ground.")]
    public LayerMask groundMask = ~0;

    [Header("Refs")]
    [Tooltip("HMD/head transform for yaw & height. Auto-fills from OVRCameraRig if empty.")]
    public Transform head;

    CharacterController _cc;
    float _snapTimer;
    float _vertVel;
    float _targetHeight;
    float _currentHeight;
    bool _isGrounded;
    Vector3 _planarMove; // computed in Update, applied in FixedUpdate

    void Awake()
    {
        _cc = GetComponent<CharacterController>();

        if (!head)
        {
            var rig = FindObjectOfType<OVRCameraRig>();
            if (rig) head = rig.centerEyeAnchor;
        }

        // Reasonable CC defaults (can still be tuned in Inspector)
        _cc.minMoveDistance = 0f;
        if (_cc.skinWidth < 0.02f) _cc.skinWidth = 0.03f;
        if (_cc.stepOffset < 0.2f) _cc.stepOffset = 0.3f;
        if (_cc.slopeLimit < 55f) _cc.slopeLimit = 65f;

        _currentHeight = Mathf.Clamp(_cc.height > 0 ? _cc.height : 1.7f, heightClamp.x, heightClamp.y);
        _targetHeight = _currentHeight;
        ApplyCapsule(_currentHeight);
    }

    void Update()
    {
        // ---- Capsule height follows HMD with smoothing ----
        if (head)
        {
            float h = Mathf.Clamp(head.localPosition.y, heightClamp.x, heightClamp.y);
            // critically damped-ish smoothing
            _targetHeight = Mathf.Lerp(_targetHeight, h, 1f - Mathf.Exp(-heightSmooth * Time.deltaTime));
        }
        _currentHeight = Mathf.Clamp(_targetHeight, heightClamp.x, heightClamp.y);
        ApplyCapsule(_currentHeight);

        // ---- Planar movement in head-yaw space (left stick) ----
        Vector2 ls = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
        Vector3 fwd = head ? head.forward : transform.forward;
        Vector3 right = head ? head.right : transform.right;
        fwd.y = 0f; right.y = 0f;
        fwd.Normalize(); right.Normalize();

        Vector3 moveDir = (fwd * ls.y + right * ls.x);
        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

        float speed = moveSpeed; // (hook a run modifier here if desired)
        _planarMove = moveDir * speed;

        // ---- Turning (right stick) ----
        Vector2 rs = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
        if (smoothTurn)
        {
            if (Mathf.Abs(rs.x) > 0.01f)
                transform.Rotate(0f, rs.x * smoothTurnSpeed * Time.deltaTime, 0f);
        }
        else
        {
            _snapTimer -= Time.deltaTime;
            if (Mathf.Abs(rs.x) > 0.7f && _snapTimer <= 0f)
            {
                transform.Rotate(0f, Mathf.Sign(rs.x) * snapTurnAngle, 0f);
                _snapTimer = snapCooldown;
            }
        }
    }

    void FixedUpdate()
    {
        // ---- Ground check via spherecast ----
        Vector3 feet = transform.position + Vector3.up * (_cc.radius + 0.05f);
        float castDist = _cc.stepOffset + 0.25f; // short cast just under feet
        bool wasGrounded = _isGrounded;
        _isGrounded = Physics.SphereCast(
            feet,
            _cc.radius * 0.95f,
            Vector3.down,
            out RaycastHit hit,
            castDist,
            groundMask,
            QueryTriggerInteraction.Ignore
        );

        if (_isGrounded)
        {
            // Gentle snap when very close to ground; prevents pogo on slopes/steps
            if (groundSnap > 0f && hit.distance > 0f && hit.distance < groundSnap)
                _vertVel = Mathf.Min(_vertVel, 0f);
            else if (!wasGrounded)
                _vertVel = 0f; // landed
        }

        // ---- Gravity ----
        _vertVel += gravity * Time.fixedDeltaTime;

        // ---- Apply motion ----
        Vector3 velocity = _planarMove + Vector3.up * _vertVel;
        _cc.Move(velocity * Time.fixedDeltaTime);
    }

    void ApplyCapsule(float height)
    {
        _cc.height = height;
        var c = _cc.center;
        c.y = height * 0.5f;
        _cc.center = c;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (heightClamp.x < 0.5f) heightClamp.x = 0.5f;
        if (heightClamp.y < heightClamp.x) heightClamp.y = heightClamp.x + 0.1f;
        if (groundSnap < 0f) groundSnap = 0f;
    }
#endif
}