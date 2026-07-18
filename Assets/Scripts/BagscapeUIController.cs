using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BagscapeUIController : MonoBehaviour
{
    [Header("초기 안내")]
    [SerializeField] private GameObject initialPanel;
    [SerializeField] private GameObject[] introPages;

    [Header("인게임")]
    [SerializeField] private GameObject inGameRoot;
    [SerializeField] private TMP_Text leftTimeText;
    [SerializeField] private Image volumeImage;
    [SerializeField] private TMP_Text volumePercentText;
    [SerializeField] private TMP_Text weightText;

    [Header("결과")]
    [SerializeField] private GameObject gameResultRoot;
    [SerializeField] private GameObject resultLoadingRoot;
    [SerializeField] private GameObject[] successCards;
    [SerializeField] private GameObject[] failCards;

    [Header("성공 카드 텍스트")]
    [SerializeField] private TMP_Text successCard1Text;
    [SerializeField] private TMP_Text successRuleText;
    [SerializeField] private TMP_Text successAiCommentText;

    [Header("실패 카드 텍스트")]
    [SerializeField] private TMP_Text failCard1Text;
    [SerializeField] private TMP_Text failRuleText;
    [SerializeField] private TMP_Text failAiCommentText;

    public int IntroPageCount => introPages != null ? introPages.Length : 0;

    private void Awake()
    {
        ResetAll();
    }

    public void ResetAll()
    {
        SetActive(initialPanel, false);
        SetActive(inGameRoot, false);
        SetActive(gameResultRoot, false);
        SetActive(resultLoadingRoot, false);
        SetAll(introPages, false);
        SetAll(successCards, false);
        SetAll(failCards, false);
        UpdateHud(0f, 0f, 0f);
    }

    public void ShowIntroPage(int index)
    {
        SetActive(initialPanel, true);
        SetActive(inGameRoot, false);
        SetActive(gameResultRoot, false);
        SetActive(resultLoadingRoot, false);

        if (introPages == null)
        {
            return;
        }

        for (int i = 0; i < introPages.Length; i++)
        {
            SetActive(introPages[i], i == index);
        }
    }

    public void ShowGameplay()
    {
        SetActive(initialPanel, false);
        SetAll(introPages, false);
        SetActive(inGameRoot, true);
        SetActive(gameResultRoot, false);
        SetActive(resultLoadingRoot, false);
        SetAll(successCards, false);
        SetAll(failCards, false);
    }

    public void UpdateHud(float remainingSeconds, float currentWeight, float maxWeight)
    {
        if (leftTimeText != null)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            leftTimeText.text = $"{minutes:00}:{seconds:00}";
        }

        float ratio = maxWeight > 0f ? currentWeight / maxWeight : 0f;

        if (volumeImage != null)
        {
            volumeImage.fillAmount = Mathf.Clamp01(ratio);
        }

        if (volumePercentText != null)
        {
            volumePercentText.text = $"{Mathf.RoundToInt(ratio * 100f)}%";
        }

        if (weightText != null)
        {
            weightText.text = $"{currentWeight:0.0} / {maxWeight:0.0}kg";
        }
    }

    public void ShowWaitingForResult()
    {
        SetActive(initialPanel, false);
        SetActive(inGameRoot, false);
        SetActive(gameResultRoot, true);
        SetActive(resultLoadingRoot, true);
        SetAll(successCards, false);
        SetAll(failCards, false);
    }

    public void BeginResult(
        bool success,
        string ruleText,
        string aiComment,
        int survivalTimeHours)
    {
        SetActive(initialPanel, false);
        SetActive(inGameRoot, false);
        SetActive(gameResultRoot, true);
        SetActive(resultLoadingRoot, false);

        if (successCard1Text != null)
        {
            successCard1Text.text = survivalTimeHours > 0
                ? $"예상 생존 시간\n{survivalTimeHours}시간"
                : "생존 준비 성공";
        }

        if (failCard1Text != null)
        {
            failCard1Text.text = survivalTimeHours > 0
                ? $"예상 생존 시간\n{survivalTimeHours}시간"
                : "생존 준비 실패";
        }

        if (successRuleText != null)
        {
            successRuleText.text = ruleText;
        }

        if (failRuleText != null)
        {
            failRuleText.text = ruleText;
        }

        if (successAiCommentText != null)
        {
            successAiCommentText.text = aiComment;
        }

        if (failAiCommentText != null)
        {
            failAiCommentText.text = aiComment;
        }

        ShowResultCard(success, 0);
    }

    public void ShowResultCard(bool success, int index)
    {
        SetAll(successCards, false);
        SetAll(failCards, false);

        GameObject[] targetCards = success ? successCards : failCards;
        if (targetCards == null || index < 0 || index >= targetCards.Length)
        {
            return;
        }

        SetActive(targetCards[index], true);
    }

    private static void SetAll(GameObject[] objects, bool value)
    {
        if (objects == null)
        {
            return;
        }

        foreach (GameObject target in objects)
        {
            SetActive(target, value);
        }
    }

    private static void SetActive(GameObject target, bool value)
    {
        if (target != null)
        {
            target.SetActive(value);
        }
    }
}
