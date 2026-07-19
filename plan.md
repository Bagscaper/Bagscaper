# Start Scene 개인정보 선택 UI 제작 계획

## 1. 목표와 완료 조건

`Assets/Scenes/Start Scene.unity`에 Meta Quest용 개인정보 선택 UI를 만들고 다음 흐름을 완성한다.

1. 시작 시 성별은 미선택, 나이는 `20~30대`가 선택된 상태로 표시한다.
2. 플레이어가 컨트롤러 Ray/Trigger로 남성 또는 여성 버튼을 선택할 수 있다.
3. 성별 버튼은 눌리는 순간 잠깐 확대된 뒤 선택 이미지로 바뀌며, 두 버튼 중 하나만 선택 상태를 유지한다.
4. 오른쪽 컨트롤러 썸스틱을 위/아래로 한 번 기울일 때마다 나이 선택이 한 단계 이동한다.
5. 성별이 선택되기 전에는 확인 버튼이 동작하지 않고, 선택 후 확인하면 현재 개인정보 Canvas만 꺼진다. 씬 전환은 하지 않는다.
6. 완성된 UI와 새 로직을 프리팹으로 만들어, 이후 merge 후 `Main Scene`을 포함한 다른 씬으로 그대로 옮길 수 있게 한다.
7. 기존 C# 스크립트는 수정하지 않는다. 필요한 데이터 저장과 연동은 새 스크립트/컴포넌트로만 구현한다.

> 초기 나이는 현재 `GameManager`의 기본 설정이 20대이고 별도의 나이 필수 선택 조건이 없다는 점을 따라 `20~30대`로 가정한다. 성별은 반드시 직접 선택해야 한다.

## 2. 현재 프로젝트 분석 결과

### Start Scene

- 현재 `Main Camera`와 `Directional Light`만 있는 사실상 빈 씬이다.
- 아직 Canvas, EventSystem/PointableCanvasModule, XR Rig, 선택 UI 로직이 없다.
- 씬과 메타 파일이 현재 Git 기준 untracked 상태이므로 기존 사용자 작업으로 간주하고 삭제하거나 새 씬으로 덮어쓰지 않는다.

### Main Scene과 XR

- Main Scene에는 루트 `OVRCameraRig`가 있고 그 아래 `OVRComprehensiveInteractionRig` 프리팹이 연결되어 있다.
- 별도 루트 `PointableCanvasModule`에 Meta Interaction SDK 입력 모듈과 Unity `EventSystem`이 함께 있다.
- `WorldCanvas`는 World Space Canvas와 `GraphicRaycaster`를 사용한다.
- Start Scene에서 컨트롤러 Ray로 uGUI Button을 누르려면 Main Scene에서 다음 구성을 같은 설정으로 가져와 검증해야 한다.
  - `OVRCameraRig`
  - 컨트롤러 Ray/UI 상호작용에 필요한 `OVRComprehensiveInteractionRig` 하위 구성
  - `PointableCanvasModule`
- XR Rig를 추가한 뒤 Start Scene의 기존 `Main Camera`는 제거하여 MainCamera 태그, Camera, AudioListener가 중복되지 않게 한다.
- Main Scene에서는 `OVRComprehensiveInteractionRig`가 비활성화된 직렬화 흔적이 있으므로 그대로 복제만 하지 않고, Start Scene에서 실제 controller ray가 활성화되는지 Play Mode/Quest에서 확인한다.

### UI 리소스

- 요청된 리소스는 모두 존재한다.
  - `Assets/UI Component/start/Personal Info.png` — 2460×1437
  - `female-button.png`, `male-button.png` — 220×103
  - `female-button-on.png`, `male-button-on.png` — 804×453
