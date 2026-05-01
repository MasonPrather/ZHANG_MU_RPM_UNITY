using UnityEngine.UIElements;
using UnityEngine.XR.Templates.VRMultiplayer;
using static UnityEngine.XR.Templates.VRMultiplayer.NetworkInteractableSpawner;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
#endif

#if UNITY_EDITOR
[CustomEditor(typeof(NetworkInteractableSpawner))]
[CanEditMultipleObjects]
public class NetworkInteractableSpawnerEditor : Editor
{
    public override VisualElement CreateInspectorGUI()
    {
        // Create a new VisualElement to be the root of our Inspector UI.
        var root = new VisualElement();

        // Add fields for the properties of the NetworkInteractableSpawner component.
        var spawnPrefab = serializedObject.FindProperty("spawnInteractablePrefab");

        var spawnPrefabPropteryField = new PropertyField(spawnPrefab, "Spawn Interactable Prefab");
        root.Add(new PropertyField(serializedObject.FindProperty("m_SpawnTransform"), "Spawn Transform"));
        root.Add(spawnPrefabPropteryField);

        PropertyField freezeOnSpawnField = new PropertyField(serializedObject.FindProperty("freezeOnSpawn"), "Freeze On Spawn");
        freezeOnSpawnField.BindProperty(serializedObject.FindProperty("freezeOnSpawn"));

        PropertyField distanceToSpawnNewField = new PropertyField(serializedObject.FindProperty("distanceToSpawnNew"), "Distance To Spawn New");
        distanceToSpawnNewField.BindProperty(serializedObject.FindProperty("distanceToSpawnNew"));

        PropertyField spawnCooldownField = new PropertyField(serializedObject.FindProperty("spawnCooldown"), "Spawn Cooldown");
        spawnCooldownField.BindProperty(serializedObject.FindProperty("spawnCooldown"));


        spawnPrefabPropteryField.BindProperty(spawnPrefab);

        // Add a label to the Inspector UI.
        var label = new Label("Spawned Object Preview");
        label.style.marginTop = 10;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;

        // Create foldout for the Gizmo Helper
        var gizmoFoldout = new Foldout();
        gizmoFoldout.text = "Preview Settings";
        gizmoFoldout.AddToClassList("unity-base-field__aligned");

        // View Data Key is used to save the foldout state going between multiple objects
        gizmoFoldout.viewDataKey = GetFoldoutDataKey(serializedObject);

        var warningLabel = new Label("Warning: No prefab selected. Please select a prefab to spawn.");
        warningLabel.style.color = Color.red;

        var gizmoHelper = serializedObject.FindProperty("m_GizmoHelper");
        gizmoFoldout.BindProperty(gizmoHelper);
        gizmoFoldout.Add(new PropertyField(gizmoHelper));

        // Add and remove to force update of properties
        root.Add(gizmoFoldout);
        root.Remove(gizmoFoldout);

        AddOrRemoveGizmosFoldout(root, spawnPrefab);

        spawnPrefabPropteryField.RegisterValueChangeCallback((e) => AddOrRemoveGizmosFoldout(root, spawnPrefab));

        void AddOrRemoveGizmosFoldout(VisualElement container, SerializedProperty changedProperty)
        {
            if (changedProperty.objectReferenceValue == null)
            {
                // If the prefab is null, remove the gizmo foldout and add a warning label

                if (container.Contains(freezeOnSpawnField))
                    container.Remove(freezeOnSpawnField);

                if (container.Contains(distanceToSpawnNewField))
                    container.Remove(distanceToSpawnNewField);

                if (container.Contains(spawnCooldownField))
                    container.Remove(spawnCooldownField);

                if (container.Contains(label))
                    container.Remove(label);

                if (container.Contains(gizmoFoldout))
                    container.Remove(gizmoFoldout);

                if (!container.Contains(warningLabel))
                    container.Add(warningLabel);
            }
            else
            {
                // If the prefab is not null, add the gizmo foldout and remove the warning label
                if (!container.Contains(freezeOnSpawnField))
                    container.Add(freezeOnSpawnField);

                if (!container.Contains(distanceToSpawnNewField))
                    container.Add(distanceToSpawnNewField);

                if (!container.Contains(spawnCooldownField))
                    container.Add(spawnCooldownField);

                if (!container.Contains(label))
                    container.Add(label);

                if (!container.Contains(gizmoFoldout))
                    container.Add(gizmoFoldout);

                if (container.Contains(warningLabel))
                    container.Remove(warningLabel);

                gizmoFoldout.value = false;
            }
        }

        // Return the finished Inspector UI.
            return root;
    }

    private string GetFoldoutDataKey(SerializedObject serializedObject)
    {
        return $"{serializedObject.targetObject.GetInstanceID()}.{serializedObject.targetObject.name}";
    }
}

[CustomPropertyDrawer(typeof(MeshPreviewHelper))]
public class MeshPreviewHelper_PropertyDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        var root = new VisualElement();
        var showPreview = property.FindPropertyRelative("showPreview");

        var meshField = new PropertyField(property.FindPropertyRelative("mesh"), " ");
        meshField.AddToClassList("unity-base-field__aligned");

        meshField.SetEnabled(false);

        var toggle = new Toggle("Show Mesh Preview");
        toggle.labelElement.visible = false;

        toggle.value = showPreview.boolValue;
        toggle.BindProperty(showPreview);

        var toggleContainer = new VisualElement();

        toggleContainer.style.flexDirection = FlexDirection.Row;

        //Move toggle container to the left
        toggleContainer.style.marginLeft = -50;

        toggleContainer.Add(toggle);
        toggleContainer.Add(meshField);

        root.Add(toggleContainer);

        return root;
    }
}

