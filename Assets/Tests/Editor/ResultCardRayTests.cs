using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class ResultCardRayTests
{
    private const string PrefabPath = "Assets/Prefabs/UI/ResultSuccessCards.prefab";

    private GameObject prefabRoot;
    private ResultCardUIController controller;
    private GameObject[] pages;

    [SetUp]
    public void SetUp()
    {
        prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        controller = prefabRoot.GetComponent<ResultCardUIController>();
        pages = new[]
        {
            prefabRoot.transform.Find("CardPages/SuccessCard1").gameObject,
            prefabRoot.transform.Find("CardPages/SuccessCard2").gameObject,
            prefabRoot.transform.Find("CardPages/SuccessCard3").gameObject
        };
    }

    [TearDown]
    public void TearDown()
    {
        if (prefabRoot != null)
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    [Test]
    public void Prefab_HasIndependentControllerAndThreeButtonPages()
    {
        Assert.That(controller, Is.Not.Null);
        Assert.That(controller.PageCount, Is.EqualTo(3));

        foreach (GameObject page in pages)
        {
            Assert.That(page.GetComponent<Image>(), Is.Not.Null);
            Assert.That(page.GetComponent<Button>(), Is.Not.Null);
        }
    }

    [Test]
    public void ShowNextPage_AdvancesOneToTwoAndTwoToThree()
    {
        controller.ResetAndShow();
        AssertActivePage(0);

        controller.ShowNextPage();
        AssertActivePage(1);

        controller.ShowNextPage();
        AssertActivePage(2);
    }

    [Test]
    public void ShowNextPage_OnThirdPage_DoesNothing()
    {
        controller.ShowPage(2);
        controller.ShowNextPage();

        Assert.That(controller.CurrentPageIndex, Is.EqualTo(2));
        AssertActivePage(2);
    }

    [Test]
    public void HideAndShow_ControlContentWithoutDisablingController()
    {
        Transform content = prefabRoot.transform.Find("CardPages");

        controller.ResetAndShow();
        controller.Hide();
        Assert.That(content.gameObject.activeSelf, Is.False);
        Assert.That(controller.gameObject.activeSelf, Is.True);

        controller.Show();
        Assert.That(content.gameObject.activeSelf, Is.True);
        AssertActivePage(0);
    }

    [Test]
    public void Pages_UseExpectedSuccessCardSprites()
    {
        string[] expectedPaths =
        {
            "Assets/UI Component/result/SuccessCard1.png",
            "Assets/UI Component/result/SuccessCard2.png",
            "Assets/UI Component/result/SuccessCard3.png"
        };

        for (int i = 0; i < pages.Length; i++)
        {
            Sprite sprite = pages[i].GetComponent<Image>().sprite;
            Assert.That(sprite, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(sprite), Is.EqualTo(expectedPaths[i]));
        }
    }

    private void AssertActivePage(int expectedIndex)
    {
        for (int i = 0; i < pages.Length; i++)
        {
            Assert.That(
                pages[i].activeSelf,
                Is.EqualTo(i == expectedIndex),
                $"SuccessCard{i + 1} active state is incorrect.");
        }
    }
}
