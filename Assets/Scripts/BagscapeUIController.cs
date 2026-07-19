using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BagscapeUIController : MonoBehaviour
{
    private const int LocalResultCardCount = 2;

    [Header("초기 안내")]
    [SerializeField] private GameObject initialPanel;
    [SerializeField] private GameObject[] introPages;

    [Header("인게임")]
    [SerializeField] private GameObject inGameRoot;
    [SerializeField] private TMP_Text leftTimeText;
    [SerializeField] private Image volumeImage;
    [SerializeField] private TMP_Text volumePercentText;

    [Header("결과")]
    [SerializeField] private GameObject gameResultRoot;
    [SerializeField] private GameObject[] successCards;
    [SerializeField] private GameObject[] failCards;

    [Header("성공 결과 - 1번 카드는 이미지 전용")]
    [SerializeField] private TMP_Text successRuleText;

    [Header("실패 결과")]
    [SerializeField] private TMP_Text failCard1Text;
    [SerializeField] private TMP_Text failRuleText;

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
        SetAll(introPages, false);
        SetAll(successCards, false);
        SetAll(failCards, false);
        UpdateHud(0f, 0, 0);
    }

    public void ShowIntroPage(int index)
    {
        SetActive(initialPanel, true);
        SetActive(inGameRoot, false);
        SetActive(gameResultRoot, false);

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
        SetAll(successCards, false);
        SetAll(failCards, false);
    }

    public void UpdateHud(
        float remainingSeconds,
        int currentWeightGrams,
        int maxWeightGrams)
    {
        if (leftTimeText != null)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            leftTimeText.text = $"{minutes:00}:{seconds:00}";
        }

        float ratio = maxWeightGrams > 0
            ? (float)currentWeightGrams / maxWeightGrams
            : 0f;

        if (volumeImage != null)
        {
            volumeImage.fillAmount = Mathf.Clamp01(ratio);
        }

        if (volumePercentText != null)
        {
            volumePercentText.text = $"{Mathf.RoundToInt(ratio * 100f)}%";
        }
    }

    public void BeginResult(bool success, string ruleText)
    {
        SetActive(initialPanel, false);
        SetActive(inGameRoot, false);
        SetActive(gameResultRoot, true);

        if (failCard1Text != null)
        {
            failCard1Text.text = "생존 준비 실패";
        }

        if (successRuleText != null)
        {
            successRuleText.text = ruleText;
        }

        if (failRuleText != null)
        {
            failRuleText.text = ruleText;
        }

        ShowResultCard(success, 0);
    }

    public int GetResultCardCount(bool success)
    {
        GameObject[] targetCards = success ? successCards : failCards;
        int configuredCount = targetCards != null ? targetCards.Length : 0;

        // 기존 3번 카드가 배열에 남아 있어도 표시하지 않습니다.
        return Mathf.Min(configuredCount, LocalResultCardCount);
    }

    public void ShowResultCard(bool success, int index)
    {
        SetAll(successCards, false);
        SetAll(failCards, false);

        GameObject[] targetCards = success ? successCards : failCards;
        int cardCount = GetResultCardCount(success);

        if (targetCards == null || index < 0 || index >= cardCount)
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
