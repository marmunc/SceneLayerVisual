using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SceneLayerVisuals
{
    internal static class SceneLayerVisualRendering
    {
        private static readonly List<SceneLayerVisual> Layers = new();
        private static readonly List<Renderer> _savedRenderers = new();
        private static readonly List<SceneLayerVisual> _visibleLayers = new();
        private static readonly List<SceneLayerVisual> Processed = new();
        private static readonly Plane[] FrustumPlanes = new Plane[6];
        private static UnityEngine.Camera _captureCamera;
        private static UnityEngine.Camera _viewer;
        private static bool _capturing;
        private static bool _reportedError;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState() => Shutdown();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterLoadedLayers()
        {
            foreach (var layer in UnityEngine.Object.FindObjectsByType<SceneLayerVisual>(FindObjectsSortMode.None))
                if (layer.isActiveAndEnabled) Register(layer);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeEditorLifecycle()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            UnityEditor.EditorApplication.quitting -= Shutdown;
            UnityEditor.EditorApplication.quitting += Shutdown;
        }

        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingEditMode
                || state == UnityEditor.PlayModeStateChange.ExitingPlayMode) Shutdown();
            else RegisterLoadedLayers();
        }
#endif

        private static void Shutdown()
        {
            Restore();
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.endCameraRendering -= End;
            foreach (var layer in Layers) if (layer != null) layer.Release();
            Layers.Clear();
            if (_captureCamera != null) SceneLayerVisual.DestroyResource(_captureCamera.gameObject);
            _captureCamera = null;
            _capturing = false;
            _reportedError = false;
        }

        internal static void Register(SceneLayerVisual layer)
        {
            if (Layers.Contains(layer)) return;
            if (Layers.Count == 0)
            {
                RenderPipelineManager.beginCameraRendering += Begin;
                RenderPipelineManager.endCameraRendering += End;
            }
            Layers.Add(layer);
            _reportedError = false;
        }

        internal static void Unregister(SceneLayerVisual layer)
        {
            Restore();
            Layers.Remove(layer);
            if (Layers.Count != 0) return;
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.endCameraRendering -= End;
            if (_captureCamera != null) SceneLayerVisual.DestroyResource(_captureCamera.gameObject);
            _captureCamera = null;
        }

        private static void Begin(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (_capturing) return;
            Restore();
            if (!camera.orthographic || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
                || camera.stereoEnabled
                || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) return;
            if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var viewerData)
                && (viewerData.renderType == CameraRenderType.Overlay
                    || (camera.cameraType == CameraType.Game && viewerData.cameraStack is { Count: > 0 }))) return;
            bool hasFrustumPlanes = false;
            foreach (var layer in Layers)
            {
                if (layer == null || !layer.HasEffects) continue;
                if (!hasFrustumPlanes)
                {
                    GeometryUtility.CalculateFrustumPlanes(camera, FrustumPlanes);
                    hasFrustumPlanes = true;
                }
                layer.CollectSources();
                if (layer.IsVisible(camera, FrustumPlanes)) _visibleLayers.Add(layer);
            }
            // Invisible and neutral groups do not trigger a scene-wide renderer scan or helper creation.
            if (_visibleLayers.Count == 0) return;
            if (_captureCamera == null)
            {
                var helper = new GameObject("SceneLayerVisual capture") { hideFlags = HideFlags.HideAndDontSave };
                _captureCamera = helper.AddComponent<UnityEngine.Camera>();
                _captureCamera.enabled = false;
                var data = helper.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.renderShadows = true;
                data.volumeLayerMask = 0;
                data.antialiasing = AntialiasingMode.None;
            }
            int rendererIndex = -1;
            if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var cameraData))
            {
                var asset = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
                // Match the actual viewer renderer, including an explicit 2D renderer override.
                for (int i = 0; i < asset.rendererDataList.Length; i++)
                    if (asset.GetRenderer(i) == cameraData.scriptableRenderer) { rendererIndex = i; break; }
            }
            _viewer = camera;
            _capturing = true;
#if UNITY_EDITOR
            var sceneView = UnityEditor.SceneView.currentDrawingSceneView;
            bool restoreGizmos = sceneView != null && sceneView.drawGizmos;
#endif
            try
            {
#if UNITY_EDITOR
                // URP reads the current Scene View's gizmo flag even for our nested camera request.
                // Renderer isolation cannot hide camera outlines or OnDrawGizmos geometry.
                if (restoreGizmos) sceneView.drawGizmos = false;
#endif
                int cameraMask = camera.cullingMask;
                foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    // Keep masks and already-hidden/disabled/camera-excluded sources untouched.
                    if (renderer is SpriteMask || !renderer.enabled || renderer.forceRenderingOff
                        || (cameraMask != -1 && (cameraMask & (1 << renderer.gameObject.layer)) == 0)) continue;
                    _savedRenderers.Add(renderer);
                    renderer.forceRenderingOff = true;
                }
                foreach (var layer in _visibleLayers)
                {
                    if (!layer.Prepare(camera, _captureCamera, rendererIndex)) continue;
                    // Only switch members of the previous/current group between captures.
                    // Outside scenery stays hidden for the complete capture batch.
                    SetSourcesHidden(layer, false);
                    layer.CaptureAndProcess(_captureCamera);
                    SetSourcesHidden(layer, true);
                    Processed.Add(layer);
                }
                RestoreCaptureVisibility();
                foreach (var layer in Processed)
                {
                    SetSourcesHidden(layer, true);
                    layer.ShowProxy();
                }
            }
            catch (Exception exception)
            {
                Restore();
                if (!_reportedError) { Debug.LogException(exception); _reportedError = true; }
            }
            finally
            {
                _capturing = false;
#if UNITY_EDITOR
                if (restoreGizmos && sceneView != null) sceneView.drawGizmos = true;
#endif
            }
        }

        private static void End(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (!_capturing && camera == _viewer) Restore();
        }

        private static void Restore()
        {
            RestoreCaptureVisibility();
            foreach (var layer in _visibleLayers) if (layer != null) SetSourcesHidden(layer, false);
            _visibleLayers.Clear();
            Processed.Clear();
            _viewer = null;
            foreach (var layer in Layers) if (layer != null) layer.HideProxy();
        }

        private static void RestoreCaptureVisibility()
        {
            foreach (var renderer in _savedRenderers)
                if (renderer != null) renderer.forceRenderingOff = false;
            _savedRenderers.Clear();
        }

        private static void SetSourcesHidden(SceneLayerVisual layer, bool hidden)
        {
            foreach (var source in layer.Sources)
                if (source != null) source.forceRenderingOff = hidden;
        }
    }
}