[CustomPropertyDrawer(typeof(GizmoHelper))]
public class GizmoHelper_PropertyDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        var root = new VisualElement();

        root.Add(GetMeshContainer(property));
        root.Add(GetWireContainer(property));

        return root;
    }

    VisualElement GetMeshContainer(SerializedProperty property)
    {
        var container = new VisualElement();
        SerializedProperty showMeshGizmo = property.FindPropertyRelative("showMeshGizmo");
        Toggle showMeshGizmoToggle = new Toggle("Show Mesh Preview");
        showMeshGizmoToggle.value = showMeshGizmo.boolValue;
        showMeshGizmoToggle.AddToClassList("unity-base-field__aligned");
        showMeshGizmoToggle.BindProperty(showMeshGizmo);
        container.Add(showMeshGizmoToggle);

        ColorField meshGizmoColor = new ColorField("Mesh Preview Color");
        meshGizmoColor.AddToClassList("unity-base-field__aligned");
        meshGizmoColor.BindProperty(property.FindPropertyRelative("meshGizmoColor"));

        PropertyField meshPreviews = new PropertyField(property.FindPropertyRelative("meshPreviewHelpers"), "Mesh Previews");

        SetExtraFieldsActive(showMeshGizmo.boolValue);

        // Add a change event to show/hide the fields based on the toggle state
        showMeshGizmoToggle.RegisterValueChangedCallback((e) => SetExtraFieldsActive(e.newValue));

        void SetExtraFieldsActive(bool show)
        {
            SetContainerActive(container, show);
            if (show)
            {
                container.Add(meshGizmoColor);
                container.Add(meshPreviews);
            }
            else
            {
                if (container.Contains(meshGizmoColor))
                    container.Remove(meshGizmoColor);
                if(container.Contains(meshPreviews))
                    container.Remove(meshPreviews);
            }
        }

        return container;
    }


    VisualElement GetWireContainer(SerializedProperty property)
    {
        var container = new VisualElement();
        SerializedProperty showWireGizmo = property.FindPropertyRelative("showWireGizmo");
        Toggle showWireGizmoToggle = new Toggle("Show Spawn Distance Gizmo");
        showWireGizmoToggle.AddToClassList("unity-base-field__aligned");
        showWireGizmoToggle.BindProperty(showWireGizmo);
        container.Add(showWireGizmoToggle);

        Slider billboardSegmentCount = new Slider("Billboard Segment Count");
        billboardSegmentCount.AddToClassList("unity-base-field__aligned");
        billboardSegmentCount.BindProperty(property.FindPropertyRelative("billboardSegmentCount"));
        billboardSegmentCount.showInputField = true;
        billboardSegmentCount.lowValue = 12;
        billboardSegmentCount.highValue = 36;

        ColorField wireGizmoColor = new ColorField("Wire Gizmo Color");
        wireGizmoColor.AddToClassList("unity-base-field__aligned");
        wireGizmoColor.BindProperty(property.FindPropertyRelative("wireGizmoColor"));

        ColorField pivotPreviewColor = new ColorField("Pivot Preview Color");
        pivotPreviewColor.AddToClassList("unity-base-field__aligned");
        pivotPreviewColor.BindProperty(property.FindPropertyRelative("pivotPreviewColor"));

        ColorField pivotActiveColor = new ColorField("Pivot Active Color");
        pivotActiveColor.AddToClassList("unity-base-field__aligned");
        pivotActiveColor.BindProperty(property.FindPropertyRelative("pivotActiveColor"));

        ColorField lineDistanceColor = new ColorField("Line Distance Color");
        lineDistanceColor.AddToClassList("unity-base-field__aligned");
        lineDistanceColor.BindProperty(property.FindPropertyRelative("lineDistanceColor"));

        SetExtraFieldsActive(showWireGizmo.boolValue);

        // Add a change event to show/hide the fields based on the toggle state
        showWireGizmoToggle.RegisterValueChangedCallback(evt =>
        {
            SetExtraFieldsActive(evt.newValue);
        });

        void SetExtraFieldsActive(bool show)
        {
            SetContainerActive(container, show);
            if (show)
            {
                container.Add(billboardSegmentCount);
                container.Add(wireGizmoColor);
                container.Add(pivotPreviewColor);
                container.Add(pivotActiveColor);
                container.Add(lineDistanceColor);
            }
            else
            {
                if (container.Contains(billboardSegmentCount))
                    container.Remove(billboardSegmentCount);
                if (container.Contains(wireGizmoColor))
                    container.Remove(wireGizmoColor);
                if (container.Contains(pivotPreviewColor))
                    container.Remove(pivotPreviewColor);
                if (container.Contains(pivotActiveColor))
                    container.Remove(pivotActiveColor);
                if (container.Contains(lineDistanceColor))
                    container.Remove(lineDistanceColor);
            }
        }
        return container;
    }

    void SetContainerActive(VisualElement container, bool show)
    {
        if (show)
        {
            container.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            container.style.marginTop = 5;
            container.style.paddingTop = 5;
            container.style.marginBottom = 5;
            container.style.paddingBottom = 5;
        }
        else
        {
            container.style.backgroundColor = Color.clear;
            container.style.marginTop = 0;
            container.style.paddingTop = 0;
            container.style.marginBottom = 0;
            container.style.paddingBottom = 0;
        }
    }
}
#endif
