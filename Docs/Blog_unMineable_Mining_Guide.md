# unMineable 시작부터 EncryptionMinerControl로 CPU·GPU 자동 채굴하기 (완벽 가이드)

> 💡 **한 줄 요약**: 별도의 회원가입 없이 받고 싶은 코인의 지갑 주소만으로 **unMineable** 채굴을 시작하고, 오픈소스 Windows 통합 제어 프로그램 **EncryptionMinerControl (Win_Coin_DigSetter)**을 이용해 유휴 시간(Idle) 자동 채굴·실시간 모니터링·잔고 확인까지 한 번에 세팅하는 방법을 정리합니다.

---

## 📌 들어가며: unMineable과 EncryptionMinerControl 소개

### unMineable이란?
원래 비트코인이나 이더리움 이후의 알트코인, 혹은 **채굴(PoW)이 불가능한 코인**(예: `USDT`, `SHIB`, `XRP`, `SOL`, `ALGO` 등)은 일반 PC로 직접 캘 수 없습니다.  
**unMineable**은 내 PC의 CPU(RandomX)나 GPU(NexaPow, Autolykos 등) 연산력을 풀에 제공하면, **실제 채굴된 코인을 자동으로 환전해 내가 원하는 코인 지갑으로 정산해 주는 마이닝 풀 서비스**입니다.

### EncryptionMinerControl (`Win_Coin_DigSetter`)이란?
CPU 채굴기(**XMRig**)와 NVIDIA GPU 채굴기(**Rigel**)를 매번 `.bat` 파일로 따로 실행하고 관리하는 번거로움을 없애기 위해 만든 **Windows WPF 기반 통합 채굴 제어기**입니다.

- **프로젝트 GitHub**: https://github.com/RightRootPark/Win_Coin_DigSetter
- **주요 장점**:
  - PC 사양(RAM·NVIDIA GPU 개수 및 VRAM)을 자동 감지해 원클릭 설정 (`AUTO DETECT & CONFIGURE`)
  - 컴퓨터를 일정 시간 쓰지 않을 때만 자동으로 채굴 시작 / 마우스를 움직이면 즉시 정지 (**Idle Mining**)
  - 작업 스케줄러 기반 **관리자 권한 자동 시작** (UAC 팝업 없이 XMRig MSR·GPU 팬 제어 활성화)
  - 대시보드에서 **unMineable 실시간 잔고·출금 진행률·온라인 워커 수** 바로 확인
  - 풀 오류·장시간 0 Share·채굴기 비정상 종료(Crash) 자동 감지 및 복구

---

## 💻 최소 사양 및 준비물

| 구분 | 최소 사양 | 권장 사양 | 비고 |
|---|---|---|---|
| **OS** | Windows 10 (1607+) **64비트** | Windows 10 / 11 64비트 | `win-x64` 단일 실행 파일(.NET 포함) |
| **CPU (XMRig)** | 64비트 AES-NI 지원 듀얼 코어+ | 6코어 이상 (L3 캐시 클수록 유리) | RandomX 기준 스레드당 L3 2MB 권장 |
| **RAM** | **4 GB** (`light` 모드 자동 적용) | **8 GB 이상** (`fast` 모드) | `fast` 모드는 단독 약 2.3GB 점유 |
| **GPU (Rigel)** | **NVIDIA GTX 10 시리즈+ (VRAM 3GB+)** | RTX / GTX 6GB 이상 | AMD/내장 그래픽은 CPU만 자동 활성화 |
| **디스크 / 네트워크** | 여유 500MB+ / `443` 아웃바운드 | 여유 1GB+ / 상시 인터넷 | TLS 암호화 풀 연결(`stratum+ssl://...:443`) |

> ⚠️ **참고**: GPU가 없거나 VRAM이 2GB 이하인 구형 그래픽카드 PC에서도 문제없이 **CPU 단독 채굴**로 사용할 수 있습니다.

---

## 1단계: unMineable 시작하기 (회원가입이 필요할까?)

결론부터 말씀드리면 **unMineable은 별도의 회원가입(이메일/비밀번호 가입) 절차가 없습니다.**  
대신 **채굴 수익을 정산받을 본인 지갑 주소**가 곧 계정이 됩니다.

