using System;
using Oculus.Interaction;
using UnityEngine;

public class M_PhonePanelRayInputSender : MonoBehaviour
{
    public RayInteractor rayInteractor;
    public MonoBehaviour selectorBehaviour; // must implement ISelector

    public RectTransform phoneRect;
    public Collider phoneCollider;
    public LayerMask phoneLayerMask;

    public M_PhoneMirrorQuestWebRTC webrtc;

    [Range(10, 120)] public int moveSendRateHz = 60;

    private ISelector _selector;
    private bool _isSelecting;
    private float _nextMoveSendTime;

    [Serializable]
    private struct TouchMsg
    {
        public string type;
        public string phase;
        public float x;
        public float y;
    }

    private void Awake()
    {
        if (selectorBehaviour is ISelector s)
            _selector = s;
    }

    private void OnEnable()
    {
        if (_selector != null)
        {
            _selector.WhenSelected += OnSelected;
            _selector.WhenUnselected += OnUnselected;
        }
    }

    private void OnDisable()
    {
        if (_selector != null)
        {
            _selector.WhenSelected -= OnSelected;
            _selector.WhenUnselected -= OnUnselected;
        }
    }

    private void Update()
    {
        if (!_isSelecting) return;
        if (Time.time < _nextMoveSendTime) return;
        if (!CanOperate()) return;

        if (TryGetPhoneUv(out Vector2 uv))
        {
            _nextMoveSendTime = Time.time + (1f / Mathf.Max(1, moveSendRateHz));
            SendTouch("move", uv);
        }
    }

    private void OnSelected()
    {
        _isSelecting = true;
        _nextMoveSendTime = 0f;

        if (!CanOperate()) return;
        if (TryGetPhoneUv(out Vector2 uv))
            SendTouch("down", uv);
    }

    private void OnUnselected()
    {
        if (CanOperate() && TryGetPhoneUv(out Vector2 uv))
            SendTouch("up", uv);

        _isSelecting = false;
    }

    private bool CanOperate()
    {
        return rayInteractor != null
            && phoneRect != null
            && phoneCollider != null
            && webrtc != null
            && webrtc.CanSendInput;
    }

    private bool TryGetPhoneUv(out Vector2 uv01)
    {
        uv01 = default;

        Ray ray = rayInteractor.Ray;

        if (!Physics.Raycast(ray, out RaycastHit hit, rayInteractor.MaxRayLength, phoneLayerMask, QueryTriggerInteraction.Collide))
            return false;

        if (hit.collider != phoneCollider)
            return false;

        Vector3 local = phoneRect.InverseTransformPoint(hit.point);
        Rect r = phoneRect.rect;

        float u = (local.x - r.xMin) / r.width;
        float v = (local.y - r.yMin) / r.height;

        if (float.IsNaN(u) || float.IsNaN(v))
            return false;

        uv01 = new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
        return true;
    }

    private void SendTouch(string phase, Vector2 uv)
    {
        var msg = new TouchMsg { type = "touch", phase = phase, x = uv.x, y = uv.y };
        webrtc.SendInputJson(JsonUtility.ToJson(msg));
    }
}
