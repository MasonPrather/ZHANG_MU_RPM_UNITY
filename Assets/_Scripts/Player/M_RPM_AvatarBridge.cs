using UnityEngine;
using ReadyPlayerMe.MetaMovement;

public class M_RPM_AvatarBridge : MonoBehaviour
{
    [SerializeField] private DynamicMovementLoader loader;

    private void Awake()
    {
        loader.OnAvatarObjectLoaded.AddListener(HandleAvatarLoaded);
    }

    private void HandleAvatarLoaded(GameObject avatar)
    {
        M_OVR_RigReferences.Singleton.AssignAvatarReferences(avatar);
    }
}