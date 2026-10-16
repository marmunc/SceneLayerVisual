using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SceneLayerVisuals.Editor
{
    [CustomEditor(typeof(SceneLayerVisual)), CanEditMultipleObjects]
    public sealed class SceneLayerVisualEditor : UnityEditor.Editor
    {
        private static readonly GUIContent BlurRadiusLabel = new("Blur Radius (pixels)");
        private SerializedProperty _blurRadius;
        private SerializedProperty[] _colorAndQuality;

        private void OnEnable()
        {
            _blurRadius = serializedObject.FindProperty(nameof(SceneLayerVisual.blurRadius));
            _colorAndQuality = new[]
            {
                serializedObject.FindProperty(nameof(SceneLayerVisual.exposure)),
                serializedObject.FindProperty(nameof(SceneLayerVisual.tint)),
                serializedObject.FindProperty(nameof(SceneLayerVisual.tintStrength)),
                serializedObject.FindProperty(nameof(SceneLayerVisual.saturation)),
                serializedObject.FindProperty(nameof(SceneLayerVisual.contrast)),
                serializedObject.FindProperty(nameof(SceneLayerVisual.resolution))
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("Blur", EditorStyles.boldLabel);
            EditorGUILayout.Slider(_blurRadius, 0, SceneLayerVisual.MaxBlurRadius, BlurRadiusLabel);
            EditorGUILayout.LabelField("Color", EditorStyles.boldLabel);
            for (int i = 0; i < _colorAndQuality.Length - 1; i++) EditorGUILayout.PropertyField(_colorAndQuality[i]);
            EditorGUILayout.LabelField("Quality", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_colorAndQuality[^1]);
            EditorGUILayout.HelpBox("Preview works with orthographic cameras (Scene View: 2D). " +
                "The complete layer is processed after lighting. Start with Half resolution. " +
                "Disable the component to restore original rendering and release its resources. " +
                "Use on scenery groups whose children form one sorting block; avoid nested effect groups.", MessageType.Info);
            if (_blurRadius.hasMultipleDifferentValues || _blurRadius.floatValue > 24)
                EditorGUILayout.HelpBox("Wide blur automatically reduces capture resolution, up to 8x per dimension, even when Full is selected.", MessageType.Info);
            foreach (var selectedTarget in targets)
            {
                var layer = (SceneLayerVisual)selectedTarget;
                if (layer.transform.parent != null && layer.transform.parent.GetComponentInParent<SceneLayerVisual>() != null)
                {
                    EditorGUILayout.HelpBox("Nested effect groups are outside v0.1 support. Use sibling groups instead.", MessageType.Warning);
                    break;
                }
                if (layer.GetComponent<SortingGroup>() == null)
                {
                    EditorGUILayout.HelpBox("Use a SortingGroup on the container for an explicit composite sorting position. Children cannot interleave with outside objects after processing.", MessageType.Warning);
                    break;
                }
            }
            if (GUILayout.Button("Reset Effects"))
            {
                _blurRadius.floatValue = 0;
                serializedObject.FindProperty(nameof(SceneLayerVisual.exposure)).floatValue = 0;
                serializedObject.FindProperty(nameof(SceneLayerVisual.tintStrength)).floatValue = 0;
                serializedObject.FindProperty(nameof(SceneLayerVisual.tint)).colorValue = Color.white;
                serializedObject.FindProperty(nameof(SceneLayerVisual.saturation)).floatValue = 1;
                serializedObject.FindProperty(nameof(SceneLayerVisual.contrast)).floatValue = 1;
            }
            if (serializedObject.ApplyModifiedProperties())
            {
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }

        [MenuItem("GameObject/Scene Layer Visual/Add Visual Controls To Scene Layers", false, 20)]
        private static void AddControls()
        {
            var selected = Selection.activeGameObject;
            if (selected == null) return;
            if (EditorUtility.IsPersistent(selected))
            {
                var path = AssetDatabase.GetAssetPath(selected);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    AddToChildren(root, false);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            else
            {
                Undo.SetCurrentGroupName("Add Scene Layer Visual controls");
                int undoGroup = Undo.GetCurrentGroup();
                AddToChildren(selected, true);
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        private static void AddToChildren(GameObject root, bool undo)
        {
            foreach (Transform child in root.transform)
            {
                if (child.GetComponent<SceneLayerVisual>() != null) continue;
                if (undo) Undo.AddComponent<SceneLayerVisual>(child.gameObject);
                else child.gameObject.AddComponent<SceneLayerVisual>();
            }
        }

        [MenuItem("GameObject/Scene Layer Visual/Add Visual Controls To Scene Layers", true)]
        private static bool CanAddControls() => Selection.activeGameObject != null;
    }
}
