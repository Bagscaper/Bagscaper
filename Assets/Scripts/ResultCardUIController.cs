using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the three success-card pages without depending on game state,
/// input devices, raycasters, or any other project class.
/// </summary>
public sealed class ResultCardUIController : MonoBehaviour
{
    [SerializeField] private GameObject contentRoot;
    [SerializeField] private GameObject[] pages;
    [SerializeField] private Button[] pageTriggers;
    [SerializeField] private bool showFirstPageOnAwake = true;

    public int CurrentPageIndex { get; private set; } = -1;
    public int PageCount => pages != null ? pages.Length : 0;
    public bool HasNextPage => CurrentPageIndex >= 0 && CurrentPageIndex + 1 < PageCount;

    private void Awake()
    {
        AddTriggerListeners();

        if (showFirstPageOnAwake)
        {
            ResetAndShow();
        }
        else
        {
            Hide();
        }
    }

    private void OnDestroy()
    {
        RemoveTriggerListeners();
    }

    /// <summary>Shows the first card and activates the UI.</summary>
    public void ResetAndShow()
    {
        ShowPage(0);
    }

    /// <summary>Shows this UI again without changing the current page.</summary>
    public void Show()
    {
        if (CurrentPageIndex < 0 && PageCount > 0)
        {
            CurrentPageIndex = 0;
            RefreshPages();
        }

        SetActive(contentRoot, true);
    }

    /// <summary>Hides the entire card UI while leaving this controller callable.</summary>
    public void Hide()
    {
        SetActive(contentRoot, false);
    }

    /// <summary>Shows a page by zero-based index. Invalid indices are ignored.</summary>
    public void ShowPage(int pageIndex)
    {
        if (pages == null || pageIndex < 0 || pageIndex >= pages.Length)
        {
            return;
        }

        CurrentPageIndex = pageIndex;
        SetActive(contentRoot, true);
        RefreshPages();
    }

    /// <summary>
    /// Advances Card 1 to 2 or Card 2 to 3. Calling it on Card 3 does nothing.
    /// This is the single method UI triggers and other developers can call.
    /// </summary>
    public void ShowNextPage()
    {
        if (!HasNextPage)
        {
            return;
        }

        ShowPage(CurrentPageIndex + 1);
    }

    private void AddTriggerListeners()
    {
        if (pageTriggers == null)
        {
            return;
        }

        foreach (Button trigger in pageTriggers)
        {
            trigger?.onClick.AddListener(ShowNextPage);
        }
    }

    private void RemoveTriggerListeners()
    {
        if (pageTriggers == null)
        {
            return;
        }

        foreach (Button trigger in pageTriggers)
        {
            trigger?.onClick.RemoveListener(ShowNextPage);
        }
    }

    private void RefreshPages()
    {
        if (pages == null)
        {
            return;
        }

        for (int i = 0; i < pages.Length; i++)
        {
            SetActive(pages[i], i == CurrentPageIndex);
        }
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null)
        {
            target.SetActive(active);
        }
    }
}
