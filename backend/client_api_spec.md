# BAGSCAPE Client API Specification

> 문서 버전: 3.0.0  
> 기준 문서: `backend_plan.md` v3.0.0  
> 기준일: 2026-07-19  
> 대상: Unity Client 개발자

## 1. API 흐름

```text
POST /game/start
        ↓ session_id
Unity 물리 객체 생성 → item_instance_id(UUIDv4) 발급
        ↓
POST /game/log 반복
        ↓ 모든 action ACK
POST /game/result
        ↓ 최종 생존 평가
```

- 모든 body는 JSON이고 요청 헤더는 `Content-Type: application/json`이다.
- 모든 응답에는 UUID 형식 `X-Request-ID` 헤더가 있다.
- JSON에 정의되지 않은 필드를 보내면 `422 VALIDATION_ERROR`가 될 수 있다.
- 세션 TTL은 마지막 활동 기준 기본 2시간이다.
- 세션은 서버 메모리에 있으므로 서버 재시작 시 사라질 수 있다.
- 인증, API version prefix, catalog 조회 API는 현재 정의돼 있지 않다.

## 2. 식별자 계약

| 필드 | 발급 주체 | 생성 시점 | 수명과 용도 |
|---|---|---|---|
| `session_id` | Server | `/game/start` | 한 게임 세션 |
| `item_id` | 기획 Static DB | 아이템 종류 정의 시 | `items.json`에 있는 정적 종류 ID |
| `item_instance_id` | Unity | 물리 객체 생성 직후 | 해당 물리 객체의 정체성 |
| `action_id` | Unity | INSERT/REMOVE 조작마다 | HTTP 멱등성 키 |

`item_id`가 같은 생수 객체가 3개라면 `item_id`는 모두 같고 `item_instance_id`는 3개가 모두 달라야 한다. `item_instance_id`와 `action_id`는 서로 대체할 수 없다.

### 2.1 UUID와 시간 형식

| 데이터 | 형식 | 예시 |
|---|---|---|
| UUIDv4 | 하이픈 포함 표준 UUID 문자열 | `d8e2fd73-a12c-4e07-a282-3bc72f473b6d` |
| 일시 | timezone 포함 ISO 8601 | `2026-07-19T12:08:21+09:00` |
| UTC 일시 | ISO 8601, 끝에 `Z` | `2026-07-19T03:08:21Z` |

Unity에서는 `Guid.NewGuid().ToString("D")`와 UTC round-trip datetime을 권장한다.

## 3. 공통 Enum

문자열 대소문자를 그대로 사용한다.

| 필드 | 허용 값 |
|---|---|
| `gender` | `male`, `female`, `other` |
| `age_group` | `child`, `teen`, `age_20_30`, `age_40_50`, `age_60_plus` |
| `disaster` | `fire`, `flood`, `typhoon`, `wildfire`, `earthquake`, `heatwave`, `coldwave` |
| `action` | `INSERT`, `REMOVE` |

## 4. 게임 시작

### `POST /game/start`

Request:

```json
{
  "gender": "female",
  "age_group": "age_20_30",
  "disaster": "wildfire"
}
```

| 필드 | 타입 | 필수 | 설명 |
|---|---|---:|---|
| `gender` | string enum | O | 성별 |
| `age_group` | string enum | O | 나이대 |
| `disaster` | string enum | O | 재난 |

Success: `201 Created`

```json
{
  "session_id": "ed503034-9522-41f9-961c-a429796fcf51",
  "reference_bmr_kcal_day": 1350,
  "required_water_72h_ml": 6930,
  "max_carry_weight_kg": 11.0,
  "expires_at": "2026-07-19T14:00:00Z"
}
```

| 필드 | 타입 | 제약 | 용도 |
|---|---|---|---|
| `session_id` | UUID | 필수 | 이후 모든 API 요청에 사용 |
| `reference_bmr_kcal_day` | integer | `> 0` | 기준 BMR 표시 |
| `required_water_72h_ml` | integer | `> 0` | 게임 기준 필요 수분량 표시 |
| `max_carry_weight_kg` | number | `> 0` | 최대 가방 중량 표시 |
| `expires_at` | datetime | timezone 포함 | 시작 시점 만료 예정 시각 |

