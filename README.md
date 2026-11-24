Developed with ❤️ by **RightRootPark** & **Antigravity AI**

# EncryptionMinerControl (Win Coin DigSetter)

![License](https://img.shields.io/badge/license-MIT-blue.svg) ![Platform](https://img.shields.io/badge/platform-Windows-lightgrey.svg) ![NetVersion](https://img.shields.io/badge/.NET-8.0-purple.svg)

**EncryptionMinerControl** is a comprehensive desktop GUI tool designed to manage, monitor, and automate cryptocurrency mining on Windows. It seamlessly integrates **XMRig (CPU)** and **Rigel (GPU)** miners into a unified control panel with advanced features like Idle Mining detection and Keep Awake functionality.

---

## 🌍 Language / 언어
- [English](#english-section)
- [한국어 (Korean)](#korean-section)

---

<a name="english-section"></a>
## 🚀 Features (English)

### 1. Unified Control Dashboard
- **Dashboard**: High-level status overview (Running/Stopped) and **Idle Monitor Gauge**.
- **Monitoring**: Real-time log viewer and manual Start/Stop controls for XMRig and Rigel.
- **Settings**: Centralized configuration for wallets, pools, and algorithms.

### 2. Smart Idle Mining
- Automatically starts mining when the computer is idle (no mouse/keyboard input for 60 seconds).
- Instantly stops mining when user activity is detected.
- Visual **Progress Bar** on the dashboard shows time remaining until mining starts.

### 3. Keep Awake (Mouse Jiggler)
- Prevents the computer from entering sleep mode by simulating microscopic mouse movements.
- **Smart Tech**: Incorporates a **Virtual Idle Timer** algorithm, so the simulated jiggles **do NOT interrupt the Idle Mining timer**. Mining continues even while the jiggler keeps the screen awake.
- Configurable interval (minimum 5 seconds).

### 4. Auto Configuration & Convenience
- **Auto Detect**: Automatically scans for miner executables and detects NVIDIA GPUs.
- **Smart Wallet Naming**: Automatically appends `.{MachineName}_CPU` or `.{MachineName}_GPU` to wallet addresses for easy tracking.
- **Reset to Batch**: Can import settings from existing `.bat` files found in the directory.

### 5. System Integration
- **Run on Startup**: Option to automatically launch the controller when Windows starts.
- **Persistence**: All settings (including checkbox states) are saved to `settings.json` and restored on reboot.
- **Cleanup**: Automatically terminates miner processes when the application is closed.

## 💻 System Requirements
- **OS**: Windows 10 (Version 1607+) or Windows 11. (Windows 7/8 not supported due to .NET 8)
- **Runtime**: .NET 8.0 Desktop Runtime.
- **Privileges**: **Administrator rights** are required to manage processes and prevent sleep mode effectively.
- **PowerShell**: Version 5.0 or higher (for downloader script).

## 🛠 Installation & Usage

1.  **Download**: Get the latest release from the [Releases] page or built `Dist` folder.
2.  **Setup**:
    *   Place `xmrig.exe` and `rigel.exe` in the `Miners` folder (or use the included `download_miners.ps1` script).
3.  **Run**: Execute `EncryptionMinerControl.exe`.
4.  **Configure**:
    *   Go to **Settings** tab.
    *   Click **AUTO DETECT & CONFIGURE** (Recommended).
    *   *Note*: Rigel default algorithm has been updated to **NexaPow (`nexapow`)** with `--nexapow-small-lut on` (reflecting unMineable's XelisHash/KarlsenHash delisting).
    *   Verify your Wallet Address and Pool URL.
    *   Click **SAVE SETTINGS**.
5.  **Idle Mining**:
    *   Check **"Enable Idle Mining"** in Settings.
    *   Go to **Dashboard** and watch the Idle Monitor bar fill up when you stop moving the mouse.

## ⚠️ Precautions
- **Antivirus**: Mining software is often flagged as false positive by antivirus software. You may need to add an exclusion for the application folder.
- **Hardware**: Mining puts stress on your hardware. Ensure adequate cooling.
- **Liability**: Use this software at your own risk. The developer is not responsible for any hardware damage or financial loss.

---

## 🤖 Continuing Development with Antigravity AI

If you want to continue developing this project using **Google DeepMind's Antigravity AI**, follow these steps:

1.  **Clone the Repository**:
    ```bash
    git clone https://github.com/RightRootPark/Win_Coin_DigSetter.git
    ```
2.  **Locate Context**:
    Inside the project, you will find a `Docs` folder containing:
    - `task.md`: The status of all tasks and to-do list.
    - `implementation_plan.md`: Technical design and architectural decisions.
    - `walkthrough.md`: Verification steps and recent changes.

3.  **Prompt the Agent**:
    When you start a new session with Antigravity, give it the following instruction:
    > "Please read the `Docs/task.md` file to understand the current project status and continue working from there."

    The agent will analyze the provided documentation and seamlessly resume work where it left off, maintaining full context of the project's history and logic.
---

---

<a name="korean-section"></a>
## 🌏 암호화폐 채굴 통합 제어기 (Korean)

**EncryptionMinerControl**은 Windows 환경에서 암호화폐 채굴을 손쉽게 관리, 모니터링 및 자동화할 수 있는 강력한 데스크톱 도구입니다. **XMRig (CPU)**와 **Rigel (GPU)** 마이너를 완벽하게 통합하여, 유휴 감지(Idle Mining) 및 절전 방지(Keep Awake)와 같은 스마트한 기능을 제공합니다.

## 🚀 주요 기능 (Korean)

### 1. 통합 제어 대시보드
- **Dashboard**: 채굴기 상태(켜짐/꺼짐)를 직관적으로 확인하고, **Idle Monitor 게이지**를 통해 채굴 시작 카운트다운을 시각화합니다.
- **Monitoring**: 실시간 로그 확인 및 수동 시작/정지 제어가 가능합니다.
- **Settings**: 지갑 주소, 풀 URL, 알고리즘 설정을 한곳에서 관리합니다.

### 2. 스마트 아이들 마이닝 (Idle Mining)
- 컴퓨터가 유휴 상태(60초간 입력 없음)일 때 자동으로 채굴을 시작합니다.
- 사용자가 마우스를 움직이면 즉시 채굴을 중단하여 실사용에 방해를 주지 않습니다.

### 3. 절전 방지 (Keep Awake)
- 마우스를 미세하게 움직여 화면 보호기나 절전 모드 진입을 막습니다.
- **스마트 기술**: **Virtual Idle Timer** 기술이 적용되어, 절전 방지를 위해 마우스가 움직여도 채굴 타이머는 초기화되지 않고 **계속 유지됩니다.** (채굴 끊김 없음!)

### 4. 자동 설정 및 편의성
- **Auto Detect**: 실행 파일 및 NVIDIA 그래픽 카드를 자동으로 감지하여 세팅합니다.
- **자동 이름 지정**: 지갑 주소 뒤에 `.{컴퓨터이름}_CPU` 형식을 자동으로 붙여 워커를 구분하기 쉽게 해줍니다.
- **Reset to Batch**: 폴더 내의 기존 배치 파일(.bat)에서 설정을 불러올 수 있습니다.

### 5. 시스템 통합
- **자동 실행**: 윈도우 시작 시 프로그램이 자동으로 켜지도록 설정할 수 있습니다.
- **설정 저장**: 체크박스 상태를 포함한 모든 설정이 `settings.json`에 저장되어 재부팅 후에도 유지됩니다.
- **자동 정리**: 프로그램을 닫으면 백그라운드에서 실행 중인 채굴기도 함께 깔끔하게 종료됩니다.

## 💻 시스템 요구사항
- **OS**: Windows 10 (1607 버전 이상) 또는 Windows 11. (Windows 7/8은 .NET 8 미지원으로 실행 불가)
- **런타임**: .NET 8.0 Desktop Runtime.
- **권한**: 프로세스 제어 및 절전 방지를 위해 **관리자 권한**으로 실행해야 합니다.
- **PowerShell**: 5.0 버전 이상 (다운로더 스크립트 사용 시).

## 🛠 설치 및 사용 방법

1.  **다운로드**: 최신 배포 폴더(`Dist`)를 준비합니다.
2.  **준비**:
    *   `Miners` 폴더 안에 `xmrig.exe`와 `rigel.exe`가 있어야 합니다. (동봉된 `download_miners.ps1` 스크립트를 사용하면 편리합니다.)
3.  **실행**: `EncryptionMinerControl.exe`를 실행합니다.
4.  **설정**:
    *   **Settings** 탭으로 이동합니다.
    *   **AUTO DETECT & CONFIGURE** 버튼을 누릅니다. (추천)
    *   **참고**: Rigel의 기본 알고리즘은 **NexaPow (`nexapow`, NEXA)**로 업데이트되었습니다. (unMineable의 KarlsenHash 및 2026-11-14 XelisHash 지원 종료 반영, GTX 1060 3GB용 `--nexapow-small-lut on` 옵션 포함)
    *   지갑 주소와 풀 주소를 확인하고 **SAVE SETTINGS**를 누릅니다.
5.  **아이들 마이닝**:
    *   **"Enable Idle Mining"**을 체크합니다.
    *   **Dashboard** 탭에서 마우스를 멈추면 게이지가 차오르는 것을 확인할 수 있습니다.

## ⚠️ 주의사항
- **백신 탐지**: 채굴 프로그램 특성상 백신(Windows Defender 등)에서 바이러스로 오진할 수 있습니다. 폴더를 검사 예외로 설정해주세요.
- **하드웨어 부하**: 채굴은 컴퓨터 자원을 많이 사용하므로 발열 관리에 유의하세요.
- **책임**: 본 소프트웨어 사용으로 인한 하드웨어 손상이나 금전적 손실에 대해 개발자는 책임을 지지 않습니다.

---



---

## 🤖 안티그래비티 AI로 개발 이어서 하기 (Korean)

이 프로젝트를 **Google DeepMind의 안티그래비티 AI(Antigravity AI)**를 사용하여 계속 개발하려면 다음 단계를 따르세요:

1.  **리포지토리 복제 (Clone)**:
    ```bash
    git clone https://github.com/RightRootPark/Win_Coin_DigSetter.git
    ```
2.  **컨텍스트 파일 확인**:
    프로젝트 폴더 내의 `Docs` 폴더에 다음 파일들이 준비되어 있습니다:
    - `task.md`: 현재 작업 진행 상황 및 할 일 목록.
    - `implementation_plan.md`: 기술 설계 및 아키텍처 결정 사항.
    - `walkthrough.md`: 검증 내역 및 최근 변경 사항.

3.  **에이전트에게 지시하기**:
    안티그래비티와 새 세션을 시작할 때, 다음과 같이 지시하세요:
    > "Docs/task.md 파일을 읽고 현재 프로젝트 상태를 파악한 뒤, 작업을 이어서 진행해줘."

    에이전트가 제공된 문서를 분석하고, 프로젝트의 역사와 로직에 대한 완전한 맥락을 유지하면서 중단된 부분부터 원활하게 작업을 재개할 것입니다.

---

