using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class PlayerValidator : EditorWindow
{
    [MenuItem("Tools/Validate Player")]
    public static void ShowWindow()
    {
        GetWindow<PlayerValidator>("Player Validator");
    }

    private GameObject playerObject;

    private void OnGUI()
    {
        GUILayout.Label("Select the root 'Player' GameObject in your scene.", EditorStyles.boldLabel);
        playerObject = (GameObject)EditorGUILayout.ObjectField("Player Object", playerObject, typeof(GameObject), true);

        if (GUILayout.Button("Validate"))
        {
            if (playerObject == null)
            {
                Debug.LogWarning("[Validator] Please assign a Player GameObject.");
                return;
            }

            ValidatePlayer(playerObject);
        }
    }

    private void ValidatePlayer(GameObject player)
    {
        Debug.Log("<color=#33FF64>[Validator]</color> Starting Player validation...");

        var cameraOffset = player.transform.Find("Camera Offset");
        if (cameraOffset == null)
        {
            Debug.LogError("[Validator] Missing 'Camera Offset' under Player.");
            return;
        }

        CheckComponent<CharacterController>(player);
        CheckComponent<XRInteractionManager>(player, optional: true);

        var leftController = cameraOffset.Find("XR Controller Left");
        var rightController = cameraOffset.Find("XR Controller Right");

        if (leftController == null)
            Debug.LogError("[Validator] Missing 'XR Controller Left' under Camera Offset.");
        else
            CheckChildRayInteractor(leftController, "Ray Interactor");

        if (rightController == null)
            Debug.LogError("[Validator] Missing 'XR Controller Right' under Camera Offset.");
        else
            CheckChildRayInteractor(rightController, "Ray Interactor");

        var cam = cameraOffset.Find("Main Camera");
        if (cam == null)
            Debug.LogError("[Validator] Missing 'Main Camera' under Camera Offset.");

        var canvas = player.transform.Find("Canvas (Body-locked UI)");
        if (canvas != null)
        {
            var canvasComp = canvas.GetComponent<Canvas>();
            if (canvasComp == null)
                Debug.LogWarning("[Validator] Canvas is missing Canvas component.");
            else if (canvasComp.renderMode != RenderMode.WorldSpace)
                Debug.LogWarning("[Validator] Canvas is not set to World Space.");

            if (canvas.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>() == null)
                Debug.LogWarning("[Validator] Canvas is missing TrackedDeviceGraphicRaycaster.");
        }

        Debug.Log("<color=#33FF64>[Validator]</color> Player validation complete.");
    }

    private void CheckComponent<T>(GameObject obj, bool optional = false) where T : Component
    {
        var comp = obj.GetComponent<T>();
        if (comp == null)
        {
            if (optional)
                Debug.LogWarning($"[Validator] Optional component '{typeof(T).Name}' is missing on {obj.name}.");
            else
                Debug.LogError($"[Validator] Required component '{typeof(T).Name}' is missing on {obj.name}.");
        }
    }

    private void CheckChildRayInteractor(Transform parent, string childName)
    {
        var child = parent.Find(childName);
        if (child == null)
        {
            Debug.LogError($"[Validator] {parent.name} is missing expected child '{childName}'.");
            return;
        }

        if (child.GetComponent<XRRayInteractor>() == null)
        {
            Debug.LogError($"[Validator] '{childName}' under {parent.name} is missing XRRayInteractor.");
        }

        if (child.GetComponent<LineRenderer>() == null)
        {
            Debug.LogWarning($"[Validator] '{childName}' under {parent.name} is missing LineRenderer.");
        }
    }
}