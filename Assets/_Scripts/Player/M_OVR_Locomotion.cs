using System.Numerics;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class M_OVR_Locomotion : MonoBehaviour
{
    [Header("Locomotion Settings")]
    public float moveSpeed = 2.5f;
    public float gravity = -9.81f;
    public float stepOffset = 0.3f;

    [Header("OVR References")]
    public Transform cameraTransform; // Assign CenterEyeAnchor from OVRCameraRig

    private CharacterController characterController;
    private UnityEngine.Vector3 velocity;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        characterController.stepOffset = stepOffset;
    }

    private void Update()
    {
        UnityEngine.Vector2 input = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick); // Left joystick

        // Move relative to head's forward
        UnityEngine.Vector3 move = cameraTransform.forward * input.y + cameraTransform.right * input.x;
        move.y = 0f;
        move.Normalize();

        characterController.Move(move * moveSpeed * Time.deltaTime);

        // Apply gravity
        if (characterController.isGrounded)
        {
            velocity.y = -1f;
        }
        else
        {
            velocity.y += gravity * Time.deltaTime;
        }

        characterController.Move(velocity * Time.deltaTime);
    }
}