# 소스코드 분석 보고서: Windows 환경 호환성 및 잠재적 문제

이 문서는 `EncryptionMinerControl` 프로젝트의 소스코드를 분석하여, 특정 Windows 환경에서 발생할 수 있는 잠재적인 문제점들을 정리한 것입니다.

## 1. 운영체제 버전 호환성 (.NET 8.0)
- **분석**: 프로젝트 파일(`EncryptionMinerControl.csproj`)이 `net8.0-windows`를 타겟으로 하고 있습니다.
- **잠재적 문제**: .NET 8은 **Windows 7, 8, 8.1을 더 이상 지원하지 않습니다.**
- **영향**: 해당 구버전 Windows를 사용하는 사용자는 프로그램을 실행할 수 없습니다. Windows 10 버전 1607 이상 또는 Windows 11이 필요합니다.

## 2. PowerShell 스크립트 호환성
- **분석**: `download_miners.ps1` 스크립트는 `Expand-Archive` 명령어를 사용합니다.
- **잠재적 문제**: `Expand-Archive`는 **PowerShell 5.0** (Windows 10 기본 탑재) 이상에서 지원됩니다.
- **영향**: Windows 7 (기본 PowerShell 2.0) 등 구형 PowerShell 환경에서는 스크립트가 작동하지 않습니다. (단, .NET 8 요구사항으로 인해 이미 OS 제약이 걸리므로 큰 문제는 아닐 수 있습니다.)

## 3. 텍스트 인코딩 문제 (.bat 파일 읽기)
- **분석**: `ViewModels/MainViewModel.cs`의 `ResetToBatchDefaults` 메서드에서 `.bat` 파일을 읽을 때 `File.ReadAllText(file)`을 사용합니다. 이는 기본적으로 **UTF-8**로 동작합니다.
- **잠재적 문제**: 한국어 Windows 환경에서 메모장(Notepad)으로 배치 파일을 작성하고 저장하면 기본적으로 **CP949 (ANSI)** 인코딩으로 저장됩니다.
- **영향**: 파일 내에 한글 주석이나 경로가 포함되어 있을 경우, 이를 UTF-8로 읽으면 글자가 깨질 수 있습니다. 다행히 마이너 설정값(지갑 주소 등)은 대부분 영문(ASCII)이므로 기능적 오류로 이어질 확률은 낮으나, 잠재적인 위험 요소입니다.

## 4. 프로세스 권한 문제 (Zombie Process Cleanup)
- **분석**: `ProcessManager.cs`에서 `p.Kill()`을 사용하여 프로세스를 강제 종료합니다.
- **잠재적 문제**: 만약 사용자가 수동으로 마이너(`xmrig.exe`)를 **관리자 권한**으로 실행해둔 상태에서, 이 프로그램(제어기)을 **일반 권한**으로 실행하면, 권한 부족으로 인해 `Win32Exception (Access Denied)`이 발생하여 프로세스를 종료하지 못할 수 있습니다.
- **대응**: 프로그램이 관리자 권한으로 실행되도록 유도하거나(Manifest 수정), 예외 처리를 통해 안내할 필요가 있습니다.

## 5. WMI 의존성 (NVIDIA GPU 감지)
- **분석**: `MainViewModel.cs`에서 `Win32_VideoController` WMI 쿼리를 사용하여 NVIDIA GPU를 감지합니다.
- **잠재적 문제**: 일부 커스텀 윈도우(Lite 버전, Gaming Edition 등)나 시스템 파일이 손상된 환경에서는 WMI 서비스가 비활성화되어 있거나 작동하지 않을 수 있습니다.
- **영향**: GPU가 있음에도 불구하고 감지 실패로 인해 `RigelMiner`가 자동으로 활성화되지 않을 수 있습니다.

## 6. 코드 내 오타 발견
- **위치**: `ViewModels/MainViewModel.cs` (Line 449 근처)
- **내용**: `XmrigMiner.Config.ExtraArguments` 설정에 `--hube-pages-jit`라는 옵션이 포함되어 있습니다.
- **수정 필요**: 이는 `--huge-pages-jit` (또는 `--huge-pages`)의 오타로 보입니다. 이 오타로 인해 XMRig가 해당 옵션을 인식하지 못하거나 경고를 띄울 수 있습니다.

## 7. 백신 탐지 (Windows Defender)
- **분석**: 마이닝 소프트웨어 특성상 Windows Defender 등 백신 프로그램에 의해 `Miners/xmrig.exe` 등이 격리되거나 삭제될 가능성이 매우 높습니다. 이는 코드의 버그는 아니지만 가장 빈번한 실행 환경 문제입니다.
