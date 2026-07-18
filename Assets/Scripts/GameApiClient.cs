using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class GameApiClient : MonoBehaviour
{
    [Header("서버 설정")]
    [Tooltip("예: https://api.bagscape.com (마지막 슬래시 없이)")]
    [SerializeField] private string baseUrl = "";

    [Header("타임아웃")]
    [SerializeField] private int defaultTimeoutSeconds = 10;
    [SerializeField] private int resultTimeoutSeconds = 15;

    private GameManager gameManager;
    private bool subscribed;

    private void Start()
    {
        gameManager = GameManager.Instance;

        if (gameManager == null)
        {
            Debug.LogError("[GameApiClient] GameManager.Instance가 없습니다.");
            return;
        }

        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (subscribed || gameManager == null)
        {
            return;
        }

        gameManager.GameStartRequested += HandleGameStartRequested;
        gameManager.BagLogRequested += HandleBagLogRequested;
        gameManager.GameResultRequested += HandleGameResultRequested;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || gameManager == null)
        {
            return;
        }

        gameManager.GameStartRequested -= HandleGameStartRequested;
        gameManager.BagLogRequested -= HandleBagLogRequested;
        gameManager.GameResultRequested -= HandleGameResultRequested;
        subscribed = false;
    }

    private void HandleGameStartRequested(GameStartRequest request)
    {
        if (!HasBaseUrl())
        {
            gameManager.ApplyStartRequestFailed("Base URL이 비어 있습니다.");
            return;
        }

        StartCoroutine(PostGameStart(request));
    }

    private IEnumerator PostGameStart(GameStartRequest request)
    {
        string json = JsonUtility.ToJson(request);

        using (UnityWebRequest www =
               BuildPostRequest("/game/start", json, defaultTimeoutSeconds))
        {
            yield return www.SendWebRequest();

            if (!IsSuccess(www))
            {
                string message = $"{www.error}\n{SafeBody(www)}";
                Debug.LogError($"[GameApiClient] /game/start 실패: {message}");
                gameManager.ApplyStartRequestFailed(message);
                yield break;
            }

            GameStartResponse response =
                JsonUtility.FromJson<GameStartResponse>(www.downloadHandler.text);

            if (response == null || string.IsNullOrWhiteSpace(response.session_id))
            {
                gameManager.ApplyStartRequestFailed("session_id가 없는 응답입니다.");
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

    private void HandleBagLogRequested(GameLogRequest request)
    {
        if (!HasBaseUrl())
        {
            return;
        }

        StartCoroutine(PostGameLog(request));
    }

    private IEnumerator PostGameLog(GameLogRequest request)
    {
        string json = JsonUtility.ToJson(request);

        using (UnityWebRequest www =
               BuildPostRequest("/game/log", json, defaultTimeoutSeconds))
        {
            yield return www.SendWebRequest();

            if (!IsSuccess(www))
            {
                Debug.LogError(
                    $"[GameApiClient] /game/log 실패: {www.error}\n{SafeBody(www)}"
                );
                yield break;
            }

            GameLogResponse response =
                JsonUtility.FromJson<GameLogResponse>(www.downloadHandler.text);

            if (response != null)
            {
                gameManager.ApplyServerBagState(
                    response.applied,
                    response.item_count,
                    response.current_weight_grams
                );
            }
        }
    }

    private void HandleGameResultRequested(string sessionId)
    {
        if (!HasBaseUrl())
        {
            gameManager.ApplyResultRequestFailed("Base URL이 비어 있습니다.");
            return;
        }

        StartCoroutine(PostGameResult(sessionId));
    }

    private IEnumerator PostGameResult(string sessionId)
    {
        GameResultRequest request = new GameResultRequest
        {
            session_id = sessionId
        };

        string json = JsonUtility.ToJson(request);

        using (UnityWebRequest www =
               BuildPostRequest("/game/result", json, resultTimeoutSeconds))
        {
            yield return www.SendWebRequest();

            if (!IsSuccess(www))
            {
                string message = $"{www.error}\n{SafeBody(www)}";
                Debug.LogError($"[GameApiClient] /game/result 실패: {message}");
                gameManager.ApplyResultRequestFailed(message);
                yield break;
            }

            GameResultResponse response =
                JsonUtility.FromJson<GameResultResponse>(www.downloadHandler.text);

            if (response == null)
            {
                gameManager.ApplyResultRequestFailed("결과 응답 파싱에 실패했습니다.");
                yield break;
            }

            string aiComment = !string.IsNullOrWhiteSpace(response.ai_comment)
                ? response.ai_comment
                : response.evaluation_narrative;

            gameManager.ApplyFinalResult(
                response.survival_type,
                aiComment,
                response.survival_time_hours
            );
        }
    }

    private bool HasBaseUrl()
    {
        return !string.IsNullOrWhiteSpace(baseUrl);
    }

    private UnityWebRequest BuildPostRequest(
        string path,
        string json,
        int timeoutSeconds)
    {
        UnityWebRequest www = new UnityWebRequest(baseUrl + path, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.timeout = timeoutSeconds;
        return www;
    }

    private static bool IsSuccess(UnityWebRequest www)
    {
        return www.result == UnityWebRequest.Result.Success;
    }

    private static string SafeBody(UnityWebRequest www)
    {
        return www.downloadHandler != null
            ? www.downloadHandler.text
            : string.Empty;
    }
}
