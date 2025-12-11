using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Per-player replicated state (extend as needed).
/// Currently holds the Ready Player Me avatar URL.
/// </summary>
public class M_NetPlayer : NetworkBehaviour
{
    [SerializeField]
    private string defaultAvatarUrl =
        "https://models.readyplayer.me/67f49ac1b494e717079b6019.glb?morphTargets=ARKit,Oculus%20Visemes";

    public NetworkVariable<FixedString512Bytes> AvatarUrl =
        new(writePerm: NetworkVariableWritePermission.Owner);

    public override void OnNetworkSpawn()
    {
        if (IsOwner) TrySetUrlOnce();
    }

    private void TrySetUrlOnce()
    {
        if (AvatarUrl.Value.Length > 0) return;

        var url = PlayerPrefs.GetString("RPM_URL", defaultAvatarUrl);
        AvatarUrl.Value = url;
    }
}