- `Personal Info.png` 자체에 패널 외곽선, 성별 슬롯, 나이 선택 영역, 화살표, 확인 버튼의 기본 디자인이 이미 포함되어 있다.
- 보조 리소스로 `Assets/Srcs/UI/confirm-on.png`, `confirm-off.png`, `age-container.png`, `arrow.png`도 존재한다.
- 요청 경로인 `Assets/UI Component/start`의 PNG는 현재 `Default Texture`, mipmap on 상태다. uGUI `Image`에서 Sprite로 직접 사용하려면 `Sprite (2D and UI)`, Single, mipmap off, alpha transparency on으로 import 설정을 바꾼다.
- 기본/선택 성별 이미지의 원본 캔버스 크기와 여백이 크게 다르므로 하나의 `Image`에서 native size로 단순 교체하지 않는다. 동일한 버튼 부모 아래 Normal/Selected 이미지 레이어를 각각 디자인에 맞게 정렬하고 활성 상태를 전환하여 위치 점프와 비율 왜곡을 막는다.
- 한글 TMP 폰트로 `Pretendard-Regular SDF`와 `Pretendard-ExtraBold SDF`가 이미 존재한다.

### GameManager와 백엔드 연결

- `GameManager`에는 `playerGender`, `playerAgeGroup`이 private serialized string으로 있으나 외부에서 설정하는 API가 없다.
- 게임 시작 API 요청에서 이 두 값을 그대로 `GameStartRequest.gender`, `age_group`에 넣는다.
- Main Scene에는 과거 필드명으로 보이는 `playerAge: 20` 직렬화 데이터가 남아 있으며, 현재 코드 필드인 `playerAgeGroup`과 일치하지 않는다.
- 백엔드가 허용하는 실제 값은 다음과 같다.

| UI 표시 | 저장/API 값 |
| --- | --- |
| 남성 | `male` |
| 여성 | `female` |
| 어린이 | `child` |
| 청소년 | `teen` |
| 20~30대 | `age_20_30` |
| 40~50대 | `age_40_50` |
| 60대+ | `age_60_plus` |

- 기존 GameManager 기본값 `Female`, `20s`는 백엔드 enum과 맞지 않지만, 이번 작업에서는 기존 `GameManager.cs`를 수정하지 않는다.
- 새 `PlayerProfileGameStartRequestBridge`가 `GameManager.GameStartRequested` 이벤트를 먼저 구독하고, 전달받은 `GameStartRequest`의 `gender`, `age_group`만 선택된 API 값으로 덮어쓰는 방식으로 연결한다. `GameStartRequest`가 class이므로 기존 GameManager와 GameApiClient를 수정하지 않고 같은 요청 객체를 보정할 수 있다.
- Bridge에는 `[DefaultExecutionOrder]`를 지정해 `GameApiClient.Start()`보다 먼저 구독하도록 하고, 실제 구독 순서를 Play Mode에서 검증한다.
- Main Scene의 `scenarioId` 대소문자와 과거 직렬화 필드 문제는 기존 코드/씬의 별도 이슈로 기록만 하고 이번 프리팹 작업에서 수정하지 않는다.
- 현재 Start Scene에는 GameManager가 없으므로 선택 UI와 런타임 저장까지만 독립 검증한다. merge 후 Main Scene에 프리팹을 배치할 때 Bridge와 `GameManager.StartGame()` 연결을 검증한다.

## 3. 구현 구조

### 새 스크립트

#### `Assets/Scripts/Start/PlayerProfileSelection.cs`

- 개인정보 UI와 다른 새 컴포넌트 사이에서 선택값을 전달하는 런타임 저장소로 만든다.
- `Gender`와 `AgeGroup`은 오타 방지를 위해 enum 또는 제한된 상수로 관리하고, API 문자열 변환을 한 곳에서 담당한다.
- 저장 항목:
  - 선택 여부
  - 선택된 성별 API 값
  - 선택된 나이 API 값
- `Set(...)`, `TryGet(...)`, `Clear()`처럼 작은 API만 노출한다.
- 영구 저장이 요구되지 않았으므로 `PlayerPrefs`는 사용하지 않는다. 앱 실행 세션 안에서 씬을 넘기기 위한 static 런타임 상태로 유지한다.

