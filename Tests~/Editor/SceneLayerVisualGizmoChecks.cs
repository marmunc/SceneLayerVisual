using System;
using System.Reflection;
using SceneLayerVisuals;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class SceneLayerVisualGizmoChecks
{
    public static void Run()
    {
        // Headless Unity does not draw native editor gizmos. Supply SceneView's repaint context
        // and inspect the same ShouldRenderGizmos gate that URP uses inside the actual capture.
        // Reflection is confined to this test; the runtime uses the public Scene View API.
        var contextField = typeof(SceneView).GetField("s_CurrentDrawingSceneView", BindingFlags.Static | BindingFlags.NonPublic);
        if (contextField == null) throw new Exception("The tested Unity version has no Scene View drawing context.");
        var previousView = SceneView.currentDrawingSceneView;
        var view = ScriptableObject.CreateInstance<SceneView>();
        var root = new GameObject("Temporary gizmo regression fixture") { hideFlags = HideFlags.HideAndDontSave };
        var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), Vector2.one * .5f, 8);
        var material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
        var output = new RenderTexture(512, 256, 24);
        int captureCount = 0;
        bool capturedGizmos = false;
        Action<ScriptableRenderContext, Camera> observeCapture = (context, camera) =>
        {
            if (camera.name != "SceneLayerVisual capture") return;
            captureCount++;
            capturedGizmos |= Handles.ShouldRenderGizmos();
        };
        try
        {
            var colors = new Color[64];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            texture.SetPixels(colors); texture.Apply();
            var layerObject = new GameObject("Artwork");
            layerObject.transform.SetParent(root.transform); layerObject.layer = 30;
            var renderer = layerObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.sharedMaterial = material;
            layerObject.AddComponent<SceneLayerVisual>().blurRadius = 12;
            var cameraObject = new GameObject("Gizmo verification camera");
            cameraObject.transform.SetParent(root.transform);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 2;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 30; camera.targetTexture = output;
            var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            output.Create();
            contextField.SetValue(null, view);
            RenderPipelineManager.beginCameraRendering += observeCapture;
            for (int rendererIndex = 0; rendererIndex < 2; rendererIndex++)
            {
                cameraData.SetRenderer(rendererIndex);
                foreach (bool enabled in new[] { true, false })
                {
                    view.drawGizmos = enabled;
                    captureCount = 0; capturedGizmos = false;
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = output });
                    Debug.Log("SLV_GIZMO_MEASURE: renderer=" + rendererIndex + " toggle=" + enabled + " captureGizmos=" + capturedGizmos);
                    if (captureCount == 0) throw new Exception("The gizmo regression did not exercise a layer capture.");
                    if (capturedGizmos) throw new Exception("URP gizmos are enabled inside the layer capture.");
                    if (view.drawGizmos != enabled || Handles.ShouldRenderGizmos() != enabled)
                        throw new Exception("The viewer's gizmo toggle was changed by capture.");
                }
            }
            Debug.Log("SLV_GIZMOS_PASS: capture gizmos are suppressed and viewer toggle states are restored in both renderers.");
        }
        finally
        {
            RenderPipelineManager.beginCameraRendering -= observeCapture;
            contextField.SetValue(null, previousView);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(output); UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(sprite); UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(view);
        }
    }
}