`expires_at`은 활동에 따라 서버에서 연장될 수 있다. 클라이언트 타이머보다 `410 SESSION_EXPIRED` 응답을 최종 기준으로 한다.

## 5. Unity 동적 객체 ID 수명주기

### 5.1 생성 규칙

```csharp
public sealed class SpawnedItem : MonoBehaviour
{
    [SerializeField] private string itemId;

    public string ItemId => itemId;
    public string ItemInstanceId { get; private set; }

    private void Awake()
    {
        ItemInstanceId = System.Guid.NewGuid().ToString("D");
    }
}
```

- scene에 새로 생성되거나 clone된 물리 객체마다 `Guid.NewGuid()`를 정확히 한 번 호출한다.
- 객체가 가방에 들어갔다가 다시 나와도 같은 `item_instance_id`를 유지한다.
- 같은 논리 객체를 네트워크 재시도 때문에 다시 생성하지 않는다.
- 객체가 파괴되고 새 객체가 스폰되면 같은 `item_id`라도 새 `item_instance_id`를 발급한다.
- UUID를 빈 문자열, 순번, `GetInstanceID()`, 재사용 가능한 object-pool slot ID로 대체하지 않는다.

Object Pool을 사용한다면 “새 게임 물리 객체로 활성화되는 시점”에 새 UUID를 발급하되, 한 활성 수명 중에는 바꾸지 않는다.

### 5.2 서버 검증 범위

- 서버는 `item_id`가 `items.json`에 존재하는지 확인한다.
- 서버는 `item_instance_id`가 UUIDv4인지 확인한다.
- 서버는 새 instance ID의 사전 등록을 요구하지 않는다.
- instance ID는 한 세션 안에서만 inventory key로 사용된다.

## 6. 실시간 인벤토리 액션

### `POST /game/log`

아이템을 가방에 넣거나 뺄 때마다 호출한다.

Request:

```json
{
  "session_id": "ed503034-9522-41f9-961c-a429796fcf51",
  "action_id": "01e55799-7922-489d-b709-3ca91d1974e2",
  "action": "INSERT",
  "item_instance_id": "d8e2fd73-a12c-4e07-a282-3bc72f473b6d",
  "item_id": "item_water",
  "occurred_at": "2026-07-19T12:08:21+09:00"
}
```

| 필드 | 타입 | 필수 | 설명 |
|---|---|---:|---|
| `session_id` | UUID | O | `/game/start`에서 받은 ID |
| `action_id` | UUID | O | 사용자 조작마다 새로 만드는 멱등 키 |
| `action` | enum | O | `INSERT` 또는 `REMOVE` |
| `item_instance_id` | UUIDv4 | O | 물리 객체가 생성될 때 만든 ID |
| `item_id` | string | O | 서버 정적 catalog와 일치하는 종류 ID |
| `occurred_at` | datetime | O | 실제 조작 시각, timezone 필수 |

`item_id`는 `^[A-Za-z0-9_-]{1,64}$` 형식이어야 한다. 형식이 맞더라도 catalog에 없으면 `422 UNKNOWN_ITEM`이다.

Success: `200 OK`

```json
{
  "session_id": "ed503034-9522-41f9-961c-a429796fcf51",
  "action_id": "01e55799-7922-489d-b709-3ca91d1974e2",
  "item_instance_id": "d8e2fd73-a12c-4e07-a282-3bc72f473b6d",
  "applied": true,
  "item_count": 1,
  "current_weight_grams": 2000
}
```

| 필드 | 타입 | 제약 | 설명 |
|---|---|---|---|
| `session_id` | UUID | 필수 | 요청 세션 ID |
| `action_id` | UUID | 필수 | 요청 액션 ID |
| `item_instance_id` | UUIDv4 | 필수 | 요청 물리 객체 ID echo |
| `applied` | boolean | 필수 | 서버 inventory가 실제 변경됐는지 |
| `item_count` | integer | `0..50` | 현재 instance 개수 |
| `current_weight_grams` | integer | `>= 0` | 모든 instance 중량의 합 |

