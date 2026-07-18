using System;

#region POST /game/start

[Serializable]
public class GameStartRequest
{
    public string gender;
    public string age_group;
    public string disaster;
}

[Serializable]
public class GameStartResponse
{
    public string session_id;
    public int reference_bmr_kcal_day;
    public int required_water_72h_ml;
    public float max_carry_weight_kg;
    public string expires_at;
}

#endregion

#region POST /game/log

[Serializable]
public class GameLogRequest
{
    public string session_id;
    public string action_id;
    public string action;
    public string item_instance_id;
    public string item_id;
    public string occurred_at;
}

[Serializable]
public class GameLogResponse
{
    public string session_id;
    public string action_id;
    public string item_instance_id;
    public bool applied;
    public int item_count;
    public int current_weight_grams;
}

#endregion

#region POST /game/result

[Serializable]
public class GameResultRequest
{
    public string session_id;
}

[Serializable]
public class GameResultResponse
{
    public string survival_type;

    // 새 API에서 권장하는 AI 코멘트 필드입니다.
    public string ai_comment;

    // 기존 서버와의 하위 호환용 필드입니다.
    public string evaluation_narrative;

    public int survival_time_hours;
}

#endregion
