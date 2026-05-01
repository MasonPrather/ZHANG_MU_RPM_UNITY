using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace XRMultiplayer
{
    public class DelayedUnityEvent : MonoBehaviour
    {
        [SerializeField, Tooltip("Allow Event to happen when script or object is disabled.")] bool m_ExecuteWhileDisabled = false;
        [SerializeField, Tooltip("Time to wait until enabled.")] float m_TimeToEnable = 4.0f;
        [SerializeField, Tooltip("Event that will be fired.")] UnityEvent m_UnityEvent;
        Coroutine m_EnablingRoutine;

        private void OnEnable()
        {
            if (m_EnablingRoutine != null) StopCoroutine(m_EnablingRoutine);
            m_EnablingRoutine = StartCoroutine(EnableAfterTime());
        }

        private void OnDisable()
        {
            if (m_EnablingRoutine != null) StopCoroutine(m_EnablingRoutine);
        }

        IEnumerator EnableAfterTime()
        {
            yield return new WaitForSeconds(m_TimeToEnable);
            if (!m_ExecuteWhileDisabled && (!gameObject.activeInHierarchy || !gameObject.activeSelf || !this.enabled))
                yield break; // Exit if the object is not active and we are not executing while disabled
            m_UnityEvent.Invoke();
        }
    }
}
