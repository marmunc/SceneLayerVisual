using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SceneLayerVisuals;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public static class SceneLayerVisualCaptureIsolationChecks
{
    public static void Run()
    {
        var contextField = typeof(SceneView).GetField("s_CurrentDrawingSceneView", BindingFlags.Static | BindingFlags.NonPublic);
        if (contextField == null) throw new Exception("The tested Unity version has no Scene View drawing context.");
        var previousView = SceneView.currentDrawingSceneView;
        var view = ScriptableObject.CreateInstance<SceneView>();
        var root = new GameObject("Capture isolation fixture") { hideFlags = HideFlags.HideAndDontSave };
        var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        texture.SetPixels(Enumerable.Repeat(Color.white, 64).ToArray());
        texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), Vector2.one * .5f, 8);
        var material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
        var litMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default"));
        var output = new RenderTexture(512, 256, 24, RenderTextureFormat.ARGBHalf);
        var failures = new List<string>();
        var previousActive = RenderTexture.active;
        var canvases = new List<Canvas>();
        var volume = CreateChild(root, "External volumetric light", new Vector3(2, .8f)).AddComponent<Light2D>();
        volume.lightType = Light2D.LightType.Sprite;
        volume.lightCookieSprite = sprite;
        volume.color = Color.red;
        volume.intensity = 1;
        volume.volumetricEnabled = true;
        volume.volumeIntensity = 1;
        var artwork = CreateChild(root, "Layer", new Vector3(-2, 0));
        var source = artwork.AddComponent<SpriteRenderer>();
        source.sprite = sprite;
        source.sharedMaterial = material;
        artwork.AddComponent<SortingGroup>();
        var litArtwork = CreateChild(artwork, "Lit source", new Vector3(-2, -1));
        var lit = litArtwork.AddComponent<SpriteRenderer>();
        lit.sprite = sprite;
        lit.sharedMaterial = litMaterial;
        var sourceLight = CreateChild(root, "Source illumination", litArtwork.transform.position).AddComponent<Light2D>();
        sourceLight.lightType = Light2D.LightType.Point;
        sourceLight.intensity = 1;
        sourceLight.pointLightOuterRadius = 2;
        sourceLight.pointLightInnerRadius = 1;
        sourceLight.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
        var effect = artwork.AddComponent<SceneLayerVisual>();
        effect.blurRadius = 12;
        var camera = CreateChild(root, "Isolation viewer", new Vector3(0, 0, -10)).AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 2;
        camera.cullingMask = 1 << 30;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.targetTexture = output;
        var cameraData = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        cameraData.renderShadows = false;
        cameraData.renderPostProcessing = false;
        var worldCanvas = CreateCanvas(root, "World UI", RenderMode.WorldSpace, new Vector3(2, -.8f), camera);
        canvases.Add(worldCanvas);
        var cameraCanvas = CreateCanvas(root, "Camera UI", RenderMode.ScreenSpaceCamera, Vector3.zero, camera);
        var cameraPanel = cameraCanvas.GetComponentInChildren<Image>().rectTransform;
        canvases.Add(cameraCanvas);
        var disabledCanvas = CreateCanvas(root, "Disabled UI", RenderMode.WorldSpace, new Vector3(3, -1), camera);
        disabledCanvas.enabled = false;
        var disabledVolume = CreateChild(root, "Disabled volumetrics", new Vector3(3, 1)).AddComponent<Light2D>();
        disabledVolume.lightType = Light2D.LightType.Sprite;
        disabledVolume.lightCookieSprite = sprite;
        disabledVolume.volumetricEnabled = false;
        int captures = 0;
        bool sceneViewer = false;
        int rendererIndex = 0;
        Color litReference = Color.clear;
        Color volumeReference = Color.clear;
        bool hasLitReference = false;
        Action<ScriptableRenderContext, Camera> observe = (context, capture) =>
        {
            if (capture.name != "SceneLayerVisual capture") return;
            captures++;
            if (capture.cameraType != CameraType.Game) failures.Add("Scene View camera type leaked into capture");
            var data = capture.GetUniversalAdditionalCameraData();
            if (data.renderPostProcessing || data.volumeLayerMask.value != 0) failures.Add("Post-processing is enabled in capture");
            if (data.renderShadows != sceneViewer) failures.Add("Capture shadow setting does not match the viewer");
            if (canvases.Any(canvas => canvas.enabled)) failures.Add("Unrelated Canvas enabled in capture");
            if (volume.volumetricEnabled) failures.Add("Unrelated Light2D volumetrics enabled in capture");
            if (!volume.enabled || volume.intensity != 1) failures.Add("Normal Light2D illumination was disabled");
            if (disabledCanvas.enabled || disabledVolume.volumetricEnabled) failures.Add("A pre-disabled input was enabled");
            Color uiPixel = ReadPixel(capture.targetTexture, capture, worldCanvas.transform.position);
            Color cameraUiPixel = ReadPixel(capture.targetTexture, capture, cameraPanel.position);
            Color volumePixel = ReadPixel(capture.targetTexture, capture, volume.transform.position);
            if (uiPixel.a > .01f) failures.Add("UI geometry leaked into capture: " + uiPixel);
            if (cameraUiPixel.a > .01f) failures.Add("Camera-space UI leaked into capture: " + cameraUiPixel);
            if (volumePixel.a > .01f || volumePixel.r > .01f) failures.Add("Volumetric geometry leaked into capture: " + volumePixel);
            if (hasLitReference && rendererIndex == 1)
            {
                Color capturedLit = ReadPixel(capture.targetTexture, capture, litArtwork.transform.position);
                if (Mathf.Abs(capturedLit.r - litReference.r) > .03f || Mathf.Abs(capturedLit.g - litReference.g) > .03f)
                    failures.Add("Source sprite lighting changed: " + capturedLit + " versus " + litReference);
            }
            Debug.Log("SLV_ISOLATION_MEASURE: renderer=" + rendererIndex + " scene=" + sceneViewer + " UI=" + uiPixel + " volume=" + volumePixel);
        };
        try
        {
            output.Create();
            Canvas.ForceUpdateCanvases();
            RenderPipelineManager.endCameraRendering += observe;
            for (rendererIndex = 0; rendererIndex < 2; rendererIndex++)
            {
                cameraData.SetRenderer(rendererIndex);
                foreach (bool scene in new[] { false, true })
                {
                    sceneViewer = scene;
                    camera.cameraType = scene ? CameraType.SceneView : CameraType.Game;
                    contextField.SetValue(null, scene ? view : null);
                    hasLitReference = false;
                    if (!scene)
                    {
                        effect.enabled = false;
                        Render(camera, output);
                        litReference = ReadPixel(output, camera, litArtwork.transform.position);
                        Debug.Log("SLV_LIT_REFERENCE: renderer=" + rendererIndex + " color=" + litReference);
                        volumeReference = ReadPixel(output, camera, volume.transform.position);
                        if (rendererIndex == 1 && litReference.r < .05f) failures.Add("Fixture's source illumination is missing");
                        hasLitReference = true;
                        effect.enabled = true;
                    }
                    int previousCaptures = captures;
                    Render(camera, output);
                    if (!scene)
                    {
                        Color visibleUI = ReadPixel(output, camera, worldCanvas.transform.position);
                        if (visibleUI.b < .5f) failures.Add("UI disappeared from the viewer after capture: " + visibleUI);
                        Color visibleCameraUI = ReadPixel(output, camera, cameraPanel.position);
                        if (visibleCameraUI.b < .5f) failures.Add("Camera-space UI disappeared from the viewer: " + visibleCameraUI);
                        if (rendererIndex == 1)
                        {
                            Color visibleVolume = ReadPixel(output, camera, volume.transform.position);
                            if (volumeReference.r < .05f || Mathf.Abs(visibleVolume.r - volumeReference.r) > .015f)
                                failures.Add("Standalone light volume was removed, duplicated or blurred in the viewer: " + visibleVolume + " versus " + volumeReference);
                        }
                    }
                    if (captures <= previousCaptures) failures.Add("Fixture did not capture a layer");
                    if (canvases.Any(canvas => !canvas.enabled) || !volume.volumetricEnabled || disabledCanvas.enabled || disabledVolume.volumetricEnabled)
                        failures.Add("Canvas or Light2D state was not restored");
                    if (scene && !view.drawGizmos) failures.Add("Scene View gizmos were left disabled");
                }
            }
            RenderPipelineManager.endCameraRendering -= observe;
            contextField.SetValue(null, null);
            camera.cameraType = CameraType.Game;
            cameraData.SetRenderer(0);
            CheckDisableDuringCapture(camera, output, effect, source, canvases, volume, failures);
            CheckDestroyDuringCapture(root, camera, output, source, failures);
            CheckFailureRecovery(camera, output, source, canvases, volume, view, contextField, failures);
            var postProbe = CreateChild(root, "Unrelated post-process reference", new Vector3(0, 1.5f)).AddComponent<SpriteRenderer>();
            postProbe.sprite = sprite;
            postProbe.sharedMaterial = material;
            postProbe.color = new Color(.25f, .25f, .25f, 1);
            effect.enabled = false;
            RenderViewer(camera, output);
            Color ungraded = ReadPixel(output, camera, postProbe.transform.position);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            try
            {
                profile.Add<ColorAdjustments>(true).postExposure.value = 1;
                var postOwner = CreateChild(root, "External post-processing", Vector3.zero);
                postOwner.layer = 0;
                var postVolume = postOwner.AddComponent<Volume>();
                postVolume.isGlobal = true;
                postVolume.priority = 10000;
                postVolume.sharedProfile = profile;
                cameraData.volumeLayerMask = 1;
                cameraData.renderPostProcessing = true;
                RenderViewer(camera, output);
                Color reference = ReadPixel(output, camera, postProbe.transform.position);
                effect.enabled = true;
                RenderViewer(camera, output);
                Color processed = ReadPixel(output, camera, postProbe.transform.position);
                Debug.Log("SLV_POST_REFERENCE: ungraded=" + ungraded + " reference=" + reference + " withCapture=" + processed);
                if (reference.r < ungraded.r * 1.5f || Mathf.Abs(processed.r - reference.r) > .015f)
                    failures.Add("The viewer's final post-processing was skipped, duplicated or changed by capture");
                postVolume.sharedProfile = null;
            }
            finally
            {
                foreach (var component in profile.components) UnityEngine.Object.DestroyImmediate(component);
                UnityEngine.Object.DestroyImmediate(profile);
            }
            if (failures.Count > 0) throw new Exception(string.Join("; ", failures.Distinct()));
            Debug.Log("SLV_ISOLATION_PASS: UI, camera type, post-processing, volumetric light, illumination and state restoration. Play Mode=" + Application.isPlaying);
        }
        finally
        {
            RenderPipelineManager.endCameraRendering -= observe;
            contextField.SetValue(null, previousView);
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(output);
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(litMaterial);
            UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(view);
        }
    }

    private static GameObject CreateChild(GameObject parent, string name, Vector3 position)
    {
        var child = new GameObject(name) { layer = 30 };
        child.transform.SetParent(parent.transform);
        child.transform.position = position;
        return child;
    }

    private static Canvas CreateCanvas(GameObject root, string name, RenderMode mode, Vector3 position, Camera camera)
    {
        var owner = new GameObject(name, typeof(RectTransform)) { layer = 30 };
        owner.transform.SetParent(root.transform);
        owner.transform.position = position;
        var canvas = owner.AddComponent<Canvas>();
        canvas.renderMode = mode;
        canvas.worldCamera = camera;
        canvas.planeDistance = 5;
        var rect = (RectTransform)owner.transform;
        rect.sizeDelta = Vector2.one * .6f;
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)) { layer = 30 };
        panel.transform.SetParent(owner.transform, false);
        var panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;
        panel.GetComponent<Image>().color = Color.blue;
        if (mode == RenderMode.ScreenSpaceCamera)
        {
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(.125f, .875f);
            panelRect.sizeDelta = new Vector2(32, 32);
        }
        return canvas;
    }

    private static void CheckDisableDuringCapture(Camera camera, RenderTexture output, SceneLayerVisual effect,
        SpriteRenderer source, List<Canvas> canvases, Light2D light, List<string> failures)
    {
        bool hasDisabled = false;
        bool shouldReenable = false;
        var errors = new List<string>();
        Application.LogCallback observeErrors = (message, trace, type) =>
        {
            if (type == LogType.Exception || type == LogType.Error) errors.Add(message);
        };
        Action<ScriptableRenderContext, Camera> disable = (context, capture) =>
        {
            if (capture.name != "SceneLayerVisual capture" || hasDisabled) return;
            hasDisabled = true;
            effect.enabled = false;
            if (shouldReenable) effect.enabled = true;
        };
        try
        {
            Application.logMessageReceived += observeErrors;
            RenderPipelineManager.endCameraRendering += disable;
            foreach (bool reenable in new[] { false, true })
            {
                shouldReenable = reenable;
                hasDisabled = false;
                errors.Clear();
                RenderViewer(camera, output);
                if (!hasDisabled) failures.Add("Disable-during-capture check did not exercise a capture");
                if (errors.Count > 0) failures.Add("Disabling during capture produced errors: " + string.Join("; ", errors));
                if (source.forceRenderingOff || canvases.Any(canvas => !canvas.enabled) || !light.volumetricEnabled)
                    failures.Add("Disabling during capture did not restore original rendering");
                effect.enabled = true;
            }
        }
        finally
        {
            RenderPipelineManager.endCameraRendering -= disable;
            Application.logMessageReceived -= observeErrors;
            effect.enabled = true;
        }
    }

    private static void CheckDestroyDuringCapture(GameObject root, Camera camera, RenderTexture output,
        SpriteRenderer source, List<string> failures)
    {
        var artwork = CreateChild(root, "Destroy-during-capture layer", new Vector3(0, -1.4f));
        var renderer = artwork.AddComponent<SpriteRenderer>();
        renderer.sprite = source.sprite;
        renderer.sharedMaterial = source.sharedMaterial;
        var effect = artwork.AddComponent<SceneLayerVisual>();
        effect.blurRadius = 1;
        bool hasDestroyed = false;
        var errors = new List<string>();
        Application.LogCallback observeErrors = (message, trace, type) =>
        {
            if (type == LogType.Exception || type == LogType.Error) errors.Add(message);
        };
        Action<ScriptableRenderContext, Camera> destroy = (context, capture) =>
        {
            if (capture.name != "SceneLayerVisual capture" || hasDestroyed) return;
            // Identify the group's capture by its artwork, without depending on registration order.
            if (ReadPixel(capture.targetTexture, capture, artwork.transform.position).a < .5f) return;
            hasDestroyed = true;
            UnityEngine.Object.DestroyImmediate(effect);
        };
        try
        {
            Application.logMessageReceived += observeErrors;
            RenderPipelineManager.endCameraRendering += destroy;
            RenderViewer(camera, output);
            if (!hasDestroyed || errors.Count > 0 || renderer.forceRenderingOff)
                failures.Add("Destroying during capture did not preserve rendering: " + string.Join("; ", errors));
            bool hasVisibleProxy = Resources.FindObjectsOfTypeAll<MeshRenderer>()
                .Any(proxy => proxy.name == "SceneLayerVisual: " + artwork.name && proxy.enabled);
            if (hasVisibleProxy) failures.Add("Destroyed component left its composite visible");
        }
        finally
        {
            RenderPipelineManager.endCameraRendering -= destroy;
            Application.logMessageReceived -= observeErrors;
            UnityEngine.Object.DestroyImmediate(artwork);
        }
    }

    private static void CheckFailureRecovery(Camera camera, RenderTexture output, SpriteRenderer source,
        List<Canvas> canvases, Light2D light, SceneView view, FieldInfo contextField, List<string> failures)
    {
        bool hasInjectedFailure = false;
        bool hasRestoredTarget = false;
        int expectedErrors = 0;
        var sentinel = new RenderTexture(8, 8, 0);
        var errors = new List<string>();
        Application.LogCallback observeErrors = (message, trace, type) =>
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (message.Contains(nameof(CaptureFailureForValidationException)))
            {
                expectedErrors++;
            }
            else errors.Add(message);
        };
        Action<ScriptableRenderContext, Camera> observeViewer = (context, current) =>
        {
            // Unity logs native render-request exceptions before returning to managed finally
            // blocks. Inspect restoration after the coordinator returns to the viewer callback.
            if (current == camera && hasInjectedFailure) hasRestoredTarget = RenderTexture.active != sentinel;
        };
        Action<ScriptableRenderContext, Camera> fail = (context, capture) =>
        {
            if (capture.name != "SceneLayerVisual capture" || hasInjectedFailure) return;
            hasInjectedFailure = true;
            RenderTexture.active = sentinel;
            throw new CaptureFailureForValidationException();
        };
        try
        {
            sentinel.Create();
            view.drawGizmos = true;
            contextField.SetValue(null, view);
            Application.logMessageReceived += observeErrors;
            RenderPipelineManager.beginCameraRendering += observeViewer;
            RenderPipelineManager.endCameraRendering += fail;
            RenderTexture.active = output;
            RenderViewer(camera, output);
            if (!hasInjectedFailure || expectedErrors != 1 || !hasRestoredTarget || errors.Count > 0)
                failures.Add("Capture failure recovery: injected=" + hasInjectedFailure + " expectedErrors=" + expectedErrors
                    + " targetRestored=" + hasRestoredTarget + " otherErrors=" + string.Join("; ", errors));
            if (source.forceRenderingOff || canvases.Any(canvas => !canvas.enabled) || !light.volumetricEnabled || !view.drawGizmos)
                failures.Add("Capture failure left scene rendering or gizmos changed");
            RenderPipelineManager.endCameraRendering -= fail;
            int recoveryCaptures = 0;
            Action<ScriptableRenderContext, Camera> observeRecovery = (context, capture) =>
            {
                if (capture.name == "SceneLayerVisual capture") recoveryCaptures++;
            };
            RenderPipelineManager.endCameraRendering += observeRecovery;
            try { RenderViewer(camera, output); }
            finally { RenderPipelineManager.endCameraRendering -= observeRecovery; }
            if (recoveryCaptures == 0) failures.Add("Capture did not recover after a render failure");
        }
        finally
        {
            RenderPipelineManager.endCameraRendering -= fail;
            Application.logMessageReceived -= observeErrors;
            RenderPipelineManager.beginCameraRendering -= observeViewer;
            contextField.SetValue(null, null);
            RenderTexture.active = output;
            UnityEngine.Object.DestroyImmediate(sentinel);
        }
    }

    private sealed class CaptureFailureForValidationException : Exception
    {
        public CaptureFailureForValidationException() : base("Intentional failure for capture-recovery validation.") { }
    }

    private static void Render(Camera camera, RenderTexture target)
    {
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
    }

    private static void RenderViewer(Camera camera, RenderTexture target)
    {
        // StandardRequest includes the viewer's Volume update; SingleCameraRequest deliberately skips it.
        RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
    }

    private static Color ReadPixel(RenderTexture texture, Camera camera, Vector3 position)
    {
        var previous = RenderTexture.active;
        var reader = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        try
        {
            Vector3 point = camera.WorldToViewportPoint(position);
            RenderTexture.active = texture;
            reader.ReadPixels(new Rect(Mathf.Clamp((int)(point.x * texture.width), 0, texture.width - 1), Mathf.Clamp((int)(point.y * texture.height), 0, texture.height - 1), 1, 1), 0, 0);
            reader.Apply();
            return reader.GetPixel(0, 0);
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(reader); }
    }
}
