using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class StartProfileAssetTests
{
    private const string PrefabPath = "Assets/Prefabs/UI/PersonalInfoSelection.prefab";
    private const string ScenePath = "Assets/Scenes/Start Scene.unity";

    [Test]
    public void PrefabIsSelfContainedAndHasNoMissingComponents()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Assert.That(root.GetComponent<StartProfileUIController>(), Is.Not.Null);
            Assert.That(root.GetComponent<StartProfileInputController>(), Is.Not.Null);
            Assert.That(root.GetComponent<PlayerProfileGameStartRequestBridge>(), Is.Not.Null);

            Canvas canvas = root.GetComponentInChildren<Canvas>(true);
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(Quaternion.Angle(canvas.transform.localRotation, Quaternion.identity), Is.LessThan(0.1f));
            RectTransform canvasRect = (RectTransform)canvas.transform;
            Assert.That(Vector2.Distance(canvasRect.anchoredPosition, new Vector2(0f, 1.6f)), Is.LessThan(0.01f));
            Assert.That(Mathf.Abs(canvasRect.localPosition.z - 2.5f), Is.LessThan(0.01f));
            Assert.That(root.GetComponentsInChildren<Button>(true).Length, Is.EqualTo(3));
            Assert.That(
                root.GetComponentsInChildren<Component>(true).Any(component => component == null),
                Is.False,
                "프리팹에 missing script가 없어야 합니다.");

            SerializedObject serializedController = new SerializedObject(
                root.GetComponent<StartProfileUIController>());
            string[] requiredReferences =
            {
                "maleButton", "maleNormalImage", "maleSelectedImage", "maleAnimationTarget",
                "femaleButton", "femaleNormalImage", "femaleSelectedImage", "femaleAnimationTarget",
                "previousAgeText", "selectedAgeText", "nextAgeText", "confirmButton",
                "confirmDisabledVisual", "confirmEnabledVisual", "personalInfoCanvas"
            };

            foreach (string propertyName in requiredReferences)
            {
                Assert.That(
                    serializedController.FindProperty(propertyName).objectReferenceValue,
                    Is.Not.Null,
                    $"{propertyName} 참조가 연결되어야 합니다.");
            }

            Image background = root.GetComponentsInChildren<Image>(true)
                .Single(image => image.name == "Background");
            Assert.That(background.raycastTarget, Is.False);
            Assert.That(
                root.GetComponentsInChildren<Image>(true)
                    .Where(image => image.name.EndsWith("Image"))
                    .All(image => !image.raycastTarget),
                Is.True);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void StartSceneContainsRequiredInfrastructureAndPrefabInstance()
    {
        bool alreadyLoaded = SceneManager.GetSceneByPath(ScenePath).isLoaded;
        Scene scene = alreadyLoaded
            ? SceneManager.GetSceneByPath(ScenePath)
            : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            Assert.That(roots.Any(root => root.name == "Main Camera"), Is.False);
            Assert.That(roots.Any(root => root.name == "OVRCameraRig"), Is.True);
            Assert.That(roots.Any(root => root.name == "PointableCanvasModule"), Is.True);

            GameObject simulator = roots.Single(root => root.name == "XR Interaction Simulator");
            Assert.That(simulator.CompareTag("EditorOnly"), Is.True);

            GameObject profile = roots.Single(root => root.name == "PersonalInfoSelection");
            Assert.That(
                PrefabUtility.GetPrefabInstanceStatus(profile),
                Is.EqualTo(PrefabInstanceStatus.Connected));

            int eventSystemCount = roots
                .SelectMany(root => root.GetComponentsInChildren<EventSystem>(true))
                .Count();
            Assert.That(eventSystemCount, Is.EqualTo(1));
        }
        finally
        {
            if (!alreadyLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