#### `Assets/Scripts/Start/StartProfileUIController.cs`

- Start Scene UI의 단일 상태 관리자다.
- Inspector 참조:
  - 남성/여성 Button 및 각 Normal/Selected Image
  - 버튼 애니메이션 대상 RectTransform
  - 이전/현재/다음 나이 TMP Text 3개
  - 확인 Button과 선택 가능/불가능 표시 이미지
  - 끌 대상 `Canvas` 또는 프리팹 UI root GameObject
  - 확인 완료 후 외부 기능을 연결할 `UnityEvent onConfirmed`
- 상태:
  - nullable 성별 선택값
  - 0~4의 나이 인덱스(초기값 2)
  - 확인 중복 처리를 막는 `isConfirmed`
- 책임:
  - 성별 단일 선택 상태 갱신
  - 클릭 피드백 애니메이션
  - 나이 텍스트 3줄 갱신
  - 확인 버튼 interactable 상태 갱신
  - 선택값 저장, Canvas 비활성화, 확인 완료 이벤트 호출

#### `Assets/Scripts/Start/StartProfileInputController.cs`

- 오른쪽 Touch 컨트롤러의 썸스틱 위/아래 입력만 담당한다.
- `OVRInput.RawButton.RThumbstickUp/Down`의 `GetDown` 또는 축 dead-zone + latch 방식으로 한 번 기울일 때 한 단계만 이동하게 한다.
- 스틱을 계속 기울이고 있을 때 프레임마다 연속 이동하지 않으며, 중립으로 돌아온 뒤 다시 기울여야 다음 단계가 이동한다.
- UI Controller의 `SelectPreviousAge()` / `SelectNextAge()`를 호출한다.
- 방향은 화면 의미와 일치시킨다.
  - 위 입력: 더 앞의 항목(예: `20~30대` → `청소년`)
  - 아래 입력: 더 뒤의 항목(예: `20~30대` → `40~50대`)

#### `Assets/Scripts/Start/PlayerProfileGameStartRequestBridge.cs`

- 기존 스크립트를 수정하지 않고 `GameManager.GameStartRequested`에 구독하는 별도 어댑터다.
- 선택값이 존재할 때 이벤트로 전달된 `GameStartRequest.gender`와 `age_group`을 올바른 API 문자열로 변경한다.
- 선택값이 없거나 유효하지 않으면 명확한 오류를 출력하고 요청 객체를 임의의 값으로 바꾸지 않는다.
- `OnEnable/Start`에서 구독하고 `OnDisable`에서 해제하여 프리팹 활성/비활성 반복 시 중복 구독을 막는다.
- merge 후 Main Scene에서 프리팹을 사용할 때만 활성화한다. 현재 Start Scene 단독 테스트에서는 GameManager가 없어도 오류 없이 대기해야 한다.

### 기존 스크립트 불변 원칙

- `GameManager.cs`, `GameApiClient.cs`, `BagscapeInputController.cs`, `BagscapeUIController.cs`를 포함한 기존 `.cs` 파일은 편집하지 않는다.
- 기존 로직에 필요한 호출은 이미 공개된 `GameManager.StartGame()`과 `GameManager.GameStartRequested` 이벤트만 사용한다.
- 기존 스크립트의 private 필드에 Reflection으로 접근하지 않는다.
- 기존 코드 변경 없이는 해결할 수 없는 문제는 이번 범위에서 억지로 우회하지 않고 별도 리스크로 문서화한다.

## 4. 프리팹 구조와 Start Scene 배치

최종 산출물은 `Assets/Prefabs/UI/PersonalInfoSelection.prefab`으로 만든다. Start Scene에는 이 프리팹 인스턴스를 배치해 제작·검증하고, 프리팹 내부 오브젝트를 씬 전용 참조에 직접 묶지 않는다.

권장 Hierarchy:

