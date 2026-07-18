using System;
using System.Collections.Generic;

/// <summary>
/// BAGSCAPE 백엔드 API 명세서에 맞춘 요청/응답 DTO 모음입니다.
/// 필드명은 JsonUtility 직렬화를 위해 서버 JSON의 스네이크 케이스를 그대로 사용합니다.
/// (참고: API 명세서 2. 열거형 정의 - Gender / AgeGroup / Disaster / Action)
/// </summary>

#region 3.1 게임 시작 (POST /game/start)

[Serializable]
public class GameStartRequest
{
    public string gender;      // "male" | "female" | "other"
    public string age_group;   // "child" | "teen" | "age_20_30" | "age_40_50" | "age_60_plus"
    public string disaster;    // "fire" | "flood" | "typhoon" | "wildfire" | "earthquake" | "heatwave" | "coldwave"
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

#region 3.2 가방 액션 로그 (POST /game/log)

[Serializable]
public class GameLogRequest
{
    public string session_id;
    public string action_id;   // 유니티에서 생성한 UUID (재시도 시 동일 값 유지)
    public string action;      // "INSERT" | "REMOVE"
    public string item_id;
    public string occurred_at; // ISO 8601, UTC 기준
}

/// <summary>
/// 아이템별 개수 항목입니다.
/// [주의] 실제 서버 필드명이 다르면 item_id / quantity 이름만 맞춰서 바꾸면 됩니다.
/// </summary>
[Serializable]
public class GameLogItemQuantity
{
    public string item_id;
    public int quantity;
}

[Serializable]
public class GameLogResponse
{
    public string session_id;
    public string action_id;
    public bool applied;
    public int item_count;
    public int current_weight_grams;

    // 아이템별 개수 (신규) - 서버가 가방 안 물품을 item_id 단위로 내려줍니다.
    public List<GameLogItemQuantity> items;
}

#endregion

#region 3.3 최종 결과 (POST /game/result)

[Serializable]
public class GameResultRequest
{
    public string session_id;
}

[Serializable]
public class GameResultResponse
{
    public string survival_type;
    public string evaluation_narrative;
    public int survival_time_hours;
}

#endregion