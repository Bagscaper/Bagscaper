using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// BAGSCAPE 백엔드 API와 통신하는 클라이언트입니다.
///
/// GameManager는 API를 직접 호출하지 않고 이벤트만 발생시킵니다.
/// 이 클래스가 그 이벤트를 구독해서 실제 HTTP 요청을 보내고,
/// 응답이 오면 GameManager의 Apply* 메서드를 호출해 상태를 반영합니다.
///
/// 씬에 이 컴포넌트를 하나 배치하고 Base Url을 채워주세요.
/// </summary>
public class GameApiClient : MonoBehaviour
{
    [Header("서버 설정")]
    [Tooltip("예: https://api.bagscape.com (마지막에 슬래시(/) 없이)")]
    [SerializeField] private string baseUrl = "";

    [Header("타임아웃 (초)")]
    [SerializeField] private int defaultTimeoutSeconds = 10;
    [Tooltip("/game/result는 AI 연산 때문에 최대 10초까지 지연될 수 있어 넉넉히 잡습니다.")]
    [SerializeField] private int resultTimeoutSeconds = 15;

    private GameManager gameManager;

    private void OnEnable()
    {
        gameManager = GameManager.Instance;

        if (gameManager == null)
        {
            Debug.LogError("[GameApiClient] GameManager.Instance가 없습니다. 씬 초기화 순서를 확인하세요.");
            return;
        }

        gameManager.GameStartRequested += HandleGameStartRequested;
        gameManager.BagLogRequested += HandleBagLogRequested;
        gameManager.GameResultRequested += HandleGameResultRequested;
    }

    private void OnDisable()
    {
        if (gameManager == null)
        {
            return;
        }

        gameManager.GameStartRequested -= HandleGameStartRequested;
        gameManager.BagLogRequested -= HandleBagLogRequested;
        gameManager.GameResultRequested -= HandleGameResultRequested;
    }

    // ------------------------------------------------------------------
    // 3.1 게임 시작
    // ------------------------------------------------------------------

    private void HandleGameStartRequested(GameStartRequest request)
    {
        StartCoroutine(PostGameStart(request));
    }

    private IEnumerator PostGameStart(GameStartRequest request)
    {
        string json = JsonUtility.ToJson(request);

        using (UnityWebRequest www = BuildPostRequest("/game/start", json, defaultTimeoutSeconds))
        {
            yield return www.SendWebRequest();

            if (!IsSuccess(www))
            {
                Debug.LogError(
                    $"[GameApiClient] /game/start 실패: {www.error}\n{SafeBody(www)}"
                );
                yield break;
            }

            GameStartResponse response =
                JsonUtility.FromJson<GameStartResponse>(www.downloadHandler.text);

            if (response == null || string.IsNullOrWhiteSpace(response.session_id))
            {
                Debug.LogError("[GameApiClient] /game/start 응답 파싱 실패 또는 session_id 없음.");
                yield break;
            }

            gameManager.ApplyStartSession(
                response.session_id,
                response.reference_bmr_kcal_day,
                response.required_water_72h_ml,
                response.max_carry_weight_kg,
                response.expires_at
            );
        }
    }

    // ------------------------------------------------------------------
    // 3.2 가방 액션 로그
    // ------------------------------------------------------------------

    private void HandleBagLogRequested(GameLogRequest request)
    {
        StartCoroutine(PostGameLog(request));
    }

    private IEnumerator PostGameLog(GameLogRequest request)
    {
        string json = JsonUtility.ToJson(request);

        using (UnityWebRequest www = BuildPostRequest("/game/log", json, defaultTimeoutSeconds))
        {
            yield return www.SendWebRequest();

            if (!IsSuccess(www))
            {
                // action_id는 재시도 시 동일하게 유지해야 하므로,
                // 재시도 로직이 필요하면 이 지점에서 request를 그대로 다시 PostGameLog에 넘기면 됩니다.
                Debug.LogError(
                    $"[GameApiClient] /game/log 실패 (action_id={request.action_id}): " +
                    $"{www.error}\n{SafeBody(www)}"
                );
                yield break;
            }

            GameLogResponse response =
                JsonUtility.FromJson<GameLogResponse>(www.downloadHandler.text);

            if (response == null)
            {
                Debug.LogError("[GameApiClient] /game/log 응답 파싱 실패.");
                yield break;
            }

            // applied=false는 에러가 아니라 "이미 있는걸 또 넣거나 없는걸 뺀 경우"입니다.
            // items(아이템별 개수)는 이 경우에도 서버 기준 값이므로 그대로 동기화합니다.
            gameManager.ApplyServerBagState(
                response.applied,
                response.item_count,
                response.current_weight_grams
            );
        }
    }

    // ------------------------------------------------------------------
    // 3.3 최종 결과
    // ------------------------------------------------------------------

    private void HandleGameResultRequested(string sessionId)
    {
        StartCoroutine(PostGameResult(sessionId));
    }

    private IEnumerator PostGameResult(string sessionId)
    {
        var request = new GameResultRequest { session_id = sessionId };
        string json = JsonUtility.ToJson(request);

        using (UnityWebRequest www = BuildPostRequest("/game/result", json, resultTimeoutSeconds))
        {
            yield return www.SendWebRequest();

            if (!IsSuccess(www))
            {
                Debug.LogError(
                    $"[GameApiClient] /game/result 실패: {www.error}\n{SafeBody(www)}"
                );
                yield break;
            }

            GameResultResponse response =
                JsonUtility.FromJson<GameResultResponse>(www.downloadHandler.text);

            if (response == null)
            {
                Debug.LogError("[GameApiClient] /game/result 응답 파싱 실패.");
                yield break;
            }

            gameManager.ApplyFinalResult(
                response.survival_type,
                response.evaluation_narrative,
                response.survival_time_hours
            );
        }
    }

    // ------------------------------------------------------------------
    // 공통 헬퍼
    // ------------------------------------------------------------------

    private UnityWebRequest BuildPostRequest(string path, string json, int timeoutSeconds)
    {
        var www = new UnityWebRequest(baseUrl + path, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.timeout = timeoutSeconds;
        return www;
    }

    private static bool IsSuccess(UnityWebRequest www)
    {
#if UNITY_2020_1_OR_NEWER
        return www.result == UnityWebRequest.Result.Success;
#else
        return !www.isNetworkError && !www.isHttpError;
#endif
    }

    private static string SafeBody(UnityWebRequest www)
    {
        return www.downloadHandler != null ? www.downloadHandler.text : string.Empty;
    }
}