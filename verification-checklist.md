# 개인정보 선택 UI 검증 체크리스트

작성일: 2026-07-19

표기 기준:

- `[x]` 자동 테스트, 컴파일 또는 직렬화 검사로 확인 완료
- `[ ]` Unity Play Mode 또는 Meta Quest 실기 확인 필요

## 자동 검증 완료

- [x] Unity 스크립트 컴파일 성공(컴파일 오류 0건)
- [x] 성별 API 매핑: `남성 → male`, `여성 → female`
- [x] 나이 API 매핑: `어린이 → child`, `청소년 → teen`, `20~30대 → age_20_30`, `40~50대 → age_40_50`, `60대+ → age_60_plus`
- [x] 런타임 선택값 `Set`, `TryGet`, `Clear` 동작
- [x] 프리팹에 `StartProfileUIController`, `StartProfileInputController`, `PlayerProfileGameStartRequestBridge` 포함
- [x] 프리팹의 Button 3개와 UI Controller 필수 Inspector 참조가 모두 연결됨
- [x] 프리팹에 missing script가 없음
- [x] Canvas가 World Space이고 Background 및 장식 Image의 Raycast Target이 꺼져 있음
- [x] Start Scene의 기존 `Main Camera`가 제거됨
- [x] Start Scene에 `OVRCameraRig`, 활성 `OVRComprehensiveInteractionRig`, `PointableCanvasModule`이 있음
- [x] XRI 3.3.2의 `XR Interaction Simulator` 샘플을 가져와 Start Scene에 `EditorOnly` 프리팹으로 배치
- [x] Canvas가 카메라를 향하도록 `PersonalInfoCanvas`의 Y 180° 회전을 제거
- [x] 나이 입력이 OVR 오른쪽 썸스틱과 XRI simulated right controller의 `primary2DAxis`를 모두 지원
- [x] Start Scene에 EventSystem이 한 개만 있음
- [x] `PersonalInfoSelection`이 연결된 프리팹 인스턴스로 배치됨
- [x] 개인정보 UI 프리팹에 GameManager 등 씬 전용 참조가 없음
- [x] 사용 PNG가 Sprite (2D and UI), mipmap off, alpha transparency on으로 설정됨
- [x] Android 텍스처가 최대 4096, ASTC 6×6로 설정됨
- [x] 기존 추적 C# 파일은 변경되지 않았고 새 C# 파일만 추가됨
- [x] Build Settings는 변경되지 않음
- [x] EditMode 테스트 결과: 10 passed, 0 failed, 0 skipped

## Unity Play Mode 확인

- [ ] 시작 직후 남성/여성 모두 기본 이미지이고 확인 버튼이 비활성인지 확인
- [ ] 초기 나이가 중앙 `20~30대`, 위 `청소년`, 아래 `40~50대`인지 확인
- [ ] 중앙 나이는 `#00FFD4` ExtraBold, 위/아래는 흰색 Regular인지 확인
- [ ] 남성 또는 여성 클릭 시 약 0.1초 동안 확대 후 원래 크기로 돌아오는지 확인
- [ ] 빠르게 남성/여성을 교차 클릭해도 마지막으로 누른 하나만 선택되는지 확인
- [ ] 같은 성별을 다시 눌러도 선택이 해제되지 않고 클릭 피드백만 재생되는지 확인
- [ ] Normal/Selected 전환 때 버튼의 중심과 내부 프레임이 튀지 않는지 확인
- [ ] 오른쪽 썸스틱 위 입력 시 이전 나이, 아래 입력 시 다음 나이로 한 단계 이동하는지 확인
- [ ] 썸스틱을 계속 기울여도 한 단계만 이동하고 중립 복귀 후 다시 입력해야 이동하는지 확인
- [ ] `어린이`와 `60대+` 경계에서 순환하거나 범위를 벗어나지 않는지 확인
- [ ] 성별 선택 후 확인 버튼이 활성화되는지 확인
- [ ] 확인 시 씬 전환 없이 `PersonalInfoCanvas`만 한 번 꺼지는지 확인
- [ ] Canvas가 꺼진 뒤 `PlayerProfileSelection.TryGet`으로 선택값을 읽을 수 있는지 확인
- [ ] `onConfirmed`가 비어 있을 때 GameManager 호출 없이 종료되는지 확인

## Meta Quest 실기 확인

- [ ] 오른쪽 컨트롤러 Ray와 Trigger로 남성, 여성, 확인 버튼을 정확히 클릭할 수 있는지 확인
- [ ] Background와 glow 이미지가 Ray/Trigger 입력을 가로채지 않는지 확인
- [ ] 헤드셋 거리에서 패널과 한글이 선명하고 읽기 쉬운지 확인
- [ ] Android 압축 후 네온 glow, 투명 alpha, 선택 이미지가 깨지지 않는지 확인
- [ ] Camera, AudioListener, EventSystem 중복 경고가 없는지 확인

## Main Scene 병합 후 확인

- [ ] `PersonalInfoSelection` 프리팹 인스턴스만 Main Scene에 배치
- [ ] 프리팹 인스턴스의 `onConfirmed`에 `GameManager.StartGame()`을 연결
- [ ] 프리팹의 `PlayerProfileGameStartRequestBridge`가 활성 상태인지 확인
- [ ] GameApiClient보다 Bridge가 먼저 `GameStartRequested`를 구독하는지 확인
- [ ] 2×5 성별/나이 조합 각각의 `/game/start` JSON에 올바른 `gender`, `age_group`이 들어가는지 확인
- [ ] `onConfirmed`를 연결한 인스턴스에서만 기존 게임 시작 흐름이 호출되는지 확인
- [ ] Main Scene의 기존 `scenarioId` 대소문자 및 과거 직렬화 필드 문제는 별도 이슈로 추적
