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

    private float currentVelocity;
    private float smoothedSpeed;

    void Update()
    {
        Vector2 move = moveInput.action.ReadValue<Vector2>();
        float rawSpeed = move.magnitude;

        // Smooth the movement speed value
        smoothedSpeed = Mathf.SmoothDamp(smoothedSpeed, rawSpeed, ref currentVelocity, smoothTime);

        avatarAnimator.SetFloat(moveSpeedParam, smoothedSpeed);
    }
}