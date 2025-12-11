using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Vivox;
using Unity.Services.Vivox.AudioTaps;
using System.Threading.Tasks;

public class M_VivoxManager : MonoBehaviour
{
    public static M_VivoxManager Instance { get; private set; }

    [Header("Optional")]
    [Tooltip("Local-only prefab with a VivoxAudioTap component (NOT a NetworkObject). Keep it disabled in the prefab.")]
    [SerializeField] private GameObject audioTapPrefab;

    public bool IsConnected => VivoxService.Instance != null && _initialized && _isLoggedIn;
    public bool IsReady => _isLoggedIn;

    public event System.Action VivoxReady;
    public event System.Action<string> ChannelJoined;
    public event System.Action<string> ChannelLeft;

    // ---- internal state
    static Task _initTask;
    static bool _initialized;
    bool _isLoggedIn;

    string _currentChannelName;

    // local-only audio tap instance (client side only; not networked)
    VivoxAudioTap _tap;

    // ---------------------- LIFECYCLE ----------------------

    private async void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        await InitializeVivoxAsync();
    }

    private async void OnApplicationQuit()
    {
        await SafeTearDownAsync();
    }

    // ---------------------- INIT (idempotent) ----------------------

    public async Task InitializeVivoxAsync()
    {
        if (_initialized) return;
        if (_initTask != null) { await _initTask; return; }

        _initTask = InitializeInternalAsync();
        await _initTask;
        _initialized = true;
    }

    static bool AlreadySigningInError(System.Exception ex)
        => ex != null && (ex.Message?.ToLowerInvariant().Contains("already signing in") ?? false);

    async Task InitializeInternalAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            try { await AuthenticationService.Instance.SignInAnonymouslyAsync(); }
            catch (System.Exception ex) { if (!AlreadySigningInError(ex)) throw; }
        }

        await VivoxService.Instance.InitializeAsync();

        if (!VivoxService.Instance.IsLoggedIn)
            await VivoxService.Instance.LoginAsync();

        _isLoggedIn = true;
        Debug.Log("[Vivox] Initialized + Logged in");
        VivoxReady?.Invoke();
    }

    // ---------------------- JOIN / LEAVE (async) ----------------------

    public async Task JoinChannelAsync(string channelName)
    {
        if (!_isLoggedIn)
        {
            Debug.LogWarning("[Vivox] Join requested before login; waiting for init…");
            await InitializeVivoxAsync();
        }

        // If already in a channel, leave first (unless it's the same)
        if (!string.IsNullOrEmpty(_currentChannelName))
        {
            if (_currentChannelName == channelName)
            {
                Debug.Log($"[Vivox] Already in channel '{channelName}'.");
                ChannelJoined?.Invoke(channelName);
                EnsureAudioTapActive(); // make sure tap is on
                return;
            }
            await LeaveChannelAsync();
        }

        // 3D positional example; swap to group if you prefer
        var props = new Channel3DProperties(
            audibleDistance: 50,
            conversationalDistance: 2,
            audioFadeIntensityByDistanceaudio: 1.0f,
            audioFadeModel: AudioFadeModel.InverseByDistance
        );

        // NOTE: This returns Task (no handle)
        await VivoxService.Instance.JoinPositionalChannelAsync(
            channelName,
            ChatCapability.AudioOnly,
            props
        );

        _currentChannelName = channelName;

        // Enable/register local tap AFTER join
        EnsureAudioTapActive();

        Debug.Log($"[Vivox] Joined channel: {channelName}");
        ChannelJoined?.Invoke(channelName);
    }

    public async Task LeaveChannelAsync()
    {
        if (string.IsNullOrEmpty(_currentChannelName))
            return;

        var name = _currentChannelName;

        // Turn off/destroy local tap before leaving to avoid “unknown channel”
        DestroyTap();

        try
        {
            await VivoxService.Instance.LeaveChannelAsync(name);
        }
        finally
        {
            _currentChannelName = null;
            Debug.Log($"[Vivox] Left channel: {name}");
            ChannelLeft?.Invoke(name);
        }
    }

    public async Task LeaveAllAsync()
    {
        DestroyTap();
        await VivoxService.Instance.LeaveAllChannelsAsync();
        _currentChannelName = null;
    }

    // ---------------------- AUDIO TAP (local-only) ----------------------
    // The current VivoxAudioTap auto-registers on OnEnable / UpdateStatus().
    // We only control WHEN it comes alive (after join), and kill it before leave.

    void EnsureAudioTapActive()
    {
        if (audioTapPrefab == null) return;

        if (_tap == null)
        {
            // Instantiate disabled → then enable so OnEnable runs AFTER join
            var go = Instantiate(audioTapPrefab);
            go.SetActive(false);
            _tap = go.GetComponent<VivoxAudioTap>();
        }

        if (_tap != null && !_tap.gameObject.activeSelf)
        {
            _tap.gameObject.SetActive(true);
        }
    }

    void DestroyTap()
    {
        if (_tap != null)
        {
            // No UnregisterTap / IsRegistered API in this package; disabling/destroying is the supported path
            Destroy(_tap.gameObject);
            _tap = null;
        }
    }

    // ---------------------- TEARDOWN ----------------------

    async Task SafeTearDownAsync()
    {
        if (!_isLoggedIn || VivoxService.Instance == null) return;

        DestroyTap();
        try { await VivoxService.Instance.LeaveAllChannelsAsync(); } catch { }
        try { await VivoxService.Instance.LogoutAsync(); } catch { }
        _isLoggedIn = false;
    }

    // ---------------------- LEGACY WRAPPERS (for existing callers) ----------------------
    // Your M_MenuManager was calling these names; keep them for compatibility.

    public void JoinChannel(string lobbyName) { _ = JoinChannelAsync(lobbyName); }
    public void LeaveChannel() { _ = LeaveChannelAsync(); }
}