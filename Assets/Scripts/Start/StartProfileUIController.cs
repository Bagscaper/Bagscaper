using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed class StartProfileUIController : MonoBehaviour
{
    private static readonly string[] AgeLabels =
    {
        "어린이",
        "청소년",
        "20~30대",
        "40~50대",
        "60대+"
    };

    private static readonly PlayerProfileSelection.AgeGroup[] AgeValues =
    {
        PlayerProfileSelection.AgeGroup.Child,
        PlayerProfileSelection.AgeGroup.Teen,
        PlayerProfileSelection.AgeGroup.Age20To30,
        PlayerProfileSelection.AgeGroup.Age40To50,
        PlayerProfileSelection.AgeGroup.Age60Plus
    };

    [Header("성별")]
    [SerializeField] private Button maleButton;
    [SerializeField] private Image maleNormalImage;
    [SerializeField] private Image maleSelectedImage;
    [SerializeField] private RectTransform maleAnimationTarget;
    [SerializeField] private Button femaleButton;
    [SerializeField] private Image femaleNormalImage;
    [SerializeField] private Image femaleSelectedImage;
    [SerializeField] private RectTransform femaleAnimationTarget;

    [Header("나이")]
    [SerializeField] private TMP_Text previousAgeText;
    [SerializeField] private TMP_Text selectedAgeText;
    [SerializeField] private TMP_Text nextAgeText;

    [Header("확인")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private Image confirmDisabledVisual;
    [SerializeField] private Image confirmEnabledVisual;
    [SerializeField] private GameObject personalInfoCanvas;
    [SerializeField] private UnityEvent onConfirmed = new UnityEvent();

    [Header("피드백")]
    [SerializeField, Range(1.01f, 1.2f)] private float pressedScale = 1.06f;
    [SerializeField, Min(0.02f)] private float animationDuration = 0.1f;

    private PlayerProfileSelection.Gender? selectedGender;
    private int ageIndex = 2;
    private bool isConfirmed;
    private Coroutine genderAnimation;

    public bool HasSelectedGender => selectedGender.HasValue;
    public int SelectedAgeIndex => ageIndex;
    public string SelectedAgeLabel => AgeLabels[ageIndex];

    private void Awake()
    {
        if (maleButton != null)
        {
            maleButton.onClick.AddListener(SelectMale);
        }

        if (femaleButton != null)
        {
            femaleButton.onClick.AddListener(SelectFemale);
        }

        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(OnConfirm);
        }

        RefreshAll();
    }

    private void OnDestroy()
    {
        if (maleButton != null) maleButton.onClick.RemoveListener(SelectMale);
        if (femaleButton != null) femaleButton.onClick.RemoveListener(SelectFemale);
        if (confirmButton != null) confirmButton.onClick.RemoveListener(OnConfirm);
    }

    public void SelectMale() => BeginGenderSelection(PlayerProfileSelection.Gender.Male);

    public void SelectFemale() => BeginGenderSelection(PlayerProfileSelection.Gender.Female);

    public void SelectPreviousAge()
    {
        SetAgeIndex(ageIndex - 1);
    }

    public void SelectNextAge()
    {
        SetAgeIndex(ageIndex + 1);
    }

    public void SetAgeIndex(int index)
    {
        ageIndex = Mathf.Clamp(index, 0, AgeLabels.Length - 1);
        RefreshAgeTexts();
    }

    public void OnConfirm()
    {
        if (isConfirmed || !selectedGender.HasValue)
        {
            return;
        }

        isConfirmed = true;
        PlayerProfileSelection.Set(selectedGender.Value, AgeValues[ageIndex]);

        if (personalInfoCanvas != null)
        {
            personalInfoCanvas.SetActive(false);
        }

        onConfirmed?.Invoke();
    }

    private void BeginGenderSelection(PlayerProfileSelection.Gender gender)
    {
        if (isConfirmed)
        {
            return;
        }

        if (genderAnimation != null)
        {
            StopCoroutine(genderAnimation);
        }

        ResetAnimationScales();
        genderAnimation = StartCoroutine(AnimateGenderSelection(gender));
    }

    private IEnumerator AnimateGenderSelection(PlayerProfileSelection.Gender gender)
    {
        RectTransform target = gender == PlayerProfileSelection.Gender.Male
            ? maleAnimationTarget
            : femaleAnimationTarget;

        Vector3 startScale = Vector3.one;
        float halfDuration = animationDuration * 0.5f;

        if (target != null)
        {
            yield return ScaleOverTime(target, startScale, Vector3.one * pressedScale, halfDuration);
            yield return ScaleOverTime(target, Vector3.one * pressedScale, startScale, halfDuration);
            target.localScale = startScale;
        }

        selectedGender = gender;
        genderAnimation = null;
        RefreshGenderVisuals();
        RefreshConfirmState();
    }

    private static IEnumerator ScaleOverTime(
        RectTransform target,
        Vector3 from,
        Vector3 to,
        float duration)
    {
        if (duration <= 0f)
        {
            target.localScale = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            target.localScale = Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        target.localScale = to;
    }

    private void RefreshAll()
    {
        ResetAnimationScales();
        RefreshGenderVisuals();
        RefreshAgeTexts();
        RefreshConfirmState();
    }

    private void RefreshGenderVisuals()
    {
        bool maleSelected = selectedGender == PlayerProfileSelection.Gender.Male;
        bool femaleSelected = selectedGender == PlayerProfileSelection.Gender.Female;

        SetImageActive(maleNormalImage, !maleSelected);
        SetImageActive(maleSelectedImage, maleSelected);
        SetImageActive(femaleNormalImage, !femaleSelected);
        SetImageActive(femaleSelectedImage, femaleSelected);
    }

    private void RefreshAgeTexts()
    {
        SetText(previousAgeText, ageIndex > 0 ? AgeLabels[ageIndex - 1] : string.Empty);
        SetText(selectedAgeText, AgeLabels[ageIndex]);
        SetText(nextAgeText, ageIndex < AgeLabels.Length - 1 ? AgeLabels[ageIndex + 1] : string.Empty);
    }

    private void RefreshConfirmState()
    {
        bool canConfirm = selectedGender.HasValue && !isConfirmed;
        if (confirmButton != null) confirmButton.interactable = canConfirm;
        SetImageActive(confirmDisabledVisual, !canConfirm);
        SetImageActive(confirmEnabledVisual, canConfirm);
    }

    private void ResetAnimationScales()
    {
        if (maleAnimationTarget != null) maleAnimationTarget.localScale = Vector3.one;
        if (femaleAnimationTarget != null) femaleAnimationTarget.localScale = Vector3.one;
    }

    private static void SetImageActive(Image image, bool active)
    {
        if (image != null) image.gameObject.SetActive(active);
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null) text.text = value;
    }
}
