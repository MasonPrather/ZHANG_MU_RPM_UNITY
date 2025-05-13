using UnityEngine;

public class M_Rotator : MonoBehaviour
{
    [Header("Rotation Settings")]
    [Tooltip("Rotation speed in degrees per second")]
    [SerializeField] public float rotationSpeed = 90f;

    // Update is called once per frame
    void Update()
    {
        // Rotate the object around its Y-axis at the given speed
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
    }
}