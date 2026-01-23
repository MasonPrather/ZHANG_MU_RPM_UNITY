using UnityEngine;

/// <summary>
/// Simple MonoBehaviour that pumps UnityMainThreadDispatcher each frame.
/// Attach this to any GameObject in your scene.
/// </summary>
public class UnityMainThreadRunner : MonoBehaviour
{
    private void Update()
    {
        UnityMainThreadDispatcher.Update();
    }
}