using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SceneLayerVisuals
{
    /// <summary>Processes a complete orthographic scenery layer without changing its source assets.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Scene Layer Visual/Scene Layer Visual")]
    public sealed class SceneLayerVisual : MonoBehaviour
    {
        public const float MaxBlurRadius = 256f;
        private const float WideBlurThreshold = 24f;
        private const int MinimumBlurTaps = 6;
        private const int MaxCaptureDivisor = 8;
        private const int GradePass = 1;
        private const int BlurPass = 2;
        private const int MaxBlurTaps = 32;
        private static readonly int _exposureId = Shader.PropertyToID("_Exposure");
        private static readonly int _tintId = Shader.PropertyToID("_Tint");
        private static readonly int _tintStrengthId = Shader.PropertyToID("_TintStrength");
        private static readonly int _saturationId = Shader.PropertyToID("_Saturation");
        private static readonly int _contrastId = Shader.PropertyToID("_Contrast");
        private static readonly int _blurTapCountId = Shader.PropertyToID("_BlurTapCount");
        private static readonly int _blurStepId = Shader.PropertyToID("_BlurStep");
        private static readonly int _mainTextureId = Shader.PropertyToID("_MainTex");
        private static readonly int _blurWeightsId = Shader.PropertyToID("_BlurWeights");

        [Range(0, MaxBlurRadius), Tooltip("Blur radius in viewing camera pixels. Above 24, capture resolution is reduced automatically for smooth broad blur.")]
        public float blurRadius;
        [Range(-3, 3)] public float exposure;
        public Color tint = Color.white;
        [Range(0, 1), Tooltip("Mix toward Tint; white lightens even black pixels.")]
        public float tintStrength;
        [Range(0, 2)] public float saturation = 1;
        [Range(0, 2)] public float contrast = 1;
        public enum Resolution { Full = 1, Half = 2, Quarter = 4 }
        public Resolution resolution = Resolution.Half;

        internal bool HasEffects => isActiveAndEnabled &&
            (blurRadius > 0 || exposure != 0 || tintStrength > 0 || saturation != 1 || contrast != 1);
        private bool HasColorGrading => exposure != 0 || tintStrength > 0 || saturation != 1 || contrast != 1;

        private readonly List<Renderer> _sources = new();
        private MeshRenderer _proxy;
        private Material _material;
        private Mesh _mesh;
        private RenderTexture _capture;
        private RenderTexture _result;
        private int _captureDivisor = 1;
        private bool _hasReportedShaderError;
        private readonly float[] _blurWeights = new float[MaxBlurTaps + 1];
        private readonly UniversalRenderPipeline.SingleCameraRequest _captureRequest = new();
        private int _blurTapCount;

        internal IReadOnlyList<Renderer> Sources => _sources;

        private void OnEnable() => SceneLayerVisualRendering.Register(this);
        private void OnDisable()
        {
            SceneLayerVisualRendering.Unregister(this);
            Release();
        }
        private void OnDestroy() => Release();
        private void OnValidate()
        {
            blurRadius = Mathf.Clamp(blurRadius, 0, MaxBlurRadius);
            if (!HasEffects && _proxy != null) _proxy.enabled = false;
#if UNITY_EDITOR
            UnityEditor.SceneView.RepaintAll();
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        internal List<Renderer> CollectSources()
        {
            _sources.Clear();
            GetComponentsInChildren(false, _sources);
            // Composite helpers are unparented and cannot be included in this hierarchy.
            for (int i = _sources.Count - 1; i >= 0; i--)
            {
                var renderer = _sources[i];
                // SpriteMask derives from Renderer, but is a clipping dependency, not layer artwork.
                if (renderer == null || renderer is SpriteMask || !renderer.enabled || renderer.forceRenderingOff
                    || renderer.GetComponentInParent<SceneLayerVisual>() != this)
                    _sources.RemoveAt(i);
            }
            return _sources;
        }

        internal bool IsVisible(UnityEngine.Camera camera, Plane[] planes)
        {
            float margin = blurRadius * 4 * camera.orthographicSize / Mathf.Max(1, camera.pixelHeight);
            foreach (var source in _sources)
            {
                if (source.forceRenderingOff || (camera.cullingMask & (1 << source.gameObject.layer)) == 0) continue;
                var bounds = source.bounds;
                bounds.Expand(margin);
                if (GeometryUtility.TestPlanesAABB(planes, bounds)) return true;
            }
            return false;
        }

        internal void HideProxy() { if (_proxy != null) _proxy.enabled = false; }

        internal bool Prepare(UnityEngine.Camera viewer, UnityEngine.Camera captureCamera, int rendererIndex)
        {
            if (_sources.Count == 0) return false;
            EnsureResources();
            if (_material == null) return false;
            int divisor = resolution == Resolution.Full ? 1 : resolution == Resolution.Quarter ? 4 : 2;
            // Limit sample spacing for broad blur without losing small scenery in the capture.
            if (blurRadius > WideBlurThreshold)
                divisor = Mathf.Max(divisor, Mathf.Min(MaxCaptureDivisor, Mathf.CeilToInt(blurRadius / MinimumBlurTaps)));
            _captureDivisor = divisor;
            int viewWidth = Mathf.Max(1, viewer.pixelWidth);
            int viewHeight = Mathf.Max(1, viewer.pixelHeight);
            // Margin prevents the blur kernel from sampling a clipped viewport edge.
            int margin = Mathf.CeilToInt(blurRadius * 2 + 2);
            int width = Mathf.Max(1, Mathf.CeilToInt((viewWidth + margin * 2f) / divisor));
            int height = Mathf.Max(1, Mathf.CeilToInt((viewHeight + margin * 2f) / divisor));
            EnsureTexture(ref _capture, width, height, 24);
            EnsureTexture(ref _result, width, height, 0);

            ConfigureCaptureCamera(viewer, captureCamera, rendererIndex, width, height, viewHeight, margin);
            ConfigureProxy(viewer, captureCamera);
            return true;
        }

        private void ConfigureCaptureCamera(UnityEngine.Camera viewer, UnityEngine.Camera captureCamera,
            int rendererIndex, int width, int height, int viewHeight, int margin)
        {
            captureCamera.CopyFrom(viewer);
            // Use Game semantics to avoid Scene View's special UI and camera-volume behavior.
            captureCamera.cameraType = CameraType.Game;
            captureCamera.enabled = false;
            captureCamera.transform.SetPositionAndRotation(viewer.transform.position, viewer.transform.rotation);
            captureCamera.rect = new Rect(0, 0, 1, 1);
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = Color.clear;
            captureCamera.allowMSAA = false;
            captureCamera.allowHDR = true;
            captureCamera.aspect = (float)width / height;
            captureCamera.orthographicSize = viewer.orthographicSize * (viewHeight + margin * 2f) / viewHeight;
            captureCamera.targetTexture = _capture;
            var captureData = captureCamera.GetUniversalAdditionalCameraData();
            captureData.SetRenderer(rendererIndex);
            captureData.renderType = CameraRenderType.Base;
            captureData.renderPostProcessing = false;
            captureData.volumeLayerMask = 0;
            captureData.antialiasing = AntialiasingMode.None;
            captureData.allowXRRendering = false;
            captureData.allowHDROutput = false;
            captureData.renderShadows = viewer.cameraType == CameraType.SceneView
                || !viewer.TryGetComponent<UniversalAdditionalCameraData>(out var viewerData) || viewerData.renderShadows;
        }

        private void ConfigureProxy(UnityEngine.Camera viewer, UnityEngine.Camera captureCamera)
        {
            var group = GetComponent<SortingGroup>();
            _proxy.sortingLayerID = group != null ? group.sortingLayerID : _sources[0].sortingLayerID;
            _proxy.sortingOrder = group != null ? group.sortingOrder : _sources[0].sortingOrder;
            // The first child may be excluded by this camera while another child is visible.
            foreach (var source in _sources)
            {
                if ((viewer.cullingMask & (1 << source.gameObject.layer)) == 0) continue;
                _proxy.gameObject.layer = source.gameObject.layer;
                break;
            }
            float distance = Mathf.Clamp(Vector3.Dot(transform.position - viewer.transform.position,
                viewer.transform.forward), viewer.nearClipPlane + 0.01f, viewer.farClipPlane - 0.01f);
            _proxy.transform.SetPositionAndRotation(viewer.transform.position + viewer.transform.forward * distance,
                viewer.transform.rotation);
            // The helper is unparented: parent scale must not distort its screen-aligned quad.
            _proxy.transform.localScale = new Vector3(captureCamera.orthographicSize * 2 * captureCamera.aspect,
                captureCamera.orthographicSize * 2, 1);
        }

        internal void CaptureAndProcess(UnityEngine.Camera captureCamera)
        {
            var previousRenderTarget = RenderTexture.active;
            try
            {
                _captureRequest.destination = _capture;
                RenderPipeline.SubmitRenderRequest(captureCamera, _captureRequest);
                RenderTexture processed = _capture;
                if (HasColorGrading)
                {
                    _material.SetFloat(_exposureId, Mathf.Pow(2, exposure));
                    _material.SetColor(_tintId, tint.linear);
                    _material.SetFloat(_tintStrengthId, tintStrength);
                    _material.SetFloat(_saturationId, saturation);
                    _material.SetFloat(_contrastId, contrast);
                    Graphics.Blit(_capture, _result, _material, GradePass);
                    processed = _result;
                }
                if (blurRadius > 0)
                {
                    float radius = blurRadius / _captureDivisor;
                    // Keep small scenery from disappearing into subpixel captures.
                    // Once downsampling reaches its limit, increase kernel density instead.
                    int tapCount = blurRadius > WideBlurThreshold
                        ? Mathf.Clamp(Mathf.CeilToInt(radius), MinimumBlurTaps, MaxBlurTaps) : MinimumBlurTaps;
                    UpdateBlurWeights(tapCount);
                    // Ping-pong the two owned textures; no third full-size temporary is needed.
                    RenderTexture intermediate = processed == _capture ? _result : _capture;
                    _material.SetVector(_blurStepId, new Vector4(radius / _capture.width, 0, 0, 0));
                    Graphics.Blit(processed, intermediate, _material, BlurPass);
                    _material.SetVector(_blurStepId, new Vector4(0, radius / _capture.height, 0, 0));
                    Graphics.Blit(intermediate, processed, _material, BlurPass);
                }
                _material.SetTexture(_mainTextureId, processed);
            }
            finally
            {
                RenderTexture.active = previousRenderTarget;
            }
        }

        private void UpdateBlurWeights(int tapCount)
        {
            if (_blurTapCount == tapCount) return;
            float total = 0;
            for (int tap = 0; tap <= tapCount; tap++)
            {
                float offset = (float)tap / tapCount;
                float weight = Mathf.Exp(-4.5f * offset * offset);
                _blurWeights[tap] = weight;
                total += tap == 0 ? weight : weight * 2;
            }
            for (int tap = 0; tap <= tapCount; tap++) _blurWeights[tap] /= total;
            _material.SetFloat(_blurTapCountId, tapCount);
            _material.SetFloatArray(_blurWeightsId, _blurWeights);
            _blurTapCount = tapCount;
        }

        internal void ShowProxy() => _proxy.enabled = true;

        private void EnsureResources()
        {
            if (_proxy != null) return;
            var shader = Resources.Load<Shader>("SceneLayerVisual");
            if (shader == null || !shader.isSupported)
            {
                if (!_hasReportedShaderError)
                {
                    Debug.LogError("Scene Layer Visual shader is missing or unsupported. Original rendering is preserved.", this);
                    _hasReportedShaderError = true;
                }
                return;
            }
            _hasReportedShaderError = false;
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _mesh = new Mesh { name = "Scene layer quad", hideFlags = HideFlags.HideAndDontSave };
            _mesh.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(-.5f, .5f),
                new Vector3(.5f, .5f), new Vector3(.5f, -.5f) };
            _mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            _mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _mesh.RecalculateBounds();
            var helper = new GameObject("SceneLayerVisual: " + name) { hideFlags = HideFlags.HideAndDontSave };
            if (gameObject.scene.IsValid())
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(helper, gameObject.scene);
            helper.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _proxy = helper.AddComponent<MeshRenderer>();
            _proxy.sharedMaterial = _material;
            _proxy.shadowCastingMode = ShadowCastingMode.Off;
            _proxy.receiveShadows = false;
            _proxy.enabled = false;
        }

        private static void EnsureTexture(ref RenderTexture texture, int width, int height, int depth)
        {
            if (texture != null && texture.width == width && texture.height == height) return;
            DestroyResource(texture);
            texture = new RenderTexture(width, height, depth, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = "Scene layer visual", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            texture.Create();
        }

        internal static void DestroyResource(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (obj is RenderTexture rt) rt.Release();
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }

        internal void Release()
        {
            if (SceneLayerVisualRendering.TryDeferRelease(this)) return;
            if (_proxy != null) { _proxy.enabled = false; DestroyResource(_proxy.gameObject); }
            DestroyResource(_material); DestroyResource(_mesh);
            DestroyResource(_capture); DestroyResource(_result);
            _proxy = null; _material = null; _mesh = null; _capture = null; _result = null;
            _captureRequest.destination = null;
            _blurTapCount = 0;
        }
    }
}
