using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// BAGSCAPE API 단독 콘솔 테스트.
/// 다른 GameApiModels.cs / GameManager.cs 없이도 컴파일됩니다.
/// 파일 이름은 반드시 BagscapeApiSmokeTest.cs 여야 합니다.
/// </summary>
public class BagscapeApiSmokeTest : MonoBehaviour
{
    [Serializable]
    private class StartRequest
    {
        public string gender;
        public string age_group;
        public string disaster;
    }

    [Serializable]
    private class StartResponse
    {
        public string session_id;
        public int reference_bmr_kcal_day;
        public int required_water_72h_ml;
        public float max_carry_weight_kg;
        public string expires_at;
    }

    [Serializable]
    private class LogRequest
    {
        public string session_id;
        public string action_id;
        public string action;
        public string item_id;
        public string occurred_at;
    }

    [Serializable]
    private class LogResponse
    {
        public string session_id;
        public string action_id;
        public bool applied;
        public int item_count;
        public int current_weight_grams;
    }

    [Serializable]
    private class ResultRequest
    {
        public string session_id;
    }

    [Serializable]
    private class ResultResponse
    {
        public string survival_type;
        public string evaluation_narrative;
        public int survival_time_hours;
    }

    [Header("서버 설정")]
    [Tooltip("예: http://127.0.0.1:8000")]
    [SerializeField] private string baseUrl = "";

    [Header("시작 요청")]
    [SerializeField] private string gender = "female";
    [SerializeField] private string ageGroup = "age_20_30";
    [SerializeField] private string disaster = "earthquake";

    [Header("로그 테스트 아이템")]
    [Tooltip("백엔드 DB item_id와 정확히 같아야 합니다.")]
    [SerializeField] private string firstItemId = "water";
    [SerializeField] private string secondItemId = "first_aid_kit";

    [Header("실행 설정")]
    [SerializeField] private bool runOnStart;
    [SerializeField] private int normalTimeoutSeconds = 10;
    [SerializeField] private int resultTimeoutSeconds = 20;

    private bool isRunning;

    private void Start()
    {
        if (runOnStart)
        {
            RunTest();
        }
    }

    public void RunTest()
    {
        if (isRunning)
        {
            Debug.LogWarning("[API TEST] 이미 실행 중입니다.");
            return;
        }

        StartCoroutine(TestRoutine());
    }

