# 얼마루미디어 (Windows 플레이어)

`얼마루미디어.exe`는 광고·인터넷 기능이 없는 단일 창 WPF 미디어 플레이어입니다. .NET Framework 4.0(WPF)에서 동작하며, Windows에 기본 포함된 `csc.exe`로 빌드합니다. NuGet·외부 패키지를 쓰지 않습니다.

## 기능

- **마우스 휠 볼륨**: 재생 화면에서 휠을 위로 굴리면 +5%, 아래로 굴리면 -5%. 0~100%에서 멈추며, **화면에 볼륨 표시를 띄우지 않습니다**. 키보드 `↑/↓`도 5%씩 조절합니다.
- **전체화면**: `Enter` 또는 `F`로 토글, `Esc`로 종료.
- **전체화면 상단 X**: 전체화면에서 마우스를 화면 상단(≤72px)으로 올리면 **화면 중앙 상단에 배경 없는 ✕**가 나타납니다. 누르면 전체화면이 끝나고, 마우스가 상단을 벗어나면 다시 숨습니다.
- 그 밖에 재생·일시정지(`Space`), 5초 이동(`←/→`), 이전·다음, 재생목록, 이미지 보기, 구간 반복, 블러 편집 세션 등 기존 기능을 그대로 유지합니다.

## 빌드

Windows PowerShell 5.1 기준:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\windows-player\build-media.ps1 `
  -Output .\windows-player\MediaPlayer.exe
```

- `build-media.ps1`은 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`와 같은 경로의 WPF 어셈블리를 사용합니다.
- 아이콘은 저장소 루트의 `assets/eolmaru-video-baby.ico`를 씁니다. 없으면 `/win32icon` 인자를 빼고 빌드하세요.
- `MediaPlayer.cs`와 `VideoPreview.cs`는 **UTF-8 BOM**으로 저장하세요. PowerShell 5.1이 BOM 없는 한글 소스를 깨뜨립니다.

## 시험

### 1) 인프로세스 UI 시험

플레이어 클래스를 직접 띄워 내부 상태(볼륨 값, 전체화면 여부, X 정렬·배경·표시)를 확인합니다.

```powershell
$wpf = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe /main:PlayerFeatureCheck `
  /out:.\windows-player\tests\PlayerFeatureCheck.exe `
  "/reference:$wpf\PresentationFramework.dll" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\WindowsBase.dll" `
  /reference:System.Xaml.dll /reference:System.Windows.Forms.dll /reference:System.Xml.Linq.dll `
  .\windows-player\MediaPlayer.cs .\windows-player\VideoPreview.cs .\windows-player\tests\PlayerFeatureCheck.cs

.\windows-player\tests\PlayerFeatureCheck.exe "C:\경로\영상.mp4" ".\player-feature-check.txt"
```

기대 결과: 휠 ±5, 볼륨 표시 없음, `Enter` 전체화면, X 중앙 정렬·투명 배경, 상단에서 X 표시, X 클릭 종료, `Esc` 종료 — 모두 `PASS`, 마지막 줄 `RESULT PASS`.

### 2) 배포 exe 라이브 시험

실제로 실행한 창을 UI 자동화로 조작합니다. 미리 빌드한 exe 경로를 `-Exe`로 넘깁니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\windows-player\tests\player-live-check.ps1 `
  -Video "C:\경로\영상.mp4" -Out ".\player-live-check.txt" `
  -Exe ".\windows-player\MediaPlayer.exe"
```

확인 항목: 창 열림, 휠 조절 시 볼륨 표시 없음(위·아래), `Enter` 전체화면, 상단 X 표시, X 화면 중앙 상단 배치, X 클릭 종료, `Enter` 재진입, `Esc` 종료.

## 배포 위치

빌드 결과를 아래 네 곳에 같은 파일로 복사합니다.

| 경로 | 역할 |
|---|---|
| `Adkiller Media Player\얼마루미디어.exe` | 사용자가 실행하는 대표 실행 파일 |
| `Adkiller Media Player\adkiller Media Player.exe` | 영문 별칭 |
| `Adkiller Media Player\EolmaruMedia.exe` | 영문 별칭 |
| `readers-framework\MediaPlayer.engine.exe` | 내부 실행 엔진 |

배포 전에 기존 exe를 `backups/`에 백업하고, 복사 후 SHA-256이 네 파일 모두 같은지 확인합니다.

## SmartScreen 참고

- 배포 exe에는 **Mark of the Web(Zone.Identifier)이 없습니다.**
- 코드 서명은 하지 않았습니다(`NotSigned`). 서명 인증서가 없으면 게시자 확인 경고가 남을 수 있습니다.
- 로컬 사용자 설정에서 SmartScreen 앱 검사를 끈 상태입니다:
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\AppHost\EnableWebContentEvaluation = 0`
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\SmartScreenEnabled = Off`
- 위 설정을 되돌리려면 각 값을 지우거나 `EnableWebContentEvaluation=1`, `SmartScreenEnabled=Warn`으로 바꿉니다.

## 파일

| 파일 | 역할 |
|---|---|
| `MediaPlayer.cs` | 플레이어 창·재생·휠 볼륨·전체화면·X·블러 편집 |
| `VideoPreview.cs` | 탐색 바 미리보기 |
| `build-media.ps1` | csc 빌드 스크립트 |
| `tests/PlayerFeatureCheck.cs` | 인프로세스 UI 시험 |
| `tests/player-live-check.ps1` | 배포 exe UI 자동화 시험 |
