using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SceneLayerVisuals
{
    /// <summary>Owns temporary scene changes for one synchronous capture batch.</summary>
    internal sealed class SceneLayerVisualCaptureState
    {
        private readonly List<Renderer> _hiddenRenderers = new();
        private readonly List<Canvas> _hiddenCanvases = new();
        private readonly List<Light2D> _suppressedVolumetricLights = new();
#if UNITY_EDITOR
        private UnityEditor.SceneView _sceneView;
        private bool _restoreGizmos;
#endif

        internal void HideSceneForCapture(Camera viewer)
        {
#if UNITY_EDITOR
            // URP uses the current Scene View's gizmo flag even for a nested Game camera.
            _sceneView = UnityEditor.SceneView.currentDrawingSceneView;
            _restoreGizmos = _sceneView != null && _sceneView.drawGizmos;
            if (_restoreGizmos) _sceneView.drawGizmos = false;
#endif
            HideCanvases();
            SuppressVolumetricLights();
            HideRenderers(viewer.cullingMask);
        }

        internal void Restore()
        {
            // Only inputs changed by this batch are tracked; pre-disabled states stay untouched.
            foreach (var renderer in _hiddenRenderers)
            {
                if (renderer != null) renderer.forceRenderingOff = false;
            }
            _hiddenRenderers.Clear();
            foreach (var canvas in _hiddenCanvases)
            {
                if (canvas != null) canvas.enabled = true;
            }
            _hiddenCanvases.Clear();
            foreach (var light in _suppressedVolumetricLights)
            {
                if (light != null) light.volumetricEnabled = true;
            }
            _suppressedVolumetricLights.Clear();
#if UNITY_EDITOR
            if (_restoreGizmos && _sceneView != null) _sceneView.drawGizmos = true;
            _sceneView = null;
            _restoreGizmos = false;
#endif
        }

        private void HideCanvases()
        {
            // CanvasRenderer is not a Renderer. UI remains in the viewer's own pass.
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.enabled) continue;
                _hiddenCanvases.Add(canvas);
                canvas.enabled = false;
            }
        }

        private void SuppressVolumetricLights()
        {
            // Keep illumination of source sprites; standalone beams are drawn by the viewer.
            foreach (var light in Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None))
            {
                if (!light.isActiveAndEnabled || !light.volumetricEnabled) continue;
                _suppressedVolumetricLights.Add(light);
                light.volumetricEnabled = false;
            }
        }

        private void HideRenderers(int cameraMask)
        {
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                // Masks are clipping dependencies. Already-hidden and excluded inputs stay untouched.
                if (renderer is SpriteMask || !renderer.enabled || renderer.forceRenderingOff
                    || (cameraMask & (1 << renderer.gameObject.layer)) == 0) continue;
                _hiddenRenderers.Add(renderer);
                renderer.forceRenderingOff = true;
            }
        }
    }
}
