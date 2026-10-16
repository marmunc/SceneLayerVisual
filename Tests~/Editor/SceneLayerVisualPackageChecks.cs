using System;
using System.IO;
using System.Linq;
using SceneLayerVisuals;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class SceneLayerVisualPackageChecks
{
    private static UniversalRenderPipelineAsset _pipeline;

    public static void Run()
    {
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.CreateFolder("Assets", "Demo");
            var forward = ScriptableObject.CreateInstance<UniversalRendererData>();
            var renderer2D = ScriptableObject.CreateInstance<Renderer2DData>();
            AssetDatabase.CreateAsset(forward, "Assets/Demo/Forward.asset");
            AssetDatabase.CreateAsset(renderer2D, "Assets/Demo/Renderer2D.asset");
            _pipeline = UniversalRenderPipelineAsset.Create(forward);
            var serializedPipeline = new SerializedObject(_pipeline);
            var renderers = serializedPipeline.FindProperty("m_RendererDataList");
            renderers.arraySize = 2;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = forward;
            renderers.GetArrayElementAtIndex(1).objectReferenceValue = renderer2D;
            serializedPipeline.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(_pipeline, "Assets/Demo/Pipeline.asset");
            GraphicsSettings.defaultRenderPipeline = _pipeline;
            QualitySettings.renderPipeline = _pipeline;
            AssetDatabase.SaveAssets();
            EditorApplication.delayCall += CheckRendering;
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void CheckRendering()
    {
        try
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SceneLayerVisual).Assembly);
            Require(package != null && package.name == "com.marmunc.scene-layer-visual", "UPM package discovery");
            Require(typeof(SceneLayerVisual).Assembly.GetName().Name == "SceneLayerVisual.Runtime", "Runtime assembly");
            var shader = Resources.Load<Shader>("SceneLayerVisual");
            Require(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Package shader");
            Debug.Log("SLV_BASELINE: " + SceneLayerVisualBaseline.Run());
            CheckMasksAndLifecycle();
            CreateDemo();
            CheckImportedSample();
            AssetDatabase.SaveAssets();
            // Demo assets are generated through Unity APIs; the package receives them after validation.
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Demo/BasicDemo.unity" },
                locationPathName = "Build/SceneLayerVisualDemo.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            Require(report.summary.result == BuildResult.Succeeded, "Standalone Windows build: " + report.summary.result);
            File.WriteAllText("results.json", JsonUtility.ToJson(new CheckResults
            {
                Unity = Application.unityVersion,
                Package = package.version,
                Baseline = true, MasksAndLifecycle = true, StandaloneBuild = true
            }, true));
            SceneLayerVisualPlayModeCheck.Begin();
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void CheckImportedSample()
    {
        var sample = UnityEditor.PackageManager.UI.Sample.FindByPackage("com.marmunc.scene-layer-visual", "0.1.0").First();
        Require(sample.Import(UnityEditor.PackageManager.UI.Sample.ImportOptions.OverridePreviousImports), "Sample import");
        string scenePath = Path.GetRelativePath(Path.GetDirectoryName(Application.dataPath),
            Path.Combine(sample.importPath, "BasicDemo.unity")).Replace('\\', '/');
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            var roots = scene.GetRootGameObjects();
            Require(roots.Sum(root => root.GetComponentsInChildren<SceneLayerVisual>(true).Length) == 3, "Imported sample component references");
            Require(roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .All(item => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject) == 0), "Imported sample missing scripts");
            foreach (var layer in roots.SelectMany(root => root.GetComponentsInChildren<SceneLayerVisual>(true)))
            {
                var serialized = new SerializedObject(layer);
                serialized.FindProperty("m_EditorClassIdentifier").stringValue =
                    typeof(SceneLayerVisual).Assembly.GetName().Name + "::" + typeof(SceneLayerVisual).FullName;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Require(EditorSceneManager.SaveScene(scene), "Sample reserialization");
            File.WriteAllText("sample-import-path.txt", sample.importPath);
            Debug.Log("SLV_SAMPLE_PASS: imported sample retains three components and has no missing scripts");
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    private static void CheckMasksAndLifecycle()
    {
        var root = new GameObject("Mask verification");
        var texture = new Texture2D(16, 16);
        texture.SetPixels(Enumerable.Repeat(Color.white, 256).ToArray());
        texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(.5f, .5f), 16);
        var material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
        var output = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGBHalf);
        output.Create();
        var reader = new Texture2D(128, 128, TextureFormat.RGBAFloat, false, true);
        var previousActive = RenderTexture.active;
        try
        {
            var camera = new GameObject("Mask camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.transform.position = new Vector3(0, 0, -10);
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.targetTexture = output;
            camera.GetUniversalAdditionalCameraData().SetRenderer(1);
            var group = new GameObject("Masked group");
            group.transform.SetParent(root.transform);
            var source = group.AddComponent<SpriteRenderer>();
            group.layer = 30;
            source.sprite = sprite;
            source.sharedMaterial = material;
            var effects = group.AddComponent<SceneLayerVisual>();
            effects.resolution = SceneLayerVisual.Resolution.Full;
            effects.tint = Color.red;
            effects.tintStrength = 1;
            var mask = new GameObject("External mask").AddComponent<SpriteMask>();
            mask.transform.SetParent(root.transform);
            mask.gameObject.layer = 30;
            mask.sprite = sprite;
            mask.transform.localScale = new Vector3(.5f, 1, 1);
            mask.transform.localPosition = new Vector3(-.25f, 0, 0);
            foreach (bool internalMask in new[] { false, true })
            {
                mask.transform.SetParent(internalMask ? group.transform : root.transform, true);
                if (internalMask) group.AddComponent<SortingGroup>();
                foreach (var interaction in new[] { SpriteMaskInteraction.VisibleInsideMask, SpriteMaskInteraction.VisibleOutsideMask })
                {
                    source.maskInteraction = interaction;
                    var baseline = Render(false);
                    var processed = Render(true);
                    Require(Mathf.Abs(baseline.x - processed.x) < .05f && Mathf.Abs(baseline.y - processed.y) < .05f,
                        "SpriteMask clipping: " + interaction + ", internal=" + internalMask);
                    Require(Mathf.Abs(baseline.x - baseline.y) > .8f, "Mask actually clips the source");
                }
            }
            effects.enabled = false;
            Require(!source.forceRenderingOff, "Disable restores visibility");
            Require(!UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Any(item => item.name.StartsWith("SceneLayerVisual:") && item.enabled), "Disable removes visible proxies");
            effects.enabled = true;
            source.forceRenderingOff = true;
            Render(true);
            Require(source.forceRenderingOff, "Pre-existing hidden state is preserved");
            source.forceRenderingOff = false;
            // The first source is excluded; a visible child's layer must be used by the composite.
            group.layer = 31;
            var visibleChild = new GameObject("Visible child").AddComponent<SpriteRenderer>();
            visibleChild.transform.SetParent(group.transform);
            visibleChild.gameObject.layer = 30;
            visibleChild.sprite = sprite;
            visibleChild.sharedMaterial = material;
            Render(true);
            Require(reader.GetPixel(64, 64).r > .9f, "Composite follows a camera-visible source layer");
            UnityEngine.Object.DestroyImmediate(visibleChild.gameObject);
            group.layer = 30;
            camera.cullingMask = 1 << 29;
            var excluded = Render(true);
            Require(excluded.x < .01f && excluded.y < .01f, "Camera culling bypasses the group");
            camera.cullingMask = 1 << 30;
            effects.enabled = false;
            var captureField = typeof(SceneLayerVisual).GetField("_capture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Require((UnityEngine.Object)captureField.GetValue(effects) == null, "Disable releases capture texture");
            effects.enabled = true;
            var unrelated = new GameObject("Unrelated translucent sprite").AddComponent<SpriteRenderer>();
            unrelated.transform.SetParent(root.transform);
            unrelated.transform.position = new Vector3(.75f, 0, 0);
            unrelated.gameObject.layer = 30;
            unrelated.sprite = sprite;
            unrelated.sharedMaterial = material;
            unrelated.color = new Color(0, 0, 1, .5f);
            unrelated.sortingOrder = 100;
            Render(false);
            Color unrelatedBaseline = reader.GetPixel(112, 64);
            Render(true);
            Color unrelatedProcessed = reader.GetPixel(112, 64);
            Require(Mathf.Abs(unrelatedBaseline.r - unrelatedProcessed.r) < .02f
                && Mathf.Abs(unrelatedBaseline.b - unrelatedProcessed.b) < .02f,
                "Unrelated translucent source is not captured or duplicated");
            unrelated.forceRenderingOff = true;
            Render(true);
            Require(unrelated.forceRenderingOff, "Unrelated pre-hidden source remains hidden");
            UnityEngine.Object.DestroyImmediate(unrelated.gameObject);
            effects.enabled = false;
            var neutral = new GameObject("Neutral group").AddComponent<SceneLayerVisual>();
            neutral.transform.SetParent(root.transform);
            neutral.gameObject.layer = 30;
            var neutralSource = neutral.gameObject.AddComponent<SpriteRenderer>();
            neutralSource.sprite = sprite;
            neutralSource.sharedMaterial = material;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = output });
            Require((UnityEngine.Object)captureField.GetValue(neutral) == null, "Neutral group allocates no capture buffer");
            neutral.blurRadius = 12;
            neutral.transform.position = new Vector3(1000, 0, 0);
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = output });
            Require((UnityEngine.Object)captureField.GetValue(neutral) == null, "Offscreen group allocates no capture buffer");
            Require(new SerializedObject(neutral).FindProperty("enableEffects") == null, "Redundant serialized toggle removed");
            Debug.Log("SLV_OPTIMIZATION_PASS: capture isolation, hidden outside sources, neutral/offscreen bypass and removed toggle");
            Debug.Log("SLV_CAMERA_PASS: mixed source layers, camera culling, capture resource release");
            Debug.Log("SLV_MASKS_PASS: internal/external masks, inside/outside interaction, disable and visibility restoration");

            Vector2 Render(bool enabled)
            {
                effects.enabled = enabled;
                bool wasHidden = source.forceRenderingOff;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = output });
                RenderTexture.active = output;
                reader.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                reader.Apply();
                Require(source.forceRenderingOff == wasHidden, "Visibility restored after masked capture");
                return new Vector2(reader.GetPixel(48, 64).r, reader.GetPixel(80, 64).r);
            }
        }
        finally
        {
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(material);
            output.Release();
            UnityEngine.Object.DestroyImmediate(output);
            UnityEngine.Object.DestroyImmediate(reader);
        }
    }

    private static void CreateDemo()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var pixels = new Color[4096];
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                pixels[y * 64 + x] = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) < 30 ? Color.white : Color.clear;
        texture.SetPixels(pixels);
        texture.Apply();
        File.WriteAllBytes("Assets/Demo/Disc.png", texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset("Assets/Demo/Disc.png");
        var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Demo/Disc.png");
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 64;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Demo/Disc.png");
        Require(sprite != null, "Demo sprite import");
        var material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default"));
        AssetDatabase.CreateAsset(material, "Assets/Demo/SpriteLit.mat");
        var camera = new GameObject("Main Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.orthographicSize = 3.5f;
        camera.transform.position = new Vector3(0, 0, -10);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.025f, .035f, .07f);
        camera.GetUniversalAdditionalCameraData().SetRenderer(1);
        var light = new GameObject("Global 2D Light").AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Global;
        light.intensity = 1;
        for (int column = 0; column < 3; column++)
        {
            var group = new GameObject(new[] { "01 Original", "02 Color grading", "03 Blur and SpriteMask" }[column]);
            group.transform.position = new Vector3((column - 1) * 3, 0, 0);
            group.AddComponent<SortingGroup>().sortingOrder = column;
            var effect = group.AddComponent<SceneLayerVisual>();
            if (column == 1) { effect.exposure = .3f; effect.tint = new Color(.4f, .7f, 1); effect.tintStrength = .4f; effect.saturation = .5f; }
            if (column == 2) effect.blurRadius = 12;
            for (int i = 0; i < 5; i++)
            {
                var renderer = new GameObject("Scenery " + (i + 1)).AddComponent<SpriteRenderer>();
                renderer.transform.SetParent(group.transform);
                renderer.transform.localPosition = new Vector3((i % 2 == 0 ? -.35f : .35f), (i - 2) * .55f, 0);
                renderer.transform.localScale = Vector3.one * 1.3f;
                renderer.sprite = sprite;
                renderer.sharedMaterial = material;
                renderer.color = Color.HSVToRGB(.04f + i * .12f, .65f, .9f);
                renderer.sortingOrder = i;
                if (column == 2) renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            }
            if (column == 2)
            {
                var mask = new GameObject("SpriteMask inside sorting group").AddComponent<SpriteMask>();
                mask.transform.SetParent(group.transform);
                mask.transform.localPosition = Vector3.zero;
                mask.transform.localScale = new Vector3(2, 3, 1);
                mask.sprite = sprite;
            }
        }
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/Demo/BasicDemo.unity");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }

    private static void Fail(Exception exception)
    {
        Debug.LogException(exception);
        EditorApplication.Exit(1);
    }

    [Serializable]
    private sealed class CheckResults
    {
        public string Unity;
        public string Package;
        public bool Baseline;
        public bool MasksAndLifecycle;
        public bool StandaloneBuild;
    }
}
