using UnityEngine;

public class M_CameraDelta : MonoBehaviour
{
    [Header("Rotation Sources")]
    public Transform mainCameraRotation;  // XR Camera
    public Transform bodyRootRotation;    // Hips or body root

    [Header("Animator Settings")]
    public Animator rpmAnimator;
    public string parameterName = "hmdRotation";

    [Header("Lower Body Turn Settings")]
    [Tooltip("How fast the lower body rotates toward the HMD (degrees per second)")]
    public float turnSpeed = 5f;

    void Update()
    {
        // Safety check
        if (mainCameraRotation == null || bodyRootRotation == null || rpmAnimator == null)
            return;

        // Grab Y-axis (yaw) rotations
        float camY = mainCameraRotation.eulerAngles.y;
        float bodyY = bodyRootRotation.eulerAngles.y;

        // Calculate delta
        float delta = Mathf.DeltaAngle(camY, bodyY);
        rpmAnimator.SetFloat(parameterName, delta);

        Debug.Log($"[M_CameraDelta] hmdRotation = {delta}");

        // Smoothly rotate lower body toward camera yaw
        Quaternion currentRotation = bodyRootRotation.rotation;
        Quaternion targetRotation = Quaternion.Euler(0f, camY, 0f);
        bodyRootRotation.rotation = Quaternion.Slerp(currentRotation, targetRotation, Time.deltaTime * turnSpeed);
    }
}