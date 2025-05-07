using UnityEngine;
using UnityEngine.XR;

public class M_XRGravityFix : MonoBehaviour
{
    public CharacterController characterController;
    public Transform cameraTransform;

    public float gravity = -9.81f;
    private float fallingSpeed = 0f;

    void Update()
    {
        // Align Character Controller height to the XR Camera (head)
        Vector3 center = cameraTransform.localPosition;
        characterController.height = center.y;
        characterController.center = new Vector3(center.x, center.y / 2, center.z);

        // Apply gravity
        if (characterController.isGrounded && fallingSpeed < 0)
            fallingSpeed = -1f;
        else
            fallingSpeed += gravity * Time.deltaTime;

        Vector3 gravityMove = new Vector3(0, fallingSpeed, 0);
        characterController.Move(gravityMove * Time.deltaTime);
    }
}