```text
Start Scene
├─ OVRCameraRig
│  └─ OVRComprehensiveInteractionRig (controller UI ray 활성 구성)
├─ PointableCanvasModule
├─ Directional Light (필요한 경우만 유지)
└─ PersonalInfoSelection (Prefab Instance)
   ├─ StartProfileUIController
   ├─ StartProfileInputController
   ├─ PlayerProfileGameStartRequestBridge (Main Scene 연동 시 사용)
   └─ PersonalInfoCanvas (World Space)
      └─ PersonalInfoRoot
         ├─ Background (Personal Info.png)
         ├─ GenderGroup
         │  ├─ MaleButton
         │  │  ├─ NormalImage (male-button.png)
         │  │  └─ SelectedImage (male-button-on.png)
         │  └─ FemaleButton
         │     ├─ NormalImage (female-button.png)
         │     └─ SelectedImage (female-button-on.png)
         ├─ AgeSelector
         │  ├─ PreviousAgeText
         │  ├─ SelectedAgeText
         │  └─ NextAgeText
         └─ ConfirmButton
            ├─ DisabledVisual
            └─ EnabledVisual
```

- `StartProfileUIController`, `StartProfileInputController`, `PlayerProfileGameStartRequestBridge` 컴포넌트는 프리팹 root 또는 그 하위에 포함하고, static `PlayerProfileSelection`은 새 스크립트로 참조한다.
- 프리팹은 Canvas, UI hierarchy, UI 제어 스크립트, 버튼/TMP 참조를 자체 포함한다.
- XR Rig와 `PointableCanvasModule`은 씬 공용 인프라이므로 프리팹에 중복 포함하지 않는다. 프리팹을 옮길 대상 씬에 해당 인프라가 없을 때만 별도로 추가한다.
- 향후 Main Scene으로 옮길 때는 프리팹 인스턴스만 배치하고, `onConfirmed`를 Inspector에서 기존 `GameManager.StartGame()`에 선택적으로 연결한다. 현재 Start Scene에서는 이 이벤트를 비워두어도 Canvas 종료까지 정상 동작해야 한다.
- Canvas는 Main Scene의 UI와 같은 World Space/Meta pointable 패턴을 사용한다.
- Canvas 해상도는 원본 아트보드 비율 2460×1437을 기준으로 하고, 물리 크기와 카메라 앞 거리만 VR 가독성에 맞춰 조절한다.
- Background가 레이캐스트를 가로채지 않도록 `Raycast Target`을 끈다.
- 실제 클릭 영역은 이미지의 투명 픽셀이나 glow 크기가 아니라 Personal Info의 성별 슬롯/확인 슬롯에 맞춘 별도 Button RectTransform으로 둔다.
- Normal/Selected 장식 이미지의 `Raycast Target`도 끄고 부모 Button만 입력을 받는다.
- 확인 버튼은 성별 미선택 시 `interactable = false`, 선택 후 `true`로 전환한다. 가능하면 기존 `confirm-off/on` 리소스를 겹쳐 시각적으로도 상태를 구분하되, 원본 Background와 중복되어 보이지 않도록 실제 Game View/헤드셋에서 정렬한다.

## 5. 세부 상호작용 규칙

### 성별 선택

1. 초기에는 두 Selected Image를 모두 끄고 두 Normal Image를 켠다.
2. 버튼 클릭 시 해당 버튼 부모 RectTransform을 약 `1.04~1.08`배로 0.08~0.12초 확대한다.
3. 다시 원래 크기로 돌아오는 시점에 선택 상태를 반영한다.
4. 새 버튼을 선택하면 이전 버튼은 즉시 Normal 상태로 복귀하고 새 버튼만 Selected 상태가 된다.
5. 이미 선택된 버튼을 다시 눌러도 선택 해제하지 않는다. 클릭 피드백만 재생하고 한 개 선택 상태를 유지한다.
6. 애니메이션 중 빠른 교차 클릭에도 최종 클릭한 버튼 하나만 선택되도록 기존 코루틴/트윈을 취소하고 상태를 재적용한다.
7. 외부 tween 패키지를 추가하지 않고 Coroutine과 `unscaledDeltaTime`으로 구현한다.

