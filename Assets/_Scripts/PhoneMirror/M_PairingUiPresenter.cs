using TMPro;
using UnityEngine;

public class M_PairingUiPresenter : MonoBehaviour
{
    public M_PairingCodeProvider codeProvider;
    public M_QuestSignalingHostTcp signalingHost;

    public TMP_Text pairCodeText;
    public TMP_Text statusText;
    public TMP_Text networkHintText;

    private void OnEnable()
    {
        Refresh();

        if (codeProvider != null)
            codeProvider.OnCodeChanged += OnCodeChanged;

        if (signalingHost != null)
        {
            signalingHost.OnClientConnected += OnClientConnected;
            signalingHost.OnClientDisconnected += OnClientDisconnected;
            signalingHost.OnClientRejected += OnClientRejected;
        }
    }

    private void OnDisable()
    {
        if (codeProvider != null)
            codeProvider.OnCodeChanged -= OnCodeChanged;

        if (signalingHost != null)
        {
            signalingHost.OnClientConnected -= OnClientConnected;
            signalingHost.OnClientDisconnected -= OnClientDisconnected;
            signalingHost.OnClientRejected -= OnClientRejected;
        }
    }

    private void Refresh()
    {
        if (pairCodeText != null && codeProvider != null)
            pairCodeText.text = codeProvider.PairingCode;

        if (networkHintText != null && signalingHost != null)
            networkHintText.text = $"Signaling: {signalingHost.HostIp}:{signalingHost.port}";

        SetStatus("Waiting for iOS client...");
    }

    private void OnCodeChanged(string code)
    {
        if (pairCodeText != null) pairCodeText.text = code;
    }

    private void OnClientConnected() => SetStatus("Paired (HELLO/ACK). Waiting for WebRTC...");
    private void OnClientDisconnected() => SetStatus("Disconnected. Waiting...");
    private void OnClientRejected(string reason) => SetStatus($"Rejected: {reason}");

    private void SetStatus(string s)
    {
        if (statusText != null) statusText.text = s;
    }
}
