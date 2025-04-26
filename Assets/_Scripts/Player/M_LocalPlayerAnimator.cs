using UnityEngine;
using UnityEngine.InputSystem;

public class M_LocalPlayerAnimator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InputActionProperty moveInput;
    [SerializeField] private Animator avatarAnimator;
    [SerializeField] private Transform hmdTransform;  // Your VR camera under XR Rig
    [SerializeField] private Transform avatarRoot;    // The root of your avatar mesh

    [Header("Animation")]
    [SerializeField] private string moveSpeedParam = "MoveSpeed";
    [SerializeField] private string crouchParam = "IsCrouching";
    [SerializeField] private float smoothTime = 0.1f;

    [Header("Crouch Settings")]
    [SerializeField] private float crouchThreshold = 1.3f;  // Y height below which player is considered crouching
    [SerializeField] private float standThreshold = 1.5f;   // Y height above which player is standing again
    private bool isCrouching = false;

    [Header("Smooth Turn Settings")]
    [SerializeField] private float yawThreshold = 45f;   // Degrees before initiating turn
    [SerializeField] private float smoothTurnSpeed = 180f;  // Degrees per second

    [Header("Testing")]
    [SerializeField] private bool enableKeyboardTesting = true;

    // Internal state
    private float currentVelocity;
    private float smoothedSpeed;
    private bool isTurning = false;
    private Quaternion targetRotation;

    void OnEnable()
    {
        if (moveInput.action != null)
            moveInput.action.Enable();

        if (hmdTransform != null && avatarRoot != null)
        {
            float hmdYaw = hmdTransform.eulerAngles.y;
            avatarRoot.rotation = Quaternion.Euler(0, hmdYaw, 0);
            Debug.Log($"[Init] Aligned avatarRoot to HMD yaw = {hmdYaw:0.0}°");
        }
    }

    void OnDisable()
    {
        if (moveInput.action != null)
            moveInput.action.Disable();
    }

    void Update()
    {
        // —— Movement Animation ——
        Vector2 move = Vector2.zero;
        if (moveInput.action != null && moveInput.action.enabled)
            move = moveInput.action.ReadValue<Vector2>();

        if (enableKeyboardTesting)
        {
            float h = (Keyboard.current.aKey.isPressed ? -1f : 0f) + (Keyboard.current.dKey.isPressed ? 1f : 0f);
            float v = (Keyboard.current.sKey.isPressed ? -1f : 0f) + (Keyboard.current.wKey.isPressed ? 1f : 0f);
            Vector2 kb = new Vector2(h, v);
            if (kb.magnitude > move.magnitude) move = kb;
        }

        float rawSpeed = Mathf.Clamp01(move.magnitude);
        smoothedSpeed = Mathf.SmoothDamp(smoothedSpeed, rawSpeed, ref currentVelocity, smoothTime);

        if (avatarAnimator != null)
        {
            avatarAnimator.SetFloat(moveSpeedParam, smoothedSpeed);
        }
        else
        {
            Debug.LogWarning("[M_LocalPlayerAnimator] Animator reference not set!");
        }

        // —— Physical Crouch Detection via Headset Height ——
        if (hmdTransform != null && avatarAnimator != null)
        {
            float headHeight = hmdTransform.localPosition.y;

            if (!isCrouching && headHeight < crouchThreshold)
            {
                isCrouching = true;
                avatarAnimator.SetFloat(crouchParam, 1);
                Debug.Log("[Crouch] Crouch detected: HMD height = " + headHeight.ToString("0.00"));
            }
            else if (isCrouching && headHeight > standThreshold)
            {
                isCrouching = false;
                avatarAnimator.SetFloat(crouchParam, 0);
                Debug.Log("[Crouch] Standing up: HMD height = " + headHeight.ToString("0.00"));
            }
        }

        // —— Smooth Snap‑Turn Logic ——
        if (hmdTransform == null || avatarRoot == null)
            return;

        float bodyYaw = avatarRoot.eulerAngles.y;
        float hmdYaw = hmdTransform.eulerAngles.y;
        float yawDelta = Mathf.DeltaAngle(bodyYaw, hmdYaw);

        if (!isTurning)
        {
            if (Mathf.Abs(yawDelta) > yawThreshold)
            {
                targetRotation = Quaternion.Euler(0, hmdYaw, 0);
                isTurning = true;
                Debug.Log($"[SmoothTurn] Starting turn: body={bodyYaw:0.0}°, hmd={hmdYaw:0.0}°, Δ={yawDelta:0.0}° → target={hmdYaw:0.0}°");
            }
        }
        else
        {
            avatarRoot.rotation = Quaternion.RotateTowards(
                avatarRoot.rotation,
                targetRotation,
                smoothTurnSpeed * Time.deltaTime
            );

            if (Quaternion.Angle(avatarRoot.rotation, targetRotation) < 0.1f)
            {
                avatarRoot.rotation = targetRotation;
                isTurning = false;
                Debug.Log($"[SmoothTurn] Completed turn. New body yaw = {hmdYaw:0.0}°");
            }
        }
    }
}