### 나이 선택

- 목록 순서는 고정한다: `어린이`, `청소년`, `20~30대`, `40~50대`, `60대+`.
- 선택 텍스트:
  - 중앙 위치
  - `#00FFD4`
  - `Pretendard-ExtraBold SDF` 또는 TMP bold style
- 비선택 텍스트:
  - 선택 항목 바로 위/아래의 한 항목만 표시
  - `#FFFFFF`
  - `Pretendard-Regular SDF`, bold off
- 경계에서는 순환하지 않는다.
  - `어린이`: 위 텍스트 숨김, 아래에 `청소년`
  - `60대+`: 위에 `40~50대`, 아래 텍스트 숨김
- 숨기는 텍스트는 빈 문자열과 비활성화 중 한 방식을 일관되게 사용하고, 레이아웃이 흔들리지 않게 고정 RectTransform을 유지한다.

### 확인과 Canvas 종료

- `OnConfirm()`은 성별 미선택과 이미 확인 처리된 상황을 guard한다.
- 정상 확인 시 UI 표시 문자열이 아닌 API 값으로 `PlayerProfileSelection`에 저장한다.
- 저장이 성공하면 먼저 `PersonalInfoCanvas.gameObject.SetActive(false)`로 Canvas 전체를 끄고, 이후 `onConfirmed`를 한 번 호출한다.
- `SceneManager.LoadScene`은 사용하지 않으며 현재 씬은 그대로 유지한다.
- Canvas가 꺼진 뒤에도 static 선택 데이터는 유지되어 Bridge 또는 이후 새 컴포넌트가 읽을 수 있다.
- merge 후 Main Scene에서는 `onConfirmed`에 기존 public `GameManager.StartGame()`을 연결할 수 있지만, 이 연결은 프리팹 자체에 씬 오브젝트 참조로 저장하지 않고 각 씬의 프리팹 인스턴스에서 설정한다.

## 6. 프리팹과 에셋 설정

1. `Assets/UI Component/start`에서 실제 사용하는 PNG의 TextureImporter를 UI Sprite로 변경한다.
2. 완성된 Canvas와 새 컴포넌트를 `Assets/Prefabs/UI/PersonalInfoSelection.prefab`으로 저장한다.
3. 프리팹 내부의 Button, TMP, Image, Canvas 참조가 모두 self-contained인지 확인한다.
4. XR Rig, EventSystem, GameManager 같은 씬 오브젝트 참조는 프리팹 asset에 직접 저장하지 않는다.
5. Start Scene에는 프리팹 인스턴스를 놓고 Prefab Mode와 씬 양쪽에서 missing reference가 없는지 확인한다.
6. Build Settings는 이번 작업에서 변경하지 않는다. 확인 버튼은 씬을 로드하지 않는다.
7. Android/Quest에서도 alpha, 압축, 최대 크기로 인해 네온 glow와 한글 UI가 깨지지 않는지 확인한다.

## 7. 구현 순서

1. 요청 PNG import 설정을 UI Sprite용으로 정리한다.
2. 새 PlayerProfileSelection 저장소와 표시/API 매핑을 구현하고 단위 테스트 가능한 순수 로직으로 분리한다.
3. 새 StartProfileUIController, StartProfileInputController, PlayerProfileGameStartRequestBridge를 구현한다.
4. Main Scene의 XR Rig/PointableCanvasModule 구성을 Start Scene에 복제하고 기존 Main Camera 중복을 제거한다.
5. World Space Canvas와 Personal Info 기반 UI hierarchy를 만들고 원본 디자인에 맞춰 RectTransform을 정렬한 뒤 프리팹화한다.
6. Button onClick, TMP Text, 이미지, 확인 버튼 참조를 Inspector에서 연결한다.
7. 기존 스크립트 파일에 변경이 없는지 Git diff로 확인한다.
8. Start Scene에서 확인 시 Canvas만 꺼지는지 검증한다.
9. 별도 테스트 인스턴스에서 `onConfirmed`와 Bridge의 선택적 GameManager 연동을 확인한다.
10. Editor, 컨트롤러 연결 Play Mode, Quest 빌드 순으로 검증한다.

