using UnityEngine;

public class M_NetPlayerAnimator : MonoBehaviour
{
    [SerializeField] private Animator avatarAnimator;
    [SerializeField] private Transform trackedTransform;

    private Vector3 lastPosition;

    void Start()
    {
        if (trackedTransform == null)
            trackedTransform = transform;

        lastPosition = trackedTransform.position;
    }

    void Update()
    {
        Vector3 velocity = (trackedTransform.position - lastPosition) / Time.deltaTime;
        lastPosition = trackedTransform.position;

        float speed = new Vector2(velocity.x, velocity.z).magnitude;
        avatarAnimator.SetFloat("MoveSpeed", speed);
    }
}