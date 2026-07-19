using System;
using System.Collections.Generic;
using System.Text;

[Serializable]
public class RuleBasedResult
{
    public bool isSuccess;
    public int score;
    public int trapCount;
    public bool hasWater;
    public bool hasFood;
    public bool hasMedical;
    public bool isOverWeight;
    public string cardText;
}

public static class RuleBasedEvaluator
{
    private const int SuccessScoreThreshold = 15;

    public static RuleBasedResult Evaluate(
        string disaster,
        int currentWeightGrams,
        int maxWeightGrams,
        IReadOnlyDictionary<string, int> selectedItems,
        SurvivalDataLoader dataLoader)
    {
        RuleBasedResult result = new RuleBasedResult();

        if (dataLoader == null || !dataLoader.IsLoaded)
        {
            result.isSuccess = false;
            result.cardText = "룰베이스 평가를 수행할 아이템 데이터가 없습니다.";
            return result;
        }

        int totalScore = 0;
        int trapCount = 0;
        int selectedCount = 0;
        int relevantCount = 0;

        foreach (KeyValuePair<string, int> selected in selectedItems)
        {
            SurvivalItemData item = dataLoader.GetItemById(selected.Key);
            if (item == null || selected.Value <= 0)
            {
                continue;
            }

            int quantity = selected.Value;
            selectedCount += quantity;

            result.hasWater |= IsCategory(item, "Water");
            result.hasFood |= IsCategory(item, "Food");
            result.hasMedical |= IsCategory(item, "Medical");

            bool isRelevant = IsRelevantForDisaster(item, disaster);
            if (isRelevant)
            {
                relevantCount += quantity;
            }

            int itemScore = item.importance * quantity;

            if (isRelevant)
            {
                itemScore += quantity;
            }

            if (item.isTrap)
            {
                trapCount += quantity;
                itemScore -= 3 * quantity;
            }

            totalScore += itemScore;
        }

        result.trapCount = trapCount;
        result.isOverWeight =
            maxWeightGrams > 0 && currentWeightGrams > maxWeightGrams;
        result.score = totalScore;

        bool hasEssentialSet =
            result.hasWater && result.hasFood && result.hasMedical;

        result.isSuccess =
            hasEssentialSet &&
            !result.isOverWeight &&
            totalScore >= SuccessScoreThreshold;

        result.cardText = BuildCardText(
            result,
            selectedCount,
            relevantCount,
            currentWeightGrams,
            maxWeightGrams
        );

        return result;
    }

    private static bool IsCategory(SurvivalItemData item, string category)
    {
        return string.Equals(
            item.category,
            category,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool IsRelevantForDisaster(
        SurvivalItemData item,
        string disaster)
    {
        if (item.usageContexts == null || item.usageContexts.Count == 0)
        {
            return false;
        }

        foreach (string context in item.usageContexts)
        {
            if (string.Equals(
                    context,
                    "All",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                string.Equals(
                    context,
                    disaster,
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildCardText(
        RuleBasedResult result,
        int selectedCount,
        int relevantCount,
        int currentWeightGrams,
        int maxWeightGrams)
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine(
            result.isSuccess
                ? "룰베이스 성공 근거"
                : "룰베이스 실패 이유"
        );

        builder.AppendLine();
        builder.AppendLine($"• 선택 아이템: {selectedCount}/16개");
        builder.AppendLine($"• 상황 적합 아이템: {relevantCount}개");
        builder.AppendLine($"• 중요도 점수: {result.score}점");
        builder.AppendLine($"• 함정 아이템: {result.trapCount}개");

        if (maxWeightGrams > 0)
        {
            builder.AppendLine(
                $"• 가방 무게: {currentWeightGrams:N0}g / {maxWeightGrams:N0}g"
            );
        }

        builder.AppendLine();

        if (!result.hasWater)
        {
            builder.AppendLine("• 식수가 없습니다.");
        }

        if (!result.hasFood)
        {
            builder.AppendLine("• 비상식량이 없습니다.");
        }

        if (!result.hasMedical)
        {
            builder.AppendLine("• 응급 의료용품이 없습니다.");
        }

        if (result.isOverWeight)
        {
            builder.AppendLine("• 가방의 허용 무게를 초과했습니다.");
        }

        if (result.trapCount > 0)
        {
            builder.AppendLine(
                "• 생존 우선순위가 낮은 함정 아이템이 포함되었습니다."
            );
        }

        if (result.isSuccess)
        {
            builder.AppendLine(
                "• 필수 생존 범주와 무게 조건을 충족했습니다."
            );
        }
        else if (result.score < SuccessScoreThreshold)
        {
            builder.AppendLine(
                $"• 성공 기준 점수 {SuccessScoreThreshold}점에 도달하지 못했습니다."
            );
        }

        return builder.ToString().TrimEnd();
    }
}