### 1) 입금받을 지갑 주소 준비하기
1. 업비트·빗썸·바이낸스 같은 거래소 앱 또는 개인 지갑(메타마스크, 팬텀, 페라 월렛 등)에서 **수령을 원하는 코인의 입금 주소**를 복사합니다. (예: `ALGO`, `DOGE`, `TRX`, `USDT` 등)
2. 공식 웹사이트(https://unmineable.com)에 접속해 해당 코인 페이지로 들어가면:
   - **최소 출금 임계치(Payment Threshold)** (예: `10 ALGO`, `30 DOGE` 등)
   - **지원 네트워크(Chain)** (예: `ERC20`, `BEP20`, `TRC20`, `Algorand` 등)를 확인할 수 있습니다.

> ⚠️ **주의사항**: 거래소 입금 주소에 **메모(Memo/Tag)** 입력이 필수인 코인(예: `XRP`, `XLM` 등)이나, 최소 입금액 제한이 있는 거래소 주소를 쓸 때는 unMineable의 최소 출금 임계치가 거래소 최소 입금 기준을 충족하는지 반드시 먼저 확인하세요.

### 2) unMineable 워커 주소 형식 이해하기
채굴기에 입력하는 지갑 주소는 아래 형식을 따릅니다.

```text
코인심볼:내_지갑_주소.워커이름
```

- 예시 (Algorand 수령 시):
  ```text
  ALGO:본인의_ALGO_지갑주소.MyPC_CPU
  ```
- **수수료 할인 팁**: 주소 끝에 `#레퍼럴코드`를 붙이면 풀 수수료가 `1.0%`에서 **`0.75%`로 할인**됩니다.
  ```text
  ALGO:본인의_ALGO_지갑주소.MyPC_CPU#레퍼럴코드
  ```

---

## 2단계: 프로그램 다운로드 및 백신 예외 설정

### 1) 실행 파일 및 마이너 준비
1. 배포 폴더(예: `EncryptionMinerControl_Release`)의 압축을 원하는 폴더에 풉니다.
2. 같은 폴더 안의 `Miners` 하위 폴더에 아래 두 파일이 있는지 확인합니다.
   - `Miners\xmrig.exe` (CPU 채굴기)
   - `Miners\rigel.exe` (NVIDIA GPU 채굴기)
   - *(파일이 없다면 동봉된 `download_miners.ps1` 스크립트를 PowerShell로 실행해 최신 버전을 받을 수 있습니다.)*

### 2) Windows Defender 예외 폴더 등록 (필수)
채굴 프로그램(`xmrig.exe`, `rigel.exe`)은 실제 바이러스가 아니더라도 백신에서 **PUA(잠재적으로 원치 않는 프로그램)**로 오진해 자동 삭제하는 경우가 많습니다.
1. **Windows 보안** → **바이러스 및 위협 방지** → **설정 관리**로 이동합니다.
2. 하단 **제외** → **제외 추가 또는 제거** → **제외 사항 추가** → **폴더**를 선택합니다.
3. 압축을 푼 **`EncryptionMinerControl` 폴더 전체**를 추가합니다.

---

## 3단계: 원클릭 자동 설정 (`AUTO DETECT & CONFIGURE`)

프로그램(`EncryptionMinerControl.exe`)을 처음 실행한 뒤 **Settings** 탭으로 이동합니다.

1. **지갑 주소 입력**:
   - **XMRig Settings**와 **Rigel Settings**의 `Wallet Address` 칸에 `코인:내_지갑_주소` 형식으로 입력합니다. (최초 실행 시 기본값이 비어 있다면 내장 기본값으로 채워지므로 **반드시 본인의 지갑 주소로 변경**하세요!)
   - `Algorithm`과 `Pool URL`은 기본적으로 unMineable 아시아 TLS 풀로 세팅됩니다.
     - **CPU (XMRig)**: `rx` / `stratum+ssl://rx-asia.unmineable.com:443`
     - **GPU (Rigel)**: `nexapow` / `stratum+ssl://nexapow-asia.unmineable.com:443`
2. **`AUTO DETECT & CONFIGURE` 버튼 클릭**:
   이 버튼을 한 번만 누르면 PC 사양을 읽어 아래 항목을 자동으로 구성합니다.
   - `Miners\xmrig.exe`, `Miners\rigel.exe` 경로 자동 탐색
   - **CPU & RAM**: 논리 코어의 `75%` 스레드 비중 계산 + 시스템 RAM이 6GB 미만이면 `--randomx-mode=light`, 6GB 이상이면 `--randomx-mode=fast` 자동 전환
   - **워커명 정규화**: 현재 PC 이름을 읽어 `.내PC이름_CPU`, `.내PC이름_GPU`를 깔끔하게 1회만 부착
   - **NVIDIA GPU 및 VRAM 자동 판별 (멀티 GPU 지원)**:
     - NVIDIA GPU가 없거나 VRAM 3GB 미만(2GB 이하)이면 **Rigel 자동 OFF**
     - 3~4GB GPU(예: GTX 1060 3GB)에는 `--nexapow-small-lut on` 자동 적용 *(미적용 대비 해시 약 3배 상승)*
     - 6GB 이상 GPU에는 `small-lut` 자동 해제 (`big LUT` 활용)
     - 2대 이상 장착된 멀티 GPU 환경에서는 2GB 이하 카드 제외(`-d`), 카드별 `on,off` 매핑, `--multi-device-arg-mode first` 자동 추가

### 세부 옵션 체크박스로 손쉽게 켜고 끄기
각 카드의 `Wallet Address` 아래에는 **체크박스 + 값 입력란** 형태의 주요 옵션 목록이 제공됩니다.
- **XMRig 주요 옵션 (12종)**: `-p`(비밀번호), `--cpu-max-threads-hint`(CPU 사용률 %), `--cpu-priority`, `--randomx-mode`, `--huge-pages-jit`, `Enable MSR Mod`, `--keepalive`, `--pause-on-battery`, `--print-time`, `--log-file` 등
- **Rigel 주요 옵션 (15종)**: `--temp-limit`(예: `tc[80-90]tm[105-115]`), `--fan-control`(예: `100`), `--nexapow-small-lut`, `--multi-device-arg-mode`, `--pl`(전력 제한), 클럭 설정, `-d`(GPU 지정), `--no-strict-ssl`, `--log-file` 등

체크박스를 켜고 값을 수정하면 카드 **맨 밑 `Extra Arguments` 텍스트박스에 실시간 반영**되며, 반대로 맨 밑 텍스트를 직접 고쳐도 위 체크박스가 즉시 동기화됩니다.

3. **`SAVE SETTINGS` 버튼 클릭**:
   - 설정 저장 후 **"변경된 설정을 적용하려면 프로그램을 재시작해 주세요."** 안내 팝업이 뜨면 확인을 누르고 프로그램을 재시작합니다.

---

## 4단계: 채굴 효율과 편의성을 높이는 핵심 팁

### 1) 관리자 권한 실행으로 CPU MSR + GPU 팬 제어 활성화
일반 사용자 권한으로 실행해도 기본 채굴과 온도 안전장치(`--temp-limit`)는 정상 동작합니다. 하지만 아래 두 가지 **효율 극대화 기능**은 Windows 관리자 권한이 필요합니다.
- **XMRig MSR 모드 (`--randomx-wrmsr --randomx-rdmsr`)**: 동일 CPU 사용률에서 RandomX 해시 약 +10~15% 향상
- **Rigel 팬·전력 제어 (`--fan-control`, `--pl`)**: GPU 발열로 인한 쿨다운 중단 방지

**Settings → General Settings**에서 두 가지 중 편한 방법을 사용하세요.
- **`RESTART AS ADMIN` 버튼**: 클릭 한 번으로 관리자 권한 재시작
- **`Run at Windows Startup as Admin (Task Scheduler, no UAC prompt)` 체크**: Windows 작업 스케줄러에 최고 권한으로 등록해, 부팅 시 UAC 팝업 없이 자동으로 관리자 권한 실행

### 2) 유휴 채굴(Idle Mining) & 절전 방지 & 스텔스 모드
- **Enable Idle Mining**: 마우스·키보드 입력이 지정된 시간(초) 동안 없을 때만 채굴을 시작하고, 사용자가 PC를 만지면 1초 내에 즉시 채굴을 멈춥니다.
- **Enable Keep Awake**: PC가 절전 모드(Sleep)로 빠져 채굴이 끊기는 것을 막기 위해 주기적으로 미세하게 마우스를 움직입니다. (`Virtual Idle Timer` 로직으로 마우스를 흔들어도 Idle 채굴 타이머가 초기화되지 않습니다.)
- **Stealth Mode (스텔스 모드)**: `HIDE UI (STEALTH MODE)` 또는 시작 시 스텔스 옵션을 켜면 작은 서비스 창(`System Optimization Service`)으로 위장되며, 진행률 바를 **5초 안에 10회 클릭**하면 원래 화면으로 복구됩니다.

---

## 5단계: 실시간 모니터링, 장애 감지 및 출금 확인

### 1) Dashboard 탭에서 상태와 잔고 한눈에 보기
- **XMRig / Rigel 카드**: 현재 `Running / Stopped` 상태와 함께 **`HealthStatus`**(`Idle`, `Starting`, `Healthy`, `Pool Error`, `No Share`, `Crashed`)를 표시합니다.
  - 풀 연결 오류가 10회 연속 발생하거나 30분 이상 Accepted Share가 없으면 경고 상태로 전환되고 트레이 알림을 띄웁니다.
  - 채굴기 프로세스가 예기치 않게 종료(Crash)되면 즉시 `Crashed`로 감지하고 30초 쿨다운 뒤 유휴 조건에서 자동 재시작합니다.
- **unMineable Balance 카드**: 5분마다(또는 `REFRESH` 버튼 클릭 시) unMineable API를 조회해 **현재 누적 잔고 / 출금 기준(예: `4.26 / 10 ALGO`), 진행률 바, 온라인 워커 수**를 바로 보여줍니다.

### 2) Monitoring 탭에서 로그 확인 및 복사하기
- 콘솔 영역을 마우스로 드래그해 `Ctrl+C` / 우클릭 복사를 하거나, 하단의 **`COPY`** 버튼으로 전체 로그를 클립보드에 복사할 수 있습니다.
- **`CLEAR`**(화면 로그 비우기), **`OPEN LOG`**(`Miners` 로그 폴더 열기) 버튼도 함께 지원합니다.
- *(참고: XMRig 로그는 10MB 초과 시 시작 전 `.1` 백업으로 자동 로테이션되며, Rigel은 5MB 단위로 자동 분할됩니다.)*

### 3) unMineable 웹사이트에서 출금하기
1. https://unmineable.com 에 접속 → 내가 채굴 설정한 **코인** 선택 → 상단 **`Address`** 입력란에 내 지갑 주소를 넣고 조회합니다.
2. **Workers** 탭에서 내 PC 워커들이 온라인인지, 실제 풀 해시레이트가 잡히는지 확인합니다.
3. 누적 잔고가 출금 임계치(100%)에 도달하면:
   - **Auto Pay**가 켜져 있으면 자동으로 내 지갑으로 송금됩니다.
   - 또는 **`Pay now`** 버튼을 눌러 수동으로 즉시 출금할 수 있습니다.

---

## 🔧 자주 묻는 질문 (FAQ & 트러블슈팅)

### Q1. 로그에 `failed to set fan control ... NoPermission` 또는 `MSR: Administrator privileges required`가 뜹니다.
- 프로그램이 일반 사용자 권한으로 실행된 상태입니다. 채굴 자체는 정상 진행되지만(GPU는 기본 자동 팬 곡선대로 동작), 팬 속도 고정과 CPU MSR 최적화를 쓰려면 Settings에서 **`RESTART AS ADMIN`** 버튼을 눌러 주세요.

### Q2. GTX 1060 3GB 같은 3~4GB 그래픽카드에서 `nexapow` 해시가 너무 낮게(2 MH/s대) 나옵니다.
- Settings → Rigel Settings 옵션 목록에서 **`NexaPow Small LUT`** (`--nexapow-small-lut on`)가 체크되어 있는지 확인하거나 **`AUTO DETECT & CONFIGURE`** 를 다시 실행한 뒤 저장·재시작하세요. 3GB 카드에서는 이 옵션 유무에 따라 해시 차이가 약 3배 납니다.

### Q3. 어느 날 갑자기 로그에 `os error 11001` (알려진 호스트가 없습니다)만 반복되며 GPU 채굴이 안 됩니다.
- unMineable은 거래소 지원 상황 등에 따라 일부 알고리즘/코인을 상장 폐지(지원 종료)할 수 있습니다. (예: 과거 KarlsenHash, 2026년 11월 XelisHash 종료)
- 이 경우 Settings → Rigel Settings에서 `Algorithm`과 `Pool URL`을 현재 unMineable이 지원하는 알고리즘(예: `nexapow` / `stratum+ssl://nexapow-asia.unmineable.com:443`)으로 변경하면 바로 복구됩니다.

---

## ⚠️ 주의사항
- 채굴은 CPU/GPU에 지속적인 부하를 주므로 **케이스 통풍과 온도(`--temp-limit`) 관리**에 유의하세요.
- 암호화폐 가격과 네트워크 난이도는 수시로 변동되므로 전기요금과 하드웨어 수명을 고려해 본인의 판단과 책임 아래 사용하시기 바랍니다.
