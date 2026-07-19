using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal static class StartProfileAssetBuilder
{
    private const string PrefabPath = "Assets/Prefabs/UI/PersonalInfoSelection.prefab";
    private const string StartScenePath = "Assets/Scenes/Start Scene.unity";
    private const string MainScenePath = "Assets/Scenes/Main Scene.unity";
    private const string SimulatorPrefabPath =
        "Assets/Samples/XR Interaction Toolkit/3.3.2/XR Interaction Simulator/XR Interaction Simulator.prefab";

    [InitializeOnLoadMethod]
    private static void ScheduleBuild()
    {
        const string sessionKey = "Bagscaper.StartProfileAssetBuilder.v4";
        if (SessionState.GetBool(sessionKey, false))
        {
            return;
        }

        SessionState.SetBool(sessionKey, true);
        EditorApplication.delayCall += BuildIfNeeded;
    }

    [MenuItem("Tools/Bagscaper/Rebuild Personal Info Selection")]
    private static void RebuildFromMenu()
    {
        BuildAll(true);
    }

    private static void BuildIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        bool needsRebuild = prefab == null ||
            AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefabPath) == null ||
            PrefabCanvasFacesAway(prefab);

        if (needsRebuild)
        {
            BuildAll(prefab != null);
        }
    }

    private static void BuildAll(bool force)
    {
        try
        {
            ConfigureSprites();
            EnsureSimulatorSampleImported();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            if (force || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                BuildPrefab();
            }

            ConfigureStartScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[StartProfileAssetBuilder] 개인정보 선택 프리팹과 Start Scene 구성이 완료되었습니다.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void ConfigureSprites()
    {
        string[] paths =
        {
            "Assets/UI Component/start/Personal Info.png",
            "Assets/UI Component/start/male-button.png",
            "Assets/UI Component/start/male-button-on.png",
            "Assets/UI Component/start/female-button.png",
            "Assets/UI Component/start/female-button-on.png",
            "Assets/Srcs/UI/confirm-off.png",
            "Assets/Srcs/UI/confirm-on.png"
        };

        foreach (string path in paths)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                continue;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = 4096;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }
    }

    private static void BuildPrefab()
    {
        Directory.CreateDirectory("Assets/Prefabs/UI");

        GameObject root = new GameObject("PersonalInfoSelection", typeof(RectTransform));
        try
        {
            StartProfileUIController controller = root.AddComponent<StartProfileUIController>();
            StartProfileInputController input = root.AddComponent<StartProfileInputController>();
            root.AddComponent<PlayerProfileGameStartRequestBridge>();

            GameObject canvasObject = CreateChild(root, "PersonalInfoCanvas", typeof(RectTransform));
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(2460f, 1437f);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;
            canvasObject.AddComponent<GraphicRaycaster>();
            canvasRect.localPosition = new Vector3(0f, 1.6f, 2.5f);
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * 0.00135f;

            GameObject uiRoot = CreateUIChild(canvasObject, "PersonalInfoRoot");
            Stretch(uiRoot.GetComponent<RectTransform>());

            Image background = CreateImage(uiRoot, "Background", Sprite("Assets/UI Component/start/Personal Info.png"));
            Stretch(background.rectTransform);
            background.raycastTarget = false;

            GameObject genderGroup = CreateUIChild(uiRoot, "GenderGroup");
            Stretch(genderGroup.GetComponent<RectTransform>());

            GenderObjects male = CreateGenderButton(
                genderGroup,
                "MaleButton",
                new Vector2(-699f, 183f),
                Sprite("Assets/UI Component/start/male-button.png"),
                Sprite("Assets/UI Component/start/male-button-on.png"));

            GenderObjects female = CreateGenderButton(
                genderGroup,
                "FemaleButton",
                new Vector2(-699f, -211f),
                Sprite("Assets/UI Component/start/female-button.png"),
                Sprite("Assets/UI Component/start/female-button-on.png"));

            GameObject ageSelector = CreateUIChild(uiRoot, "AgeSelector");
            Stretch(ageSelector.GetComponent<RectTransform>());
            TMP_FontAsset regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Fonts/Pretendard-Regular SDF.asset");
            TMP_FontAsset bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Fonts/Pretendard-ExtraBold SDF.asset");

            TMP_Text previous = CreateText(ageSelector, "PreviousAgeText", new Vector2(503f, 202f), regular, 42f, Color.white);
            TMP_Text selected = CreateText(ageSelector, "SelectedAgeText", new Vector2(503f, 42f), bold, 58f, new Color32(0, 255, 212, 255));
            TMP_Text next = CreateText(ageSelector, "NextAgeText", new Vector2(503f, -118f), regular, 42f, Color.white);

            GameObject confirmObject = CreateUIChild(uiRoot, "ConfirmButton");
            RectTransform confirmRect = confirmObject.GetComponent<RectTransform>();
            SetRect(confirmRect, new Vector2(0f, -534f), new Vector2(2160f, 150f));
            Image confirmHit = confirmObject.AddComponent<Image>();
            confirmHit.color = new Color(1f, 1f, 1f, 0.001f);
            Button confirm = confirmObject.AddComponent<Button>();
            confirm.transition = Selectable.Transition.None;
            confirm.targetGraphic = confirmHit;

            Image confirmDisabled = CreateImage(confirmObject, "DisabledVisual", Sprite("Assets/Srcs/UI/confirm-off.png"));
            Stretch(confirmDisabled.rectTransform);
            confirmDisabled.color = new Color(1f, 1f, 1f, 0.88f);
            confirmDisabled.raycastTarget = false;
            Image confirmEnabled = CreateImage(confirmObject, "EnabledVisual", Sprite("Assets/Srcs/UI/confirm-on.png"));
            Stretch(confirmEnabled.rectTransform);
            confirmEnabled.raycastTarget = false;

            SerializedObject controllerObject = new SerializedObject(controller);
            Set(controllerObject, "maleButton", male.Button);
            Set(controllerObject, "maleNormalImage", male.Normal);
            Set(controllerObject, "maleSelectedImage", male.Selected);
            Set(controllerObject, "maleAnimationTarget", male.Rect);
            Set(controllerObject, "femaleButton", female.Button);
            Set(controllerObject, "femaleNormalImage", female.Normal);
            Set(controllerObject, "femaleSelectedImage", female.Selected);
            Set(controllerObject, "femaleAnimationTarget", female.Rect);
            Set(controllerObject, "previousAgeText", previous);
            Set(controllerObject, "selectedAgeText", selected);
            Set(controllerObject, "nextAgeText", next);
            Set(controllerObject, "confirmButton", confirm);
            Set(controllerObject, "confirmDisabledVisual", confirmDisabled);
            Set(controllerObject, "confirmEnabledVisual", confirmEnabled);
            Set(controllerObject, "personalInfoCanvas", canvasObject);
            controllerObject.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject inputObject = new SerializedObject(input);
            Set(inputObject, "uiController", controller);
            inputObject.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void ConfigureStartScene()
    {
        bool mainWasAlreadyLoaded = IsSceneLoaded(MainScenePath);
        Scene startScene = GetOrOpenScene(StartScenePath);
        Scene mainScene = GetOrOpenScene(MainScenePath);

        GameObject mainCamera = FindRoot(startScene, "Main Camera");
        if (mainCamera != null)
        {
            UnityEngine.Object.DestroyImmediate(mainCamera);
        }

        CopyInfrastructureIfMissing(mainScene, startScene, "OVRCameraRig");
        CopyInfrastructureIfMissing(mainScene, startScene, "PointableCanvasModule");

        GameObject cameraRig = FindRoot(startScene, "OVRCameraRig");
        Transform interactionRig = FindDescendant(cameraRig?.transform, "OVRComprehensiveInteractionRig");
        if (interactionRig != null)
        {
            interactionRig.gameObject.SetActive(true);
        }

        if (FindRoot(startScene, "PersonalInfoSelection") == null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            PrefabUtility.InstantiatePrefab(prefab, startScene);
        }

        if (FindRoot(startScene, "XR Interaction Simulator") == null)
        {
            GameObject simulatorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefabPath);
            if (simulatorPrefab != null)
            {
                GameObject simulator = (GameObject)PrefabUtility.InstantiatePrefab(simulatorPrefab, startScene);
                simulator.tag = "EditorOnly";
            }
        }

        EditorSceneManager.MarkSceneDirty(startScene);
        EditorSceneManager.SaveScene(startScene);

        if (!mainWasAlreadyLoaded && mainScene.isLoaded && mainScene.path == MainScenePath)
        {
            EditorSceneManager.CloseScene(mainScene, true);
        }
    }

    private static void EnsureSimulatorSampleImported()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefabPath) != null)
        {
            return;
        }

        Sample sample = Sample.FindByPackage("com.unity.xr.interaction.toolkit", "3.3.2")
            .FirstOrDefault(candidate => candidate.displayName == "XR Interaction Simulator");
        if (string.IsNullOrEmpty(sample.displayName) ||
            !sample.Import(Sample.ImportOptions.OverridePreviousImports))
        {
            throw new InvalidOperationException("XR Interaction Simulator 샘플을 가져오지 못했습니다.");
        }
    }

    private static bool PrefabCanvasFacesAway(GameObject prefab)
    {
        if (prefab == null)
        {
            return true;
        }

        Transform canvas = FindDescendant(prefab.transform, "PersonalInfoCanvas");
        if (canvas is not RectTransform canvasRect)
        {
            return true;
        }

        return Quaternion.Angle(canvas.localRotation, Quaternion.identity) > 0.1f ||
            Vector2.Distance(canvasRect.anchoredPosition, new Vector2(0f, 1.6f)) > 0.01f ||
            Mathf.Abs(canvas.localPosition.z - 2.5f) > 0.01f;
    }

    private static bool IsSceneLoaded(string path)
    {
        return Enumerable.Range(0, SceneManager.sceneCount)
            .Select(SceneManager.GetSceneAt)
            .Any(scene => scene.path == path && scene.isLoaded);
    }

    private static Scene GetOrOpenScene(string path)
    {
        Scene loaded = Enumerable.Range(0, SceneManager.sceneCount)
            .Select(SceneManager.GetSceneAt)
            .FirstOrDefault(scene => scene.path == path);
        return loaded.IsValid() ? loaded : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
    }

    private static void CopyInfrastructureIfMissing(Scene source, Scene destination, string rootName)
    {
        if (FindRoot(destination, rootName) != null)
        {
            return;
        }

        GameObject sourceRoot = FindRoot(source, rootName);
        if (sourceRoot == null)
        {
            Debug.LogWarning($"[StartProfileAssetBuilder] Main Scene에서 {rootName}을 찾지 못했습니다.");
            return;
        }

        GameObject clone = UnityEngine.Object.Instantiate(sourceRoot);
        clone.name = rootName;
        SceneManager.MoveGameObjectToScene(clone, destination);
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        return scene.IsValid() && scene.isLoaded
            ? scene.GetRootGameObjects().FirstOrDefault(root => root.name == name)
            : null;
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform found = FindDescendant(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static GenderObjects CreateGenderButton(
        GameObject parent,
        string name,
        Vector2 position,
        Sprite normalSprite,
        Sprite selectedSprite)
    {
        GameObject buttonObject = CreateUIChild(parent, name);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        SetRect(rect, position, new Vector2(660f, 310f));
        Image hitArea = buttonObject.AddComponent<Image>();
        hitArea.color = new Color(1f, 1f, 1f, 0.001f);
        Button button = buttonObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hitArea;

        Image normal = CreateImage(buttonObject, "NormalImage", normalSprite);
        SetRect(normal.rectTransform, Vector2.zero, new Vector2(660f, 309f));
        normal.raycastTarget = false;

        Image selected = CreateImage(buttonObject, "SelectedImage", selectedSprite);
        SetRect(selected.rectTransform, Vector2.zero, new Vector2(804f, 453f));
        selected.raycastTarget = false;
        selected.gameObject.SetActive(false);

        return new GenderObjects(button, normal, selected, rect);
    }

    private static TMP_Text CreateText(
        GameObject parent,
        string name,
        Vector2 position,
        TMP_FontAsset font,
        float fontSize,
        Color color)
    {
        GameObject textObject = CreateUIChild(parent, name);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        SetRect(rect, position, new Vector2(900f, 100f));
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateChild(GameObject parent, string name, params Type[] components)
    {
        GameObject child = new GameObject(name, components);
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    private static GameObject CreateUIChild(GameObject parent, string name)
    {
        return CreateChild(parent, name, typeof(RectTransform));
    }

    private static Image CreateImage(GameObject parent, string name, Sprite sprite)
    {
        GameObject imageObject = CreateUIChild(parent, name);
        Image image = imageObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = false;
        return image;
    }

    private static Sprite Sprite(string path)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            throw new InvalidOperationException($"Sprite를 불러올 수 없습니다: {path}");
        }
        return sprite;
    }

    private static void SetRect(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Set(SerializedObject target, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty property = target.FindProperty(propertyName);
        if (property == null)
        {
            throw new MissingFieldException(target.targetObject.GetType().Name, propertyName);
        }
        property.objectReferenceValue = value;
    }

    private readonly struct GenderObjects
    {
        public GenderObjects(Button button, Image normal, Image selected, RectTransform rect)
        {
            Button = button;
            Normal = normal;
            Selected = selected;
            Rect = rect;
        }

        public Button Button { get; }
        public Image Normal { get; }
        public Image Selected { get; }
        public RectTransform Rect { get; }
    }
}
