using System.Security.Cryptography;
using UnityEngine;

public class M_PairingCodeProvider : MonoBehaviour
{
    [SerializeField] private string pairingCode;
    public string PairingCode => pairingCode;

    public event System.Action<string> OnCodeChanged;

    private void Awake()
    {
        Regenerate();
    }

    [ContextMenu("Regenerate Pairing Code")]
    public void Regenerate()
    {
        pairingCode = GenerateCode();
        OnCodeChanged?.Invoke(pairingCode);
    }

    private static string GenerateCode()
    {
        int value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6");
    }
}
