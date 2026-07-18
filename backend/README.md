# BAGSCAPE Backend 3.0.0

FastAPI 기반 세션형 72시간 생존 게임 백엔드입니다. 상세 규칙과 API 계약은
`backend_plan.md`, `client_api_spec.md`를 참고하세요.

## 3.0 동적 스폰 배포 계약

3.0은 하위 호환되지 않는 릴리스입니다. 서버, `items.json`, `survival_types.json`, Unity 클라이언트를 같은 릴리스 단위로 배포합니다.

- Unity는 물리 객체 생성 시 UUIDv4 `item_instance_id`를 만들고 객체 수명 동안 유지해야 합니다.
- `POST /game/log`에 `item_instance_id`가 필수이므로 구버전 클라이언트는 `422 VALIDATION_ERROR`를 받습니다.
- 한 instance ID를 다른 `item_id`로 재사용하면 `409 ITEM_INSTANCE_CONFLICT`이며 자동 재시도하지 않아야 합니다.
- 세션 저장소가 인메모리이므로 재배포·재시작 시 진행 중인 세션은 소실됩니다.
- readiness 진입 전 두 catalog와 코드 기반 결과 유형 규칙의 `type_code` 참조 무결성을 검사합니다.
- unknown item, instance conflict, inventory limit 거부를 운영 counter로 기록합니다.

## 실행

```powershell
python -m venv venv
.\venv\Scripts\pip install -r requirements.lock
.\venv\Scripts\uvicorn app.main:app --reload --workers 1
```

Gemini 평가를 사용하려면 [Google AI Studio](https://aistudio.google.com/app/apikey)에서 API 키를 만들고
`GEMINI_API_KEY`를 설정합니다. 기본 모델은 `gemini-3.5-flash`이며,
필요하면 `GEMINI_MODEL`로 변경할 수 있습니다. 무료 티어에는 사용량 제한이 있고 입력 데이터가 Google 제품 개선에 사용될 수 있습니다.
키가 없거나 AI 호출에 실패하면 같은 응답 스키마의 결정론적 규칙 기반 평가를 반환합니다.

주요 환경 변수:

| 변수 | 기본값 | 설명 |
|---|---:|---|
| `GEMINI_API_KEY` | 없음 | Google AI Studio에서 발급한 API 키 |
| `GEMINI_MODEL` | `gemini-3.5-flash` | Gemini 모델 ID |
| `GEMINI_TIMEOUT_SECONDS` | `8.0` | 개별 AI 호출 제한 |
| `RESULT_REQUEST_TIMEOUT_SECONDS` | `10.0` | 결과 HTTP 요청 전체 제한 |
| `GEMINI_MAX_ATTEMPTS` | `2` | 최초 호출을 포함한 최대 시도 수 |
| `GEMINI_RETRY_BASE_SECONDS` | `0.25` | 지수 backoff 기준값 |
| `GEMINI_RETRY_MAX_SECONDS` | `1.0` | backoff 상한 |
| `GEMINI_MAX_CONCURRENCY` | `8` | 동시 AI 호출 상한 |
| `SESSION_TTL_SECONDS` | `7200` | 마지막 정상 활동 기준 세션 TTL |
| `LOG_HMAC_SALT` | 프로세스별 임의값 | 세션 식별자를 로그에 연결해야 할 때 사용하는 HMAC salt |

메모리 세션 저장소를 사용하므로 반드시 `--workers 1`로 실행해야 합니다. 프로세스 재시작 시 세션과
결과 캐시는 사라집니다. 모든 응답에는 UUID 형식의 `X-Request-ID`가 포함되며, 유효한 클라이언트
요청 ID는 그대로 이어받습니다.

```powershell
.\venv\Scripts\ruff format --check app tests
.\venv\Scripts\ruff check app tests
.\venv\Scripts\mypy app
.\venv\Scripts\pytest --cov=app --cov-report=term-missing --cov-fail-under=90
```

상태 확인은 `GET /health/live`, `GET /health/ready`를 사용합니다.

프롬프트 연구 근거와 few-shot은 각각 `app/ai/research_facts.json`, `app/ai/few_shots.json`에서
검증 가능한 메타데이터와 함께 관리합니다. 실제 모델을 사용하는 평가는 기본 CI에 포함하지 않습니다.

현재 회귀 스위트는 105개 테스트로 구성되며 `app/` statement coverage 95% 이상을 검증합니다.
