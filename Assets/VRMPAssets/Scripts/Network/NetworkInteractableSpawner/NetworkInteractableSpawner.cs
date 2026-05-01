using System;
using Unity.Netcode;
using XRMultiplayer;

namespace UnityEngine.XR.Templates.VRMultiplayer
{
    public class NetworkInteractableSpawner : NetworkBehaviour
    {
        public Transform spawnTransform
        {
            get
            {
                if (m_SpawnTransform == null)
                    return transform;
                return m_SpawnTransform;
            }
        }

        [SerializeField]
        Transform m_SpawnTransform;
        public NetworkBaseInteractable spawnInteractablePrefab;

        public bool freezeOnSpawn = true;
        public float distanceToSpawnNew = .15f;
        public float spawnCooldown = .5f;

        float m_SpawnCooldownTimer = 0f;

        /// <summary>
        /// The current network interactable object in the dispenser slot.
        /// </summary>
        NetworkBaseInteractable m_CurrentInteractable;

        NetworkVariable<NetworkObjectReference> m_CurrentInteractableReference = new NetworkVariable<NetworkObjectReference>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            m_CurrentInteractableReference.OnValueChanged += OnCurrentInteractableReferenceChanged;
            NetworkManager.Singleton.OnSessionOwnerPromoted += SessionOwnerPromoted;

            UpdateCurrentInteractable();
        }