    [ContextMenu("Run BAGSCAPE API Test")]
    private void RunFromContextMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[API TEST] Play 모드에서 실행하세요.");
            return;
        }

        RunTest();
    }

    private IEnumerator TestRoutine()
    {
        isRunning = true;

        string url = (baseUrl ?? string.Empty).Trim().TrimEnd('/');

        if (string.IsNullOrWhiteSpace(url))
        {
            Debug.LogError("[API TEST] Base Url이 비어 있습니다.");
            isRunning = false;
            yield break;
        }

        Debug.Log("[API TEST] ===== 테스트 시작 =====");

        // 1. /game/start
        var startRequest = new StartRequest
        {
            gender = gender.Trim().ToLowerInvariant(),
            age_group = ageGroup.Trim().ToLowerInvariant(),
            disaster = disaster.Trim().ToLowerInvariant()
        };

        string startBody = null;
        bool startOk = false;

        yield return Post(
            url + "/game/start",
            JsonUtility.ToJson(startRequest),
            normalTimeoutSeconds,
            (ok, body) =>
            {
                startOk = ok;
                startBody = body;
            }
        );

        if (!startOk)
        {
            Finish(false);
            yield break;
        }

        StartResponse startResponse =
            JsonUtility.FromJson<StartResponse>(startBody);

        if (startResponse == null ||
            string.IsNullOrWhiteSpace(startResponse.session_id))
        {
            Debug.LogError(
                "[API TEST] /game/start 응답에 session_id가 없습니다.\n" +
                startBody
            );
            Finish(false);
            yield break;
        }

        string sessionId = startResponse.session_id;

        Debug.Log(
            "[API TEST] ✅ START 성공\n" +
            $"session_id={sessionId}\n" +
            $"max_carry_weight_kg={startResponse.max_carry_weight_kg}"
        );

        // 2. 첫 아이템 INSERT
        bool firstOk = false;

        yield return SendLog(
            url,
            sessionId,
            "INSERT",
            firstItemId,
            ok => firstOk = ok
        );

        if (!firstOk)
        {
            Finish(false);
            yield break;
        }

        // 3. 두 번째 아이템 INSERT
        bool secondOk = false;

        yield return SendLog(
            url,
            sessionId,
            "INSERT",
            secondItemId,
            ok => secondOk = ok
        );

        if (!secondOk)
        {
            Finish(false);
            yield break;
        }

        // 4. 두 번째 아이템 REMOVE
        bool removeOk = false;

        yield return SendLog(
            url,
            sessionId,
            "REMOVE",
            secondItemId,
            ok => removeOk = ok
        );

        if (!removeOk)
        {
            Finish(false);
            yield break;
        }

        // 5. /game/result
        var resultRequest = new ResultRequest
        {
            session_id = sessionId
        };

        string resultBody = null;
        bool resultOk = false;

        yield return Post(
            url + "/game/result",
            JsonUtility.ToJson(resultRequest),
            resultTimeoutSeconds,
            (ok, body) =>
            {
                resultOk = ok;
                resultBody = body;
            }
        );

        if (!resultOk)
        {
            Finish(false);
            yield break;
        }

        ResultResponse resultResponse =
            JsonUtility.FromJson<ResultResponse>(resultBody);

        Debug.Log(
            "[API TEST] ✅ RESULT 성공\n" +
            $"survival_type={resultResponse.survival_type}\n" +
            $"survival_time_hours={resultResponse.survival_time_hours}\n" +
            $"evaluation_narrative={resultResponse.evaluation_narrative}"
        );

        Finish(true);
    }

    private IEnumerator SendLog(
        string url,
        string sessionId,
        string action,
        string itemId,
        Action<bool> completed)
    {
        var requestBody = new LogRequest
        {
            session_id = sessionId,
            action_id = Guid.NewGuid().ToString(),
            action = action,
            item_id = itemId,
            occurred_at =
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        string responseBody = null;
        bool ok = false;

        yield return Post(
            url + "/game/log",
            JsonUtility.ToJson(requestBody),
            normalTimeoutSeconds,
            (success, body) =>
            {
                ok = success;
                responseBody = body;
            }
        );

        if (!ok)
        {
            completed?.Invoke(false);
            yield break;
        }

        LogResponse response =
            JsonUtility.FromJson<LogResponse>(responseBody);

        Debug.Log(
            $"[API TEST] ✅ LOG {action} 성공\n" +
            $"item_id={itemId}\n" +
            $"applied={response.applied}\n" +
            $"item_count={response.item_count}\n" +
            $"current_weight_grams={response.current_weight_grams}"
        );

        completed?.Invoke(true);
    }

    private IEnumerator Post(
        string url,
        string json,
        int timeout,
        Action<bool, string> completed)
    {
        Debug.Log(
            $"[API TEST] ➡ POST {url}\n" +
            $"REQUEST: {json}"
        );

        using (var request =
               new UnityWebRequest(
                   url,
                   UnityWebRequest.kHttpVerbPOST
               ))
        {
            request.uploadHandler =
                new UploadHandlerRaw(
                    Encoding.UTF8.GetBytes(json)
                );

            request.downloadHandler =
                new DownloadHandlerBuffer();

            request.SetRequestHeader(
                "Content-Type",
                "application/json"
            );

            request.timeout = Mathf.Max(1, timeout);

            yield return request.SendWebRequest();

            string body = request.downloadHandler != null
                ? request.downloadHandler.text
                : string.Empty;

            bool success =
                request.result ==
                UnityWebRequest.Result.Success;

            Debug.Log(
                $"[API TEST] ⬅ HTTP {request.responseCode}\n" +
                $"RESULT: {request.result}\n" +
                $"ERROR: {request.error}\n" +
                $"RESPONSE: {body}"
            );

            completed?.Invoke(success, body);
        }
    }

    private void Finish(bool success)
    {
        Debug.Log(
            success
                ? "[API TEST] ===== PASS ====="
                : "[API TEST] ===== FAIL ====="
        );

        isRunning = false;
    }
}