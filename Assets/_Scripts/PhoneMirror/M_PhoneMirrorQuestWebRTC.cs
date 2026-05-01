using System;
using System.Collections;
using TMPro;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UI;

public class M_PhoneMirrorQuestWebRTC : MonoBehaviour
{
    public M_QuestSignalingHostTcp signalingHost;

    public RawImage phoneRawImage;
    public TMP_Text statusText;

    public string stunUrl = "stun:stun.l.google.com:19302";

    private RTCPeerConnection _pc;
    private RTCDataChannel _inputChannel;
    private VideoStreamTrack _remoteVideo;
    private bool _webrtcUpdateRunning;
    private Texture _latestFrame;
    private bool _gotFrame;

    [Serializable]
    private class SigMsg
    {
        public string type;
        public string sdp;
        public string candidate;
        public string sdpMid;
        public int? sdpMLineIndex;
    }

    private void Update()
    {
        if (_gotFrame && phoneRawImage != null)
        {
            _gotFrame = false;
            phoneRawImage.texture = _latestFrame;
        }
    }

    private void OnEnable()
    {
        if (signalingHost == null)
        {
            Debug.LogError("[Quest/WebRTC] signalingHost not assigned.");
            enabled = false;
            return;
        }

        if (!_webrtcUpdateRunning)
        {
            _webrtcUpdateRunning = true;
            StartCoroutine(WebRTC.Update());
        }

        signalingHost.OnClientConnected += OnSignalingConnected;
        signalingHost.OnClientDisconnected += OnSignalingDisconnected;
        signalingHost.OnJsonReceived += OnSignalingJson;

        SetStatus("Waiting for phone...");
    }

    private void OnDisable()
    {
        if (signalingHost != null)
        {
            signalingHost.OnClientConnected -= OnSignalingConnected;
            signalingHost.OnClientDisconnected -= OnSignalingDisconnected;
            signalingHost.OnJsonReceived -= OnSignalingJson;
        }

        CleanupPeer();
    }

    private void OnSignalingConnected()
    {
        SetStatus("Paired. Waiting for WebRTC offer...");
    }

    private void OnSignalingDisconnected()
    {
        SetStatus("Disconnected");
        CleanupPeer();
    }

    private void OnSignalingJson(string json)
    {
        Debug.Log("[Quest/WebRTC] RX signaling: " + json);

        SigMsg msg;
        try { msg = JsonUtility.FromJson<SigMsg>(json); }
        catch { return; }

        if (msg == null || string.IsNullOrEmpty(msg.type)) return;

        if (msg.type == "offer")
            StartCoroutine(HandleOffer(msg.sdp));
        else if (msg.type == "ice")
            HandleRemoteIce(msg);
    }

    

    private IEnumerator HandleOffer(string sdp)
    {
        if (string.IsNullOrEmpty(sdp)) yield break;

        EnsurePeer();
        SetStatus("Received offer. Creating answer...");

        var offerDesc = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = sdp };

        var opSetRemote = _pc.SetRemoteDescription(ref offerDesc);
        yield return opSetRemote;
        if (opSetRemote.IsError)
        {
            SetStatus("SetRemote(offer) failed");
            yield break;
        }

        var opCreateAnswer = _pc.CreateAnswer();
        yield return opCreateAnswer;
        if (opCreateAnswer.IsError)
        {
            SetStatus("CreateAnswer failed");
            yield break;
        }

        var answerDesc = opCreateAnswer.Desc;

        var opSetLocal = _pc.SetLocalDescription(ref answerDesc);
        yield return opSetLocal;
        if (opSetLocal.IsError)
        {
            SetStatus("SetLocal(answer) failed");
            yield break;
        }

        var answerMsg = new SigMsg { type = "answer", sdp = _pc.LocalDescription.sdp };
        signalingHost.SendJson(JsonUtility.ToJson(answerMsg));

        SetStatus("Answer sent. Connecting...");
    }

    private void HandleRemoteIce(SigMsg iceMsg)
    {
        if (_pc == null) return;
        if (string.IsNullOrEmpty(iceMsg.candidate)) return;

        var init = new RTCIceCandidateInit
        {
            candidate = iceMsg.candidate,
            sdpMid = iceMsg.sdpMid,
            sdpMLineIndex = iceMsg.sdpMLineIndex
        };
        _pc.AddIceCandidate(new RTCIceCandidate(init));
    }

    private void EnsurePeer()
    {
        if (_pc != null) return;

        Debug.Log("[Quest/WebRTC] Creating peer...");

        var config = new RTCConfiguration
        {
            iceServers = new[] { new RTCIceServer { urls = new[] { stunUrl } } }
        };

        _pc = new RTCPeerConnection(ref config);

        _pc.OnIceCandidate = cand =>
        {
            if (cand == null) return;
            var msg = new SigMsg
            {
                type = "ice",
                candidate = cand.Candidate,
                sdpMid = cand.SdpMid,
                sdpMLineIndex = cand.SdpMLineIndex
            };
            signalingHost.SendJson(JsonUtility.ToJson(msg));
        };

        _pc.OnTrack = e =>
        {
            Debug.Log("[Quest/WebRTC] OnTrack: " + e.Track?.Kind);

            if (e.Track is VideoStreamTrack v)
            {
                _remoteVideo = v;
                _remoteVideo.OnVideoReceived += OnRemoteVideoFrame;
                Debug.Log("[Quest/WebRTC] Video track bound.");
            }
        };

        _pc.OnDataChannel = ch =>
        {
            Debug.Log("[Quest/WebRTC] DataChannel: " + ch?.Label);

            if (ch != null && ch.Label == "input")
                _inputChannel = ch;
        };

        _pc.OnConnectionStateChange = s =>
        {
            if (s == RTCPeerConnectionState.Connected) SetStatus("Connected");
            if (s == RTCPeerConnectionState.Failed || s == RTCPeerConnectionState.Disconnected) SetStatus($"Connection {s}");
        };

        _pc.OnIceConnectionChange = state =>
            Debug.Log("[Quest/WebRTC] ICE state: " + state);

        _pc.OnConnectionStateChange = s =>
            Debug.Log("[Quest/WebRTC] PC state: " + s);
    }

    private void OnRemoteVideoFrame(Texture tex)
    {
        _latestFrame = tex;
        _gotFrame = true;
    }

    public bool CanSendInput => _inputChannel != null && _inputChannel.ReadyState == RTCDataChannelState.Open;

    public void SendInputJson(string json)
    {
        if (!CanSendInput) return;
        if (string.IsNullOrEmpty(json)) return;

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        _inputChannel.Send(bytes);
    }

    private void CleanupPeer()
    {
        try
        {
            if (_remoteVideo != null)
                _remoteVideo.OnVideoReceived -= OnRemoteVideoFrame;
        }
        catch { }

        _remoteVideo = null;

        try { _inputChannel?.Close(); } catch { }
        _inputChannel = null;

        try { _pc?.Close(); } catch { }
        _pc?.Dispose();
        _pc = null;
    }

    private void SetStatus(string s)
    {
        if (statusText != null) statusText.text = s;
    }
}