## 8. 검증 체크리스트

### UI 상태

- [ ] 시작 직후 남성/여성 모두 기본 이미지이고 확인 버튼을 눌러도 이동하지 않는다.
- [ ] 초기 중앙 나이는 `20~30대`, 색은 `#00FFD4`, 굵게 표시된다.
- [ ] 초기 위/아래에는 각각 `청소년`, `40~50대`가 흰색 regular로 표시된다.
- [ ] 성별 버튼 클릭 때 확대→복귀 피드백이 눈에 보인다.
- [ ] 성별 선택 이미지는 항상 하나만 켜진다.
- [ ] 기본/선택 이미지 교체 시 버튼 위치와 내부 프레임이 튀지 않는다.

### 입력

- [ ] 오른쪽 썸스틱 위/아래 한 번 입력에 나이가 정확히 한 단계 이동한다.
- [ ] 스틱을 계속 기울여도 여러 단계가 연속 이동하지 않는다.
- [ ] 첫/마지막 항목에서 범위를 벗어나거나 목록이 순환하지 않는다.
- [ ] Meta controller ray와 trigger로 남성, 여성, 확인 Button을 정확히 클릭할 수 있다.
- [ ] Background/장식 Image가 Button raycast를 가로채지 않는다.

### 데이터와 Canvas 종료

- [ ] 성별 선택 전 확인 버튼은 비활성이다.
- [ ] 성별 선택 후 확인은 씬을 전환하지 않고 개인정보 Canvas만 한 번 비활성화한다.
- [ ] Canvas가 꺼진 뒤 선택한 성별/나이 값이 `PlayerProfileSelection`에 남아 있다.
- [ ] 프리팹을 재배치해도 2×5 성별/나이 조합이 올바른 API 문자열로 저장된다.
- [ ] merge 후 Bridge를 활성화한 테스트에서 `/game/start` JSON에 `male|female` 및 올바른 age_group이 들어간다.
- [ ] `onConfirmed`를 연결한 경우에만 기존 `GameManager.StartGame()`이 호출된다.
- [ ] Start Scene과 프리팹을 옮긴 대상 씬에 Camera/AudioListener/EventSystem 중복 경고가 없다.
- [ ] 프리팹 asset에 Start Scene 전용 object reference가 남아 있지 않다.
- [ ] 기존 `.cs` 파일에는 변경 사항이 없다.

## 9. 주의할 기존 리스크

- Main Scene의 직렬화 데이터와 현재 C# `GameManager` 필드 구성이 어긋나 있지만, 기존 스크립트/메인 씬 수정 금지 원칙에 따라 이번 작업에서는 고치지 않는다.
- `OVRComprehensiveInteractionRig`의 현재 비활성 상태가 의도인지 확인이 필요하다. Start Scene에서는 UI Ray에 필요한 최소 controller interaction만 활성화하여 손 추적/컨트롤러 모델/카메라가 중복 생성되지 않게 한다.
- 서로 다른 크기의 Normal/Selected 이미지와 confirm-on/off 이미지는 native size 교체 시 정렬이 깨질 수 있다. 상태별 시각 레이어를 분리하고 실제 렌더 결과를 기준으로 크기를 맞춘다.
- 이벤트 요청 객체를 보정하는 Bridge는 구독 순서가 중요하다. merge 후 GameApiClient보다 먼저 실행되는지 반드시 로그/API payload로 확인하며, 보장되지 않으면 기존 코드 변경 없이 적용 가능한 별도 새 bootstrap 컴포넌트로 실행 순서를 고정한다.
- Editor의 키보드 대체 입력은 개발 편의를 위해 선택적으로 제공할 수 있지만, 최종 합격 기준은 오른쪽 Quest 컨트롤러 썸스틱과 Ray/Trigger 입력이다.
