using System;
using System.Collections.Generic;

#region 아이템 JSON

[Serializable]
public class SurvivalItemDatabase
{
    public int version;
    public string description;
    public List<SurvivalItemData> items;
    public List<string> spawnAreas;
}

[Serializable]
public class SurvivalItemData
{
    public string itemId;
    public string itemName;
    public string category;
    public string categoryLabel;
    public float weight;
    public int importance;
    public string duplicateGroup;
    public List<string> usageContexts;
    public bool isTrap;
    public List<string> allowedSpawnAreas;
}

#endregion

#region 점수 규칙 JSON

[Serializable]
public class SurvivalScoreRuleDatabase
{
    public int version;
    public string description;
    public List<string> supportedDisasters;
    public List<ScoreGroupRule> scoreGroups;
    public List<ItemScoreRule> itemRules;
    public List<WeightEfficiencyRule> weightEfficiencyRules;
    public TrapPenaltyRule trapPenaltyRule;
    public List<GradeRule> gradeRules;
}

[Serializable]
public class ScoreGroupRule
{
    public string scoreGroup;
    public int maxScore;
}

[Serializable]
public class ItemScoreRule
{
    public string itemId;
    public string scoreGroup;
    public int baseScore;
    public int recommendedMin;
    public int recommendedMax;
    public bool essential;

    public List<string> requiredItemIds;

    // JSON에 이 필드가 없으면 자동으로 0이 됩니다.
    public int scoreWhenRequirementsMissing;

    public List<DisasterBonusRule> disasterBonuses;
}

[Serializable]
public class DisasterBonusRule
{
    public string disaster;
    public int bonus;
}

[Serializable]
public class WeightEfficiencyRule
{
    public float maxRatio;
    public int score;
}

[Serializable]
public class TrapPenaltyRule
{
    public int penaltyPerItem;
    public float heavyItemWeightThreshold;
    public int heavyItemAdditionalPenalty;
    public int maxPenalty;
}

[Serializable]
public class GradeRule
{
    public int minScore;
    public string grade;
}

#endregion

#region 점수 계산 입력

[Serializable]
public class SurvivalScoreInput
{
    public string disaster;
    public float maxWeight;
    public List<ScoreSelectedItem> selectedItems;

    // 실제로 해당 판에 등장한 아이템 ID 목록입니다.
    // 비어 있으면 모든 아이템이 등장한 것으로 간주합니다.
    public List<string> availableItemIds;
}

[Serializable]
public class ScoreSelectedItem
{
    public string itemId;
    public int quantity;
}

#endregion

#region 점수 계산 결과

[Serializable]
public class SurvivalScoreResult
{
    public int totalScore;
    public string grade;
    public SurvivalScoreBreakdown scores;

    public float totalWeight;
    public float maxWeight;
    public float weightRatio;

    public int trapPenalty;
    public List<string> trapItemIds;

    public List<MissingScoreItem> missingItems;
    public List<ExcessScoreItem> excessItems;
    public List<RequirementWarning> requirementWarnings;
}

[Serializable]
public class SurvivalScoreBreakdown
{
    public int water;
    public int food;
    public int medical;
    public int equipment;
    public int warmth;
    public int weightEfficiency;
}

[Serializable]
public class MissingScoreItem
{
    public string itemId;
    public string itemName;
    public int recommendedMin;
    public int selectedQuantity;
    public string severity;
}

[Serializable]
public class ExcessScoreItem
{
    public string itemId;
    public string itemName;
    public int quantity;
    public int recommendedMax;
}

[Serializable]
public class RequirementWarning
{
    public string itemId;
    public string itemName;
    public List<string> missingRequiredItemIds;
}

#endregion

#region 게임 진행 및 종료 결과

[Serializable]
public class SelectedItemResult
{
    public string itemId;
    public string itemName;
    public int quantity;
    public float unitWeight;
    public float totalWeight;
    public bool isTrap;
}

[Serializable]
public class GameActionLog
{
    public float elapsedTime;
    public string action;
    public string itemId;
    public string itemName;
    public int quantityAfterAction;
    public float currentWeight;
}

[Serializable]
public class SurvivalGameResult
{
    public string scenarioId;

    public string playerGender;
    public int playerAge;

    public float elapsedTime;
    public float totalWeight;
    public float maxWeight;

    public List<SelectedItemResult> selectedItems;
    public List<GameActionLog> actionLogs;
    public List<string> availableItemIds;

    public SurvivalScoreResult scoreResult;
}

#endregion
