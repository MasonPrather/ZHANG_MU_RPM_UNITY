using UnityEngine;

public class M_CameraDelta : MonoBehaviour
{
    [Header("Rotation Sources")]
    public Transform mainCameraRotation;
    public Transform rightToeRotation;

    [Header("Animator Settings")]
    public Animator rpmAnimator;
    public string parameterName = "hmdRotation";

    void Update()
    {
        if (mainCameraRotation == null || rightToeRotation == null || rpmAnimator == null)
            return;

        // Get Yaw (Y-axis) rotations only
        float camY = mainCameraRotation.eulerAngles.y;
        float toeY = rightToeRotation.eulerAngles.y;

        // Calculate the signed delta angle
        float delta = Mathf.DeltaAngle(camY, toeY);

        // Optional: Normalize to 0..1 range if needed (not required, depends on your Animator)
        // float normalizedDelta = Mathf.InverseLerp(-180f, 180f, delta);

        // Set the Animator float
        rpmAnimator.SetFloat(parameterName, delta);
    }
}