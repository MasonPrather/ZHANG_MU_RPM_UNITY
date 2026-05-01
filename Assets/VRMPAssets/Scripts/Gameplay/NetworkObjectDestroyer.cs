using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace XRMultiplayer
{
    /// <summary>
    /// This class is responsible for handling the networked despawning of objects based within
    /// a trigger volume. It also resets the player controller if they are within the bounds.
    /// It also handles the spawning of particles when an object is despawned.
    /// </summary>
    public class NetworkObjectDestroyer : NetworkBehaviour
    {
        /// <summary>
        /// The sub-trigger that triggers the action.
        /// </summary>
        [SerializeField] SubTrigger m_SubTrigger;

        /// <summary>
        /// The Y offset for the particles that are spawned.
        /// </summary>
        [SerializeField] float m_YOffset = .1f;

        /// <summary>
        /// The list of scene interactables that cannot be destroyed.
        /// </summary>
        [SerializeField] List<NetworkBaseInteractable> m_UndestroyableInteractables;

        readonly List<NetworkBaseInteractable> m_DestroyedInteractables = new();

        Pooler m_ParticlePooler;

        /// <summary>
        /// Awake is called when the script instance is being loaded.
        /// </summary>
        void Awake()
        {
            if (!TryGetComponent(out m_ParticlePooler))
            {
                Utils.LogError("NetworkObjectDestroyer requires a Pooler component to be attached to the same GameObject.");
                return;
            };
            m_SubTrigger.OnTriggerAction += Triggered;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            m_SubTrigger.OnTriggerAction -= Triggered;
        }

        /// <summary>
        /// Event handler for the trigger action.
        /// </summary>
        /// <param name="other">The collider that triggered the action.</param>
        /// <param name="entered">A flag indicating if the collider entered or exited the trigger.</param>
        void Triggered(Collider other, bool entered)
        {
            if (!entered) return;
            NetworkBaseInteractable networkBaseInteractable = other.GetComponentInParent<NetworkBaseInteractable>();

            if (networkBaseInteractable != null)
            {
                if (m_UndestroyableInteractables.Contains(networkBaseInteractable) || networkBaseInteractable.isInteracting || networkBaseInteractable.NetworkObject.IsSceneObject.Value) return;

                // This will prevent objects with multiple colliders from being destroyed multiple times.
                if (m_DestroyedInteractables.Contains(networkBaseInteractable))
                    return;
                m_DestroyedInteractables.Add(networkBaseInteractable);


                Vector3 position = networkBaseInteractable.transform.position + Vector3.up * m_YOffset;

                if (!networkBaseInteractable.IsSpawned)
                {
                    Destroy(networkBaseInteractable);
                }
                else
                {
                    if (!networkBaseInteractable.IsOwner)
                    {
                        networkBaseInteractable.NetworkObject.ChangeOwnership(NetworkManager.Singleton.LocalClientId);
                    }

                    if (networkBaseInteractable.IsOwner)
                        networkBaseInteractable.NetworkObject.Despawn();
                }
                PlayDestroyEffect(position);
            }
            else
            {
                if (other.TryGetComponent(out CharacterResetter playerResetter))
                {
                    PlayDestroyEffectRpc(playerResetter.transform.position);
                    playerResetter.ResetPlayer();
                }

                if (other.TryGetComponent(out Projectile projectile))
                {
                    PlayDestroyEffectRpc(projectile.transform.position);
                    projectile.ResetProjectile();
                }
            }
        }

        /// <summary>
        /// Plays the destroy effect at the specified position.
        /// </summary>
        /// <param name="position">The position at which to play the destroy effect.</param>
        void PlayDestroyEffect(Vector3 position)
        {
            GameObject particleObject = m_ParticlePooler.GetItem();
            particleObject.transform.position = position;
            StartCoroutine(ReturnParticleToPool(particleObject));
        }

        IEnumerator ReturnParticleToPool(GameObject particleObject)
        {
            yield return new WaitForSeconds(1.0f);
            m_ParticlePooler.ReturnItem(particleObject);
        }

        [Rpc(SendTo.Everyone)]
        void PlayDestroyEffectRpc(Vector3 position)
        {
            PlayDestroyEffect(position);
        }
    }
}