        void SessionOwnerPromoted(ulong sessionOwnerId)
        {
            if (sessionOwnerId == NetworkManager.Singleton.LocalClientId)
            {
                m_SpawnCooldownTimer = spawnCooldown;
                UpdateCurrentInteractable();
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            m_SpawnCooldownTimer = spawnCooldown;
            m_CurrentInteractableReference.OnValueChanged -= OnCurrentInteractableReferenceChanged;
            NetworkManager.Singleton.OnSessionOwnerPromoted -= SessionOwnerPromoted;
        }

        void OnCurrentInteractableReferenceChanged(NetworkObjectReference previous, NetworkObjectReference current)
        {
            m_SpawnCooldownTimer = spawnCooldown;
            UpdateCurrentInteractable();
        }

        void UpdateCurrentInteractable()
        {
            if (!m_CurrentInteractableReference.Value.TryGet(out NetworkObject interactable) || !interactable.TryGetComponent(out m_CurrentInteractable))
            {
                m_CurrentInteractable = null;
            }
        }

        void Awake()
        {
            if (spawnInteractablePrefab == null)
            {
                Debug.LogError("No Interactable Prefab Assigned to the spawner. Disabling Component.", this);
                enabled = false;
                return;
            }
        }

        void Update()
        {
            if (!NetworkManager.Singleton.IsConnectedClient)
                return;

            if (IsOwner)
            {
                if (m_CurrentInteractable != null && m_CurrentInteractableReference.Value.TryGet(out NetworkObject interactable))
                {
                    if (CheckInteractablePosition())
                    {
                        m_SpawnCooldownTimer = spawnCooldown;
                        m_CurrentInteractableReference.Value = default;
                    }
                }
                else
                {
                    CheckSpawn();
                }
            }
        }

        bool CheckInteractablePosition()
        {
            return Vector3.Distance(m_CurrentInteractable.transform.position, spawnTransform.position) > distanceToSpawnNew;
        }

        void CheckSpawn()
        {
            if (m_SpawnCooldownTimer > 0)
            {
                m_SpawnCooldownTimer -= Time.deltaTime;
                return;
            }
            else
            {
                m_SpawnCooldownTimer = spawnCooldown;
                SpawnInteractablePrefab(spawnTransform);
            }
        }

        void SpawnInteractablePrefab(Transform spawnerTransform)
        {
            m_SpawnCooldownTimer = spawnCooldown;
            NetworkBaseInteractable spawnedInteractable = Instantiate
            (
                spawnInteractablePrefab,
                spawnerTransform.position,
                spawnerTransform.rotation
            );

            // Can only freeze physics interactables
            if (freezeOnSpawn && spawnedInteractable is NetworkPhysicsInteractable interactable)
                interactable.spawnLocked = true;

            spawnedInteractable.NetworkObject.Spawn();
            spawnedInteractable.transform.localScale = spawnerTransform.localScale;

            m_CurrentInteractableReference.Value = spawnedInteractable.NetworkObject;
        }


        [Serializable]
        public class GizmoHelper
        {
            public bool showMeshGizmo;
            public Color meshGizmoColor;

            [NonReorderable]
            public MeshPreviewHelper[] meshPreviewHelpers;

            public bool showWireGizmo;
            public bool showDistanceGizmoAsBillboard;
            public int billboardSegmentCount;
            public Color wireGizmoColor;
            public Color pivotPreviewColor;
            public Color pivotActiveColor;
            public Color lineDistanceColor;
        }

        [SerializeField]
        GizmoHelper m_GizmoHelper = new GizmoHelper()
        {
            showMeshGizmo = true,
            showWireGizmo = true,
            showDistanceGizmoAsBillboard = true,
            billboardSegmentCount = 36,
            wireGizmoColor = Color.yellow,
            meshGizmoColor = ColorUtility.TryParseHtmlString("#1993D0", out var color) ? color : Color.magenta,
            pivotPreviewColor = ColorUtility.TryParseHtmlString("#E26352", out color) ? color : Color.cyan,
            pivotActiveColor = ColorUtility.TryParseHtmlString("#FECF49", out color) ? color : Color.cyan,
            lineDistanceColor = ColorUtility.TryParseHtmlString("#5FD564", out color) ? color : Color.cyan,
        };

        [Serializable]
        public struct MeshPreviewHelper
        {
            public Mesh mesh;
            public MeshFilter meshFilter;
            public Renderer meshRenderer;
            public bool showPreview;
        }

        void OnValidate()
        {
            if (spawnInteractablePrefab != null && m_GizmoHelper.showMeshGizmo)
            {
                var previewMeshes = spawnInteractablePrefab.GetComponentsInChildren<MeshFilter>();
                if (previewMeshes.Length == m_GizmoHelper.meshPreviewHelpers.Length)
                    return;

                m_GizmoHelper.meshPreviewHelpers = new MeshPreviewHelper[previewMeshes.Length];
                for (int i = 0; i < previewMeshes.Length; i++)
                {
                    m_GizmoHelper.meshPreviewHelpers[i].mesh = previewMeshes[i].sharedMesh;
                    m_GizmoHelper.meshPreviewHelpers[i].meshFilter = previewMeshes[i];
                    m_GizmoHelper.meshPreviewHelpers[i].meshRenderer = previewMeshes[i].GetComponent<Renderer>();
                    m_GizmoHelper.meshPreviewHelpers[i].showPreview = true;
                }
            }
        }

        void OnDrawGizmos()
        {
            if (m_GizmoHelper.showMeshGizmo)
            {
                if (m_GizmoHelper.meshPreviewHelpers != null)
                {
                    for (int i = 0; i < m_GizmoHelper.meshPreviewHelpers.Length; i++)
                    {
                        if (!m_GizmoHelper.meshPreviewHelpers[i].showPreview)
                            continue;
                        var meshFilter = m_GizmoHelper.meshPreviewHelpers[i].meshFilter;
                        var meshTransform = meshFilter.transform;
                        var position = spawnTransform.position + (meshTransform.localPosition * spawnTransform.localScale.x);
                        var rotation = spawnTransform.localRotation * meshTransform.localRotation;
                        var scale = new Vector3(
                            spawnTransform.localScale.x * meshTransform.localScale.x,
                            spawnTransform.localScale.y * meshTransform.localScale.y,
                            spawnTransform.localScale.z * meshTransform.localScale.z
                        );
                        scale *= .99f;

                        Gizmos.color = m_GizmoHelper.meshGizmoColor;
                        if (m_GizmoHelper.meshPreviewHelpers[i].meshRenderer.sharedMaterial.HasColor("_BaseColor"))
                            Gizmos.color *= m_GizmoHelper.meshPreviewHelpers[i].meshRenderer.sharedMaterial.GetColor("_BaseColor");

                        Gizmos.DrawMesh(meshFilter.sharedMesh, position, rotation, scale);
                    }
                }
            }

            if (m_GizmoHelper.showWireGizmo)
            {
                Gizmos.color = m_GizmoHelper.wireGizmoColor;
                Gizmos g = new();
                g.DrawCircle(spawnTransform.position, distanceToSpawnNew, m_GizmoHelper.billboardSegmentCount, m_GizmoHelper.showDistanceGizmoAsBillboard);
                Gizmos.color = m_GizmoHelper.pivotPreviewColor;
                Gizmos.DrawWireSphere(spawnTransform.position, .01f);

                if (m_CurrentInteractable != null)
                {
                    Gizmos.color = m_GizmoHelper.pivotActiveColor;
                    Gizmos.DrawWireSphere(m_CurrentInteractable.transform.position, .01f);
                    Gizmos.color = m_GizmoHelper.lineDistanceColor;
                    Gizmos.DrawRay(spawnTransform.position, (m_CurrentInteractable.transform.position - spawnTransform.position).normalized * distanceToSpawnNew);
                }
            }
        }
    }
}
