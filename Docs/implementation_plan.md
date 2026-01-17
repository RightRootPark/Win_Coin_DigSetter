# [기능 추가] 절전 방지 (Keep Awake) 및 Idle Mining 충돌 방지

## 목표
사용자가 컴퓨터를 사용하지 않을 때 화면 보호기나 절전 모드로 진입하는 것을 방지하기 위해 마우스를 주기적으로 미세하게 움직입니다. 
동시에, 이 "가짜 움직임"이 기존의 **Idle Mining(유휴 시간 감지 후 채굴 시작)** 기능을 방해하지 않아야 합니다.

## 핵심 도전 과제
*   마우스를 프로그램이 움직이면 Windows는 이를 '사용자 활동'으로 인식하여 `System Idle Time`을 0으로 초기화합니다.
*   이로 인해 Idle Mining 타이머(60초)가 계속 리셋되어 채굴이 시작되지 않는 문제가 발생할 수 있습니다.
*   **해결책**: `Virtual Idle Timer` 도입. 프로그램이 스스로 마우스를 움직인 직후에는 타이머 리셋을 무시하고 시간을 누적시킵니다.

## 구현 상세

### 1. 데이터 모델 (AppConfig & ViewModel)
*   **IsKeepAwakeEnabled** (`bool`): 절전 방지 기능 켜기/끄기
*   **KeepAwakeInterval** (`int`): 마우스 움직임 간격 (초). 최소값 5초 강제.
*   `settings.json`에 저장 및 로드.

### 2. 마우스 제어 (Native Methods)
*   `user32.dll`의 `mouse_event` 사용.
*   `MOUSEEVENTF_MOVE` 플래그로 현재 위치에서 +1, -1 픽셀만큼 미세 이동.

### 3. Virtual Idle Logic (MainViewModel.cs)
`IdleTimer_Tick` (1초마다 실행) 로직 수정:

1.  **변수 정의**:
    *   `_lastSystemIdleTime`: 이전 틱의 시스템 유휴 시간.
    *   `_virtualIdleAccumulator`: 누적된 유휴 시간 보정값.
    *   `_justJiggled`: 프로그램이 방금 마우스를 움직였는지 표시하는 플래그.

2.  **로직 흐름**:
    *   현재 시스템 유휴 시간(`sysIdle`) 측정.
    *   **누적 시간 계산**:
        *   만약 `sysIdle` < `_lastSystemIdleTime` (리셋 발생 확인):
            *   `_justJiggled`가 **True**라면? -> "내가 움직인 것" -> `_virtualIdleAccumulator += _lastSystemIdleTime` (시간 보존).
            *   `_justJiggled`가 **False**라면? -> "사용자가 움직인 것" -> `_virtualIdleAccumulator = 0` (진짜 초기화).
    *   `CurrentIdleSeconds = sysIdle + _virtualIdleAccumulator` (UI 및 채굴 트리거용).
    *   **채굴기 Start/Stop 판단** (기존 로직 수행).
    *   **Keep Awake 수행**:
        *   `KeepAwakeInterval` 시간이 지났는지 확인 (`CurrentIdleSeconds % Interval == 0` 등).
        *   조건 만족 시:
            *   `_justJiggled = true` 설정.
            *   마우스 미세 이동 함수 호출.
        *   아니라면:
            *   `_justJiggled = false` (다음 틱을 위해 해제).

### 4. UI 변경
*   **Settings 탭**:
    *   [체크박스] "Enable Keep Awake (Prevent Sleep)"
    *   [텍스트박스] "Interval (sec)" (숫자만 입력 가능, < 5 입력 시 5로 보정)

## 검증 계획
1.  Interval을 10초로 설정.
2.  마우스를 가만히 둠.
3.  **관찰**:
    *   10초마다 마우스가 살짝 움직이는지 확인.
    *   그럼에도 불구하고 Dashboard의 Idle 게이지가 리셋되지 않고 계속 차오르는지 확인.
    *   60초 후 채굴이 정상적으로 시작되는지 확인.
4.  직접 마우스를 건드렸을 때 즉시 게이지가 0이 되고 채굴이 멈추는지 확인.
