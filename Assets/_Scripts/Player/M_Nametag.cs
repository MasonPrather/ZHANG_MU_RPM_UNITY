using UnityEngine;
using TMPro;
using Unity.Netcode;

public class M_Nametag : NetworkBehaviour
{
    [Header("UI Elements")]
    public TMP_Text playerNameTag; // 3D Text above the player's head

    private NetworkVariable<string> playerName = new NetworkVariable<string>(string.Empty);

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // If this player is a client (not the host), update the name
            if (!IsHost)
            {
                UpdatePlayerNameServerRpc("VP16");
            }
        }

        // Update the name tag when the value changes
        playerName.OnValueChanged += UpdateNameTag;
    }

    private void UpdateNameTag(string oldName, string newName)
    {
        playerNameTag.text = newName;
    }

    [ServerRpc(RequireOwnership = false)]
    private void UpdatePlayerNameServerRpc(string newName)
    {
        playerName.Value = newName;
    }
}