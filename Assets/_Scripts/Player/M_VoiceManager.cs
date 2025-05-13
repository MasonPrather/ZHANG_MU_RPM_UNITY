using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Vivox;
using System.Threading.Tasks;

public class M_VoiceManager : MonoBehaviour
{
    [SerializeField] private string channelName = "lobby-voice";

    private async void Start()
    {
        await InitializeVivoxAsync();
    }

    private async Task InitializeVivoxAsync()
    {
        if (!UnityServices.State.Equals(ServicesInitializationState.Initialized))
        {
            await UnityServices.InitializeAsync();
        }

        await VivoxService.Instance.InitializeAsync();

        VivoxService.Instance.ParticipantAddedToChannel += OnParticipantAdded;
        VivoxService.Instance.ParticipantRemovedFromChannel += OnParticipantRemoved;

        if (!VivoxService.Instance.IsLoggedIn)
        {
            await VivoxService.Instance.LoginAsync();
        }

        await VivoxService.Instance.JoinGroupChannelAsync(channelName, ChatCapability.AudioOnly);
    }

    private void OnParticipantAdded(VivoxParticipant participant)
    {
        Debug.Log($"Participant joined: {participant.PlayerId}");
    }

    private void OnParticipantRemoved(VivoxParticipant participant)
    {
        Debug.Log($"Participant left: {participant.PlayerId}");
    }

    private async void OnApplicationQuit()
    {
        if (VivoxService.Instance != null)
        {
            await VivoxService.Instance.LeaveAllChannelsAsync();
            await VivoxService.Instance.LogoutAsync();

            VivoxService.Instance.ParticipantAddedToChannel -= OnParticipantAdded;
            VivoxService.Instance.ParticipantRemovedFromChannel -= OnParticipantRemoved;
        }
    }
}