### 6.1 동일 종류 다중 적재 예시

아래 두 요청은 `item_id`가 같지만 instance ID가 다르므로 둘 다 적용된다.

```json
{
  "action": "INSERT",
  "item_instance_id": "d8e2fd73-a12c-4e07-a282-3bc72f473b6d",
  "item_id": "item_water"
}
```

```json
{
  "action": "INSERT",
  "item_instance_id": "a2e37ebd-d789-4560-95be-14a60121b451",
  "item_id": "item_water"
}
```

두 번째 성공 응답은 `item_count=2`이고, 각 생수가 2kg이면 `current_weight_grams=4000`이다. 첫 번째 UUID만 REMOVE하면 `item_count=1`, `current_weight_grams=2000`이 되며 두 번째 인스턴스는 유지된다.

### 6.2 `applied=false`

다음은 HTTP 오류가 아닌 정상 no-op이다.

- 이미 inventory에 있는 같은 `item_instance_id` + 같은 `item_id`를 다시 INSERT.
- inventory에 없는 `item_instance_id`를 REMOVE.

같은 instance ID를 다른 `item_id`로 보내는 것은 no-op이 아니라 `409 ITEM_INSTANCE_CONFLICT`다. 이는 Unity 객체의 ID/종류 연결이 중간에 바뀐 버그를 뜻한다.

### 6.3 멱등성과 재시도

- 새 INSERT/REMOVE 조작마다 새 `action_id`를 만든다.
- timeout으로 재전송할 때는 원래 `action_id`, `item_instance_id`, `item_id`, action, 시각을 모두 그대로 사용한다.
- 같은 `action_id`와 동일 payload면 최초 응답을 받고 중복 적용되지 않는다.
- 같은 `action_id`에서 어느 필드라도 변경하면 `409 ACTION_ID_CONFLICT`다.
- 같은 객체를 사용자가 다시 조작할 때는 새 `action_id`를 만들고 기존 `item_instance_id`를 유지한다.
- 전송 순서는 `occurred_at`이 아니라 서버 수신·적용 순서이므로 action queue에서 순차 전송한다.

## 7. 최종 결과

### `POST /game/result`

모든 `/game/log` 요청의 성공 ACK를 받은 뒤 호출한다. snapshot이 확정되면 이후 로그는 거부된다.

Request:

```json
{"session_id": "ed503034-9522-41f9-961c-a429796fcf51"}
```

Success: `200 OK`

```json
{
  "survival_type": "Smart Planner Survivor",
  "evaluation_narrative": "중요도가 높은 재난 대응 물품을 여러 카테고리에 고르게 배치했고 가방 중량도 허용 범위에 유지했다. 같은 종류의 물품도 실제 인스턴스 수만큼 반영되어 준비 수준과 선택 행동이 일관되게 평가됐다.",
  "survival_time_hours": 61
}
```

| 필드 | 타입 | 제약 |
|---|---|---|
| `survival_type` | string | 1~80자 |
| `evaluation_narrative` | string | 1~2,000자 |
| `survival_time_hours` | integer | `0..72` |

같은 세션의 결과 요청은 멱등하다. timeout 또는 연결 단절 후 같은 `session_id`로 재요청한다. AI 장애 시에도 서버는 같은 schema의 규칙 기반 폴백을 `200 OK`로 반환한다.

## 8. 오류 처리

### 8.1 도메인 오류

| HTTP | 코드 | 조건 | 클라이언트 처리 |
|---:|---|---|---|
| 404 | `SESSION_NOT_FOUND` | 세션 없음·서버 재시작 | 새 게임 안내 |
| 410 | `SESSION_EXPIRED` | TTL 만료 | 세션 폐기 후 새 게임 |
| 409 | `SESSION_COMPLETED` | 결과 확정 뒤 로그 | 로그 중단, 결과 재조회 |
| 409 | `ACTION_ID_CONFLICT` | action ID payload 변경 | 자동 재시도 중단, 오류 기록 |
| 409 | `ITEM_INSTANCE_CONFLICT` | instance ID와 item ID 연결 변경 | 객체 ID 관리 버그 기록, 자동 재시도 중단 |
| 409 | `INVENTORY_LIMIT` | 50개 초과 INSERT | 추가 취소, 용량 안내 |
| 422 | `UNKNOWN_ITEM` | catalog에 없는 `item_id` | catalog 버전 점검 |
| 422 | `VALIDATION_ERROR` | UUIDv4·필드·enum·시간 형식 오류 | payload 수정, 자동 재시도 금지 |
| 429 | `LOG_LIMIT_REACHED` | 세션 로그 500개 초과 | 로그 중단, 종료 흐름 안내 |
| 504 | `RESULT_TIMEOUT` | 결과 시간 예산 초과 | 같은 session ID로 결과 재시도 |

