using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SceneLayerVisuals
{
    internal static class SceneLayerVisualRendering
    {
        private static readonly List<SceneLayerVisual> _layers = new();
        private static readonly List<SceneLayerVisual> _visibleLayers = new();
        private static readonly List<SceneLayerVisual> _processedLayers = new();
        private static readonly List<SceneLayerVisual> _pendingReleases = new();
        private static readonly Plane[] _frustumPlanes = new Plane[6];
        private static readonly SceneLayerVisualCaptureState _captureState = new();
        private static Camera _captureCamera;
        private static Camera _viewer;
        private static bool _isCapturing;
        private static bool _hasReportedCaptureError;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState() => Shutdown();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterLoadedLayers()
        {
            foreach (var layer in UnityEngine.Object.FindObjectsByType<SceneLayerVisual>(FindObjectsSortMode.None))
            {
                if (layer.isActiveAndEnabled) Register(layer);
            }
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
                || state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                Shutdown();
            }
            else
            {
                RegisterLoadedLayers();
            }
        }
#endif

        private static void Shutdown()
        {
            RestoreViewer();
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            _isCapturing = false;
            ReleasePendingResources();
            foreach (var layer in _layers)
            {
                if (layer != null) layer.Release();
            }
            _layers.Clear();
            ReleaseCaptureCamera();
            _hasReportedCaptureError = false;
        }

        internal static void Register(SceneLayerVisual layer)
        {
            if (_layers.Contains(layer)) return;
            if (_layers.Count == 0)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            }
            _layers.Add(layer);
            _hasReportedCaptureError = false;
        }

        internal static void Unregister(SceneLayerVisual layer)
        {
            // Render callbacks can disable a component while the capture snapshot is in use.
            if (!_isCapturing) RestoreViewer();
            _layers.Remove(layer);
            if (_layers.Count != 0) return;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            if (!_isCapturing) ReleaseCaptureCamera();
        }

        internal static bool TryDeferRelease(SceneLayerVisual layer)
        {
            if (!_isCapturing) return false;
            if (!_pendingReleases.Contains(layer)) _pendingReleases.Add(layer);
            return true;
        }

        private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (_isCapturing) return;
            RestoreViewer();
            if (!CanProcessCamera(camera)) return;

            CollectVisibleLayers(camera);
            // Neutral and offscreen groups do not allocate helpers or scan the rest of the scene.
            if (_visibleLayers.Count == 0) return;

            _viewer = camera;
            _isCapturing = true;
            try
            {
                EnsureCaptureCamera();
                CaptureLayers(camera, GetRendererIndex(camera));
                ShowProcessedLayers();
            }
            catch (Exception exception)
            {
                RestoreViewer();
                if (!_hasReportedCaptureError)
                {
                    Debug.LogException(exception);
                    _hasReportedCaptureError = true;
                }
            }
            finally
            {
                _isCapturing = false;
                ReleasePendingResources();
                if (_layers.Count == 0)
                {
                    RestoreViewer();
                    ReleaseCaptureCamera();
                }
            }
        }

        private static bool CanProcessCamera(Camera camera)
        {
            if (!camera.orthographic || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
                || camera.stereoEnabled || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
                return false;

            return !camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)
                || (data.renderType != CameraRenderType.Overlay
                    && (camera.cameraType != CameraType.Game || data.cameraStack is not { Count: > 0 }));
        }

        private static void CollectVisibleLayers(Camera camera)
        {
            bool hasFrustumPlanes = false;
            foreach (var layer in _layers)
            {
                if (layer == null || !layer.HasEffects) continue;
                if (!hasFrustumPlanes)
                {
                    GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);
                    hasFrustumPlanes = true;
                }
                layer.CollectSources();
                if (layer.IsVisible(camera, _frustumPlanes)) _visibleLayers.Add(layer);
            }
        }

        private static void EnsureCaptureCamera()
        {
            if (_captureCamera != null) return;
            var helper = new GameObject("SceneLayerVisual capture") { hideFlags = HideFlags.HideAndDontSave };
            _captureCamera = helper.AddComponent<Camera>();
            _captureCamera.enabled = false;
        }

        private static int GetRendererIndex(Camera camera)
        {
            if (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)) return -1;
            var asset = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            // Match the actual viewer renderer, including an explicit 2D renderer override.
            for (int index = 0; index < asset.rendererDataList.Length; index++)
            {
                if (asset.GetRenderer(index) == data.scriptableRenderer) return index;
            }
            return -1;
        }

        private static void CaptureLayers(Camera camera, int rendererIndex)
        {
            try
            {
                _captureState.HideSceneForCapture(camera);
                foreach (var layer in _visibleLayers)
                {
                    if (layer == null || !layer.HasEffects || _pendingReleases.Contains(layer)
                        || !layer.Prepare(camera, _captureCamera, rendererIndex)) continue;
                    // Only group members switch between captures; outside artwork stays hidden.
                    SetSourcesHidden(layer, false);
                    layer.CaptureAndProcess(_captureCamera);
                    SetSourcesHidden(layer, true);
                    _processedLayers.Add(layer);
                }
            }
            finally
            {
                _captureState.Restore();
            }
        }

        private static void ShowProcessedLayers()
        {
            foreach (var layer in _processedLayers)
            {
                if (layer == null || !layer.HasEffects || _pendingReleases.Contains(layer)) continue;
                SetSourcesHidden(layer, true);
                layer.ShowProxy();
            }
        }

        private static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!_isCapturing && camera == _viewer) RestoreViewer();
        }

        private static void RestoreViewer()
        {
            _captureState.Restore();
            foreach (var layer in _visibleLayers)
            {
                if (layer != null) SetSourcesHidden(layer, false);
            }
            _visibleLayers.Clear();
            _processedLayers.Clear();
            _viewer = null;
            foreach (var layer in _layers)
            {
                if (layer != null) layer.HideProxy();
            }
        }

        private static void ReleasePendingResources()
        {
            // A destroyed MonoBehaviour can still own unparented native helpers. Release its
            // managed resource references too; do not skip it using Unity's destroyed-object check.
            foreach (var layer in _pendingReleases) layer.Release();
            _pendingReleases.Clear();
        }

        private static void ReleaseCaptureCamera()
        {
            if (_captureCamera != null) SceneLayerVisual.DestroyResource(_captureCamera.gameObject);
            _captureCamera = null;
        }

        private static void SetSourcesHidden(SceneLayerVisual layer, bool hidden)
        {
            foreach (var source in layer.Sources)
            {
                if (source != null) source.forceRenderingOff = hidden;
            }
        }
    }
}
