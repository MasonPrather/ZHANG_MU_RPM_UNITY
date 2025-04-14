using UnityEngine;
using UnityEngine.InputSystem;

public class M_LocalPlayerAnimator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InputActionProperty moveInput;
    [SerializeField] private Animator avatarAnimator;

    [Header("Animation")]
    [SerializeField] private string moveSpeedParam = "MoveSpeed";
    [SerializeField] private float smoothTime = 0.1f;

    [Header("Testing")]
    [SerializeField] private bool enableKeyboardTesting = true;

    private float currentVelocity;
    private float smoothedSpeed;

    void OnEnable()
    {
        if (moveInput != null && moveInput.action != null)
        {
            moveInput.action.Enable();
            Debug.Log("[M_LocalPlayerAnimator] Move input action enabled.");
        }
        else
        {
            Debug.LogWarning("[M_LocalPlayerAnimator] Move input action is null or not assigned.");
        }
    }

    void OnDisable()
    {
        if (moveInput != null && moveInput.action != null)
        {
            moveInput.action.Disable();
            Debug.Log("[M_LocalPlayerAnimator] Move input action disabled.");
        }
    }

    void Update()
    {
        Vector2 move = Vector2.zero;

        // XR controller input
        if (moveInput != null && moveInput.action != null && moveInput.action.enabled)
        {
            move = moveInput.action.ReadValue<Vector2>();
            Debug.Log($"[M_LocalPlayerAnimator] XR move input: {move}");
        }

        // Keyboard input for testing
        if (enableKeyboardTesting)
        {
            float h = (Keyboard.current.aKey.isPressed ? -1f : 0f) + (Keyboard.current.dKey.isPressed ? 1f : 0f);
            float v = (Keyboard.current.sKey.isPressed ? -1f : 0f) + (Keyboard.current.wKey.isPressed ? 1f : 0f);

            Vector2 keyboardMove = new Vector2(h, v);
            Debug.Log($"[M_LocalPlayerAnimator] Keyboard input: {keyboardMove}");

            if (keyboardMove.magnitude > move.magnitude)
            {
                move = keyboardMove;
                Debug.Log("[M_LocalPlayerAnimator] Overriding XR input with keyboard input.");
            }
        }

        float rawSpeed = Mathf.Clamp01(move.magnitude);
        Debug.Log($"[M_LocalPlayerAnimator] Raw movement speed: {rawSpeed}");

        smoothedSpeed = Mathf.SmoothDamp(smoothedSpeed, rawSpeed, ref currentVelocity, smoothTime);
        Debug.Log($"[M_LocalPlayerAnimator] Smoothed speed: {smoothedSpeed}");

        if (avatarAnimator != null)
        {
            avatarAnimator.SetFloat(moveSpeedParam, smoothedSpeed);
            Debug.Log($"[M_LocalPlayerAnimator] Animator parameter '{moveSpeedParam}' set to {smoothedSpeed}");
        }
        else
        {
            Debug.LogWarning("[M_LocalPlayerAnimator] Animator reference not set!");
        }
    }
}