오류 envelope:

```json
{
  "error": {
    "code": "ITEM_INSTANCE_CONFLICT",
    "message": "Item instance is already associated with another item"
  }
}
```

### 8.2 네트워크 오류

| 요청 | 응답 유실 시 처리 |
|---|---|
| `/game/start` | 새 세션이 중복 생성될 수 있으므로 무제한 자동 재시도 금지 |
| `/game/log` | 원래 payload 전체를 그대로 재시도 |
| `/game/result` | 같은 `session_id`로 재시도 |

## 9. 클라이언트 상태 전이

| 상태 | 진입 | 허용 동작 |
|---|---|---|
| `NOT_STARTED` | 앱 시작·세션 폐기 | `/game/start` |
| `ACTIVE` | start 성공 | 물리 객체 UUID 발급, `/game/log`, `/game/result` |
| `RESULT_LOADING` | result 호출 | 같은 result 재시도, 새 log 금지 |
| `COMPLETED` | result 성공 | 결과 표시·같은 결과 재조회 |
| `INVALID` | 404·410 | 세션 폐기·새 start |

`RESULT_LOADING`에서 요청이 실패해도 서버 snapshot은 이미 확정됐을 수 있다. `ACTIVE`로 되돌려 로그를 추가하지 말고 결과만 재시도한다.

## 10. 연동 체크리스트

- [ ] 서버와 동일한 `item_id` catalog를 Unity 리소스에 반영했다.
- [ ] 물리 객체 생성/clone/object-pool 재활성 시점의 UUIDv4 정책을 구현했다.
- [ ] 한 객체의 활성 수명 중 `item_instance_id`가 바뀌지 않는다.
- [ ] 같은 종류의 서로 다른 객체가 서로 다른 instance ID를 가진다.
- [ ] `action_id`와 `item_instance_id`를 별도 필드·수명으로 관리한다.
- [ ] 재시도 queue가 payload 전체를 그대로 보존한다.
- [ ] action을 순차 전송하고 모두 ACK된 뒤 result를 호출한다.
- [ ] `applied=false`를 정상 no-op으로 처리한다.
- [ ] 응답의 `item_count`, `current_weight_grams`를 UI 기준으로 사용한다.
- [ ] 404, 410, 409, 422, 429, 504 흐름을 구현했다.
- [ ] 구버전 서버/클라이언트와 혼합 배포하지 않는다.

## 11. 현재 API 범위 밖

- 아이템 catalog 조회 및 catalog version API
- 현재 inventory 조회 API
- WebSocket/server push
- 세션 취소, 인증, 결과 이력 저장
- 서버가 Unity spawn instance를 사전 발급하거나 등록하는 API

서버는 `item_instance_id`를 사전 등록하지 않으므로 Unity의 객체 ID 수명주기 구현이 정확해야 한다.

## 12. 변경 이력

| 날짜 | 문서 버전 | 변경 내용 |
|---|---|---|
| 2026-07-18 | 1.0.0 | Unity 전달용 세션 API 계약 최초 작성 |
| 2026-07-19 | 2.1.0 | 공통 오류 envelope, 요청 ID, 결과 timeout·재시도 계약 확정 |
| 2026-07-19 | 3.0.0 | MR 실시간 동적 스폰을 위한 필수 UUIDv4 `item_instance_id`, 동일 종류 다중 적재, 인스턴스 단위 제거, `ITEM_INSTANCE_CONFLICT`, Unity 객체 수명주기 및 재시도 계약 추가 |
