using System;
using System.Collections.Concurrent;

public static class UnityMainThreadDispatcher
{
    // Queue of actions to run on the Unity main thread
    private static readonly ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();

    /// <summary>
    /// Enqueue an action to be executed on the Unity main thread.
    /// Call this from any background thread.
    /// </summary>
    public static void Enqueue(Action action)
    {
        if (action == null) return;
        _actions.Enqueue(action);
    }

    /// <summary>
    /// Called from a MonoBehaviour's Update() on the main thread.
    /// </summary>
    public static void Update()
    {
        while (_actions.TryDequeue(out var action))
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception)
            {
                // Optionally log errors, but keep this generic helper lightweight.
            }
        }
    }
}