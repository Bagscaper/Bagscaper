using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// JSON에 저장된 규칙을 기준으로 최종 점수를 계산합니다.
/// GameObject에 붙이지 않고 GameManager에서 바로 호출합니다.
/// </summary>
public static class SurvivalScoreCalculator
{
    public static SurvivalScoreResult Evaluate(
        SurvivalScoreInput input,
        SurvivalItemDatabase itemDatabase,
        SurvivalScoreRuleDatabase ruleDatabase)
    {
        ValidateInput(input, itemDatabase, ruleDatabase);

        Dictionary<string, SurvivalItemData> itemMap =
            BuildItemMap(itemDatabase.items);

        Dictionary<string, int> selectedQuantityMap =
            BuildSelectedQuantityMap(input.selectedItems);

        HashSet<string> availableItemSet =
            BuildAvailableItemSet(input.availableItemIds);

        Dictionary<string, float> rawGroupScores =
            CreateGroupScoreMap(ruleDatabase.scoreGroups);

        List<MissingScoreItem> missingItems =
            new List<MissingScoreItem>();

        List<ExcessScoreItem> excessItems =
            new List<ExcessScoreItem>();

        List<RequirementWarning> requirementWarnings =
            new List<RequirementWarning>();

        foreach (ItemScoreRule rule in ruleDatabase.itemRules)
        {
            int quantity = GetQuantity(
                selectedQuantityMap,
                rule.itemId
            );

            SurvivalItemData item;
            itemMap.TryGetValue(rule.itemId, out item);

            if (quantity > 0 && item != null && !item.isTrap)
            {
                bool requirementsMet = HasAllRequirements(
                    selectedQuantityMap,
                    rule.requiredItemIds
                );

                int itemScore = requirementsMet
                    ? rule.baseScore
                    : rule.scoreWhenRequirementsMissing;

                if (requirementsMet)
                {
                    itemScore += GetDisasterBonus(
                        rule.disasterBonuses,
                        input.disaster
                    );
                }
                else
                {
                    requirementWarnings.Add(
                        CreateRequirementWarning(
                            rule,
                            item,
                            selectedQuantityMap
                        )
                    );
                }

                // 기본 점수는 수량과 관계없이 아이템 종류당 한 번만 더합니다.
                AddGroupScore(
                    rawGroupScores,
                    rule.scoreGroup,
                    itemScore
                );
            }

            if (rule.essential &&
                quantity < rule.recommendedMin &&
                IsAvailable(rule.itemId, availableItemSet))
            {
                missingItems.Add(new MissingScoreItem
                {
                    itemId = rule.itemId,
                    itemName =
                        item != null ? item.itemName : rule.itemId,
                    recommendedMin = rule.recommendedMin,
                    selectedQuantity = quantity,
                    severity = "critical"
                });
            }

            if (quantity > rule.recommendedMax)
            {
                excessItems.Add(new ExcessScoreItem
                {
                    itemId = rule.itemId,
                    itemName =
                        item != null ? item.itemName : rule.itemId,
                    quantity = quantity,
                    recommendedMax = rule.recommendedMax
                });
            }
        }

        float totalWeight = CalculateTotalWeight(
            selectedQuantityMap,
            itemMap
        );

        float weightRatio =
            input.maxWeight > 0f
                ? totalWeight / input.maxWeight
                : 0f;

        int weightScore = CalculateWeightScore(
            weightRatio,
            input.maxWeight,
            ruleDatabase.weightEfficiencyRules
        );

        int trapPenalty;
        List<string> trapItemIds;

        CalculateTrapPenalty(
            selectedQuantityMap,
            itemMap,
            ruleDatabase.trapPenaltyRule,
            out trapPenalty,
            out trapItemIds
        );

        SurvivalScoreBreakdown breakdown = BuildBreakdown(
            rawGroupScores,
            ruleDatabase.scoreGroups,
            weightScore
        );

        int subtotal =
            breakdown.water +
            breakdown.food +
            breakdown.medical +
            breakdown.equipment +
            breakdown.warmth +
            breakdown.weightEfficiency;

        int totalScore = Mathf.Clamp(
            subtotal - trapPenalty,
            0,
            100
        );

        return new SurvivalScoreResult
        {
            totalScore = totalScore,
            grade = DetermineGrade(
                totalScore,
                ruleDatabase.gradeRules
            ),
            scores = breakdown,

            totalWeight = RoundToTwoDecimals(totalWeight),
            maxWeight = RoundToTwoDecimals(input.maxWeight),
            weightRatio = RoundToTwoDecimals(weightRatio),

            trapPenalty = trapPenalty,
            trapItemIds = trapItemIds,

            missingItems = missingItems,
            excessItems = excessItems,
            requirementWarnings = requirementWarnings
        };
    }

    private static void ValidateInput(
        SurvivalScoreInput input,
        SurvivalItemDatabase itemDatabase,
        SurvivalScoreRuleDatabase ruleDatabase)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (itemDatabase == null || itemDatabase.items == null)
        {
            throw new ArgumentException(
                "아이템 데이터베이스가 비어 있습니다."
            );
        }

        if (ruleDatabase == null || ruleDatabase.itemRules == null)
        {
            throw new ArgumentException(
                "점수 규칙 데이터베이스가 비어 있습니다."
            );
        }

        if (input.selectedItems == null)
        {
            input.selectedItems = new List<ScoreSelectedItem>();
        }
    }

    private static Dictionary<string, SurvivalItemData> BuildItemMap(
        List<SurvivalItemData> items)
    {
        Dictionary<string, SurvivalItemData> map =
            new Dictionary<string, SurvivalItemData>();

        foreach (SurvivalItemData item in items)
        {
            if (item == null ||
                string.IsNullOrWhiteSpace(item.itemId))
            {
                continue;
            }

            if (!map.ContainsKey(item.itemId))
            {
                map.Add(item.itemId, item);
            }
        }

        return map;
    }

    private static Dictionary<string, int> BuildSelectedQuantityMap(
        List<ScoreSelectedItem> selectedItems)
    {
        Dictionary<string, int> map =
            new Dictionary<string, int>();

        foreach (ScoreSelectedItem selected in selectedItems)
        {
            if (selected == null ||
                string.IsNullOrWhiteSpace(selected.itemId) ||
                selected.quantity <= 0)
            {
                continue;
            }

            if (!map.ContainsKey(selected.itemId))
            {
                map.Add(selected.itemId, 0);
            }

            map[selected.itemId] += selected.quantity;
        }

        return map;
    }

    private static HashSet<string> BuildAvailableItemSet(
        List<string> availableItemIds)
    {
        if (availableItemIds == null ||
            availableItemIds.Count == 0)
        {
            return null;
        }

        return new HashSet<string>(availableItemIds);
    }

    private static bool IsAvailable(
        string itemId,
        HashSet<string> availableItemSet)
    {
        return availableItemSet == null ||
               availableItemSet.Contains(itemId);
    }

    private static int GetQuantity(
        Dictionary<string, int> quantityMap,
        string itemId)
    {
        int quantity;

        return quantityMap.TryGetValue(itemId, out quantity)
            ? quantity
            : 0;
    }

    private static Dictionary<string, float> CreateGroupScoreMap(
        List<ScoreGroupRule> groupRules)
    {
        Dictionary<string, float> map =
            new Dictionary<string, float>();

        if (groupRules == null)
        {
            return map;
        }

        foreach (ScoreGroupRule group in groupRules)
        {
            if (!map.ContainsKey(group.scoreGroup))
            {
                map.Add(group.scoreGroup, 0f);
            }
        }

        return map;
    }

    private static void AddGroupScore(
        Dictionary<string, float> scoreMap,
        string scoreGroup,
        float score)
    {
        if (!scoreMap.ContainsKey(scoreGroup))
        {
            scoreMap.Add(scoreGroup, 0f);
        }

        scoreMap[scoreGroup] += score;
    }

    private static bool HasAllRequirements(
        Dictionary<string, int> selectedQuantityMap,
        List<string> requiredItemIds)
    {
        if (requiredItemIds == null ||
            requiredItemIds.Count == 0)
        {
            return true;
        }

        foreach (string requiredItemId in requiredItemIds)
        {
            if (GetQuantity(
                    selectedQuantityMap,
                    requiredItemId
                ) <= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static RequirementWarning CreateRequirementWarning(
        ItemScoreRule rule,
        SurvivalItemData item,
        Dictionary<string, int> selectedQuantityMap)
    {
        List<string> missingIds = new List<string>();

        if (rule.requiredItemIds != null)
        {
            foreach (string requiredId in rule.requiredItemIds)
            {
                if (GetQuantity(
                        selectedQuantityMap,
                        requiredId
                    ) <= 0)
                {
                    missingIds.Add(requiredId);
                }
            }
        }

        return new RequirementWarning
        {
            itemId = rule.itemId,
            itemName = item.itemName,
            missingRequiredItemIds = missingIds
        };
    }

    private static int GetDisasterBonus(
        List<DisasterBonusRule> bonuses,
        string disaster)
    {
        if (bonuses == null ||
            string.IsNullOrWhiteSpace(disaster))
        {
            return 0;
        }

        int totalBonus = 0;

        foreach (DisasterBonusRule bonus in bonuses)
        {
            if (bonus != null &&
                string.Equals(
                    bonus.disaster,
                    disaster,
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                totalBonus += bonus.bonus;
            }
        }

        return totalBonus;
    }

    private static float CalculateTotalWeight(
        Dictionary<string, int> selectedQuantityMap,
        Dictionary<string, SurvivalItemData> itemMap)
    {
        float totalWeight = 0f;

        foreach (
            KeyValuePair<string, int> selected
            in selectedQuantityMap
        )
        {
            SurvivalItemData item;

            if (!itemMap.TryGetValue(selected.Key, out item))
            {
                Debug.LogWarning(
                    $"[SurvivalScoreCalculator] 알 수 없는 itemId: " +
                    $"{selected.Key}"
                );
                continue;
            }

            totalWeight += item.weight * selected.Value;
        }

        return totalWeight;
    }

    private static int CalculateWeightScore(
        float weightRatio,
        float maxWeight,
        List<WeightEfficiencyRule> rules)
    {
        if (maxWeight <= 0f ||
            rules == null ||
            rules.Count == 0)
        {
            return 0;
        }

        List<WeightEfficiencyRule> sortedRules =
            new List<WeightEfficiencyRule>(rules);

        sortedRules.Sort(
            (a, b) => a.maxRatio.CompareTo(b.maxRatio)
        );

        foreach (WeightEfficiencyRule rule in sortedRules)
        {
            if (weightRatio <= rule.maxRatio)
            {
                return rule.score;
            }
        }

        return 0;
    }

    private static void CalculateTrapPenalty(
        Dictionary<string, int> selectedQuantityMap,
        Dictionary<string, SurvivalItemData> itemMap,
        TrapPenaltyRule rule,
        out int penalty,
        out List<string> trapItemIds)
    {
        penalty = 0;
        trapItemIds = new List<string>();

        if (rule == null)
        {
            return;
        }

        foreach (
            KeyValuePair<string, int> selected
            in selectedQuantityMap
        )
        {
            SurvivalItemData item;

            if (!itemMap.TryGetValue(selected.Key, out item) ||
                !item.isTrap)
            {
                continue;
            }

            trapItemIds.Add(item.itemId);

            penalty +=
                rule.penaltyPerItem * selected.Value;

            if (item.weight >= rule.heavyItemWeightThreshold)
            {
                penalty +=
                    rule.heavyItemAdditionalPenalty *
                    selected.Value;
            }
        }

        penalty = Mathf.Min(penalty, rule.maxPenalty);
    }

    private static SurvivalScoreBreakdown BuildBreakdown(
        Dictionary<string, float> rawScores,
        List<ScoreGroupRule> groupRules,
        int weightScore)
    {
        return new SurvivalScoreBreakdown
        {
            water = GetCappedGroupScore(
                "Water",
                rawScores,
                groupRules
            ),
            food = GetCappedGroupScore(
                "Food",
                rawScores,
                groupRules
            ),
            medical = GetCappedGroupScore(
                "Medical",
                rawScores,
                groupRules
            ),
            equipment = GetCappedGroupScore(
                "Equipment",
                rawScores,
                groupRules
            ),
            warmth = GetCappedGroupScore(
                "Warmth",
                rawScores,
                groupRules
            ),
            weightEfficiency = weightScore
        };
    }

    private static int GetCappedGroupScore(
        string groupName,
        Dictionary<string, float> rawScores,
        List<ScoreGroupRule> groupRules)
    {
        float rawScore = 0f;
        rawScores.TryGetValue(groupName, out rawScore);

        int maxScore = 0;

        if (groupRules != null)
        {
            foreach (ScoreGroupRule group in groupRules)
            {
                if (group.scoreGroup == groupName)
                {
                    maxScore = group.maxScore;
                    break;
                }
            }
        }

        return Mathf.Clamp(
            Mathf.RoundToInt(rawScore),
            0,
            maxScore
        );
    }

    private static string DetermineGrade(
        int totalScore,
        List<GradeRule> gradeRules)
    {
        if (gradeRules == null ||
            gradeRules.Count == 0)
        {
            return "D";
        }

        List<GradeRule> sortedRules =
            new List<GradeRule>(gradeRules);

        sortedRules.Sort(
            (a, b) => b.minScore.CompareTo(a.minScore)
        );

        foreach (GradeRule rule in sortedRules)
        {
            if (totalScore >= rule.minScore)
            {
                return rule.grade;
            }
        }

        return "D";
    }

    private static float RoundToTwoDecimals(float value)
    {
        return Mathf.Round(value * 100f) / 100f;
    }
}
