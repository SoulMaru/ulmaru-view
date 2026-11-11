param(
    [Parameter(Mandatory = $true)][string]$Video,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Exe = 'C:\AI_SOULMARU\4.adkiller\Adkiller Media Player\얼마루미디어.exe'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, Microsoft.VisualBasic
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class PlayerWin {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, int data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$lines = New-Object System.Collections.Generic.List[string]
$script:pass = $true
function Check([string]$name, [bool]$ok, [string]$detail) {
    $prefix = 'FAIL '
    if ($ok) { $prefix = 'PASS ' }
    $script:lines.Add($prefix + $name + ' - ' + $detail)
    $script:pass = $script:pass -and $ok
}
function Rect([IntPtr]$handle) {
    $rect = New-Object PlayerWin+RECT
    [void][PlayerWin]::GetWindowRect($handle, [ref]$rect)
    return $rect
}
function IsFullScreen([IntPtr]$handle) {
    $rect = Rect $handle
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $wide = ($rect.Right - $rect.Left) -ge ($bounds.Width - 8)
    $tall = ($rect.Bottom - $rect.Top) -ge ($bounds.Height - 8)
    return ($wide -and $tall -and $rect.Left -le 2 -and $rect.Top -le 2)
}
function FindButton($root, [string]$name) {
    $type = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $label = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $both = New-Object System.Windows.Automation.AndCondition($type, $label)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $both)
}

$process = Start-Process -FilePath $Exe -ArgumentList ('"' + $Video + '"') -PassThru
try {
    $window = $null
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 300
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
            break
        }
    }
    Check '플레이어 창 열림' ($null -ne $window) 'handle=' + $process.MainWindowHandle
    if ($null -eq $window) { throw '창을 찾지 못했습니다.' }

    [void][PlayerWin]::SetForegroundWindow($process.MainWindowHandle)
    try { [Microsoft.VisualBasic.Interaction]::AppActivate($process.Id) } catch { }
    Start-Sleep -Seconds 3

    $handle = $process.MainWindowHandle
    $rect = Rect $handle
    $centerX = [int](($rect.Left + $rect.Right) / 2)
    $centerY = [int](($rect.Top + $rect.Bottom) / 2)

    # 영상 가운데를 실제로 클릭해 창을 활성화한다(키 입력을 받으려면 필요).
    [void][PlayerWin]::SetCursorPos($centerX, $centerY)
    Start-Sleep -Milliseconds 150
    [PlayerWin]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    [PlayerWin]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
    [PlayerWin]::mouse_event(0x0800, 0, 0, 120, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 250
    $textType = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $texts = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textType)
    $seen = ''
    foreach ($text in $texts) { if ($text.Current.Name -match '볼륨\s*\d+%') { $seen = $text.Current.Name } }
    Check '휠 조절 시 볼륨 표시 없음' ($seen -eq '') ('보인 표시=' + $seen)

    [PlayerWin]::mouse_event(0x0800, 0, 0, -120, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 300
    $texts = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textType)
    $seenDown = ''
    foreach ($text in $texts) { if ($text.Current.Name -match '볼륨\s*\d+%') { $seenDown = $text.Current.Name } }
    Check '휠 내려도 볼륨 표시 없음' ($seenDown -eq '') ('보인 표시=' + $seenDown)
    Check '휠 조절 후 창 정상' (-not $process.HasExited) ('hasExited=' + $process.HasExited)

    [void][PlayerWin]::SetCursorPos($centerX, $centerY)
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Milliseconds 900
    Check 'Enter 전체화면' (IsFullScreen $handle) ('rect=' + (Rect $handle).Left + ',' + (Rect $handle).Top + ',' + (Rect $handle).Right + ',' + (Rect $handle).Bottom)

    $top = (Rect $handle).Top + 12
    [void][PlayerWin]::SetCursorPos($centerX, $top)
    Start-Sleep -Milliseconds 600
    $exitButton = FindButton $window '✕'
    Check '상단 X 표시' ($null -ne $exitButton -and -not $exitButton.Current.IsOffscreen) ('found=' + ($null -ne $exitButton))
    if ($null -ne $exitButton) {
        $box = $exitButton.Current.BoundingRectangle
        $windowRect = Rect $handle
        $windowCenter = ($windowRect.Left + $windowRect.Right) / 2
        $buttonCenter = ($box.Left + $box.Right) / 2
        $offset = [math]::Abs($buttonCenter - $windowCenter)
        Check 'X 화면 중앙 상단 배치' ($offset -le 40 -and ($box.Top - $windowRect.Top) -le 80) ('center_offset=' + $offset + ' top_gap=' + ($box.Top - $windowRect.Top))
    }
    [void][PlayerWin]::SetCursorPos($centerX, $centerY)
    Start-Sleep -Milliseconds 800
    $awayButton = FindButton $window '✕'
    $awayHidden = ($null -eq $awayButton) -or $awayButton.Current.IsOffscreen
    Check '상단 벗어나면 X 숨김' $awayHidden ('found=' + ($null -ne $awayButton) + ' offscreen=' + ($null -ne $awayButton -and $awayButton.Current.IsOffscreen))
    [void][PlayerWin]::SetCursorPos($centerX, $top)
    Start-Sleep -Milliseconds 600
    $exitButton = FindButton $window '✕'

    if ($null -ne $exitButton) {
        $clickBox = $exitButton.Current.BoundingRectangle
        $clickX = [int](($clickBox.Left + $clickBox.Right) / 2)
        $clickY = [int](($clickBox.Top + $clickBox.Bottom) / 2)
        [void][PlayerWin]::SetCursorPos($clickX, $clickY)
        Start-Sleep -Milliseconds 250
        [PlayerWin]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
        [PlayerWin]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 1000
        Check 'X 실제 클릭 종료' (-not (IsFullScreen $handle)) ('click=' + $clickX + ',' + $clickY + ' rect=' + (Rect $handle).Left + ',' + (Rect $handle).Top)
    } else {
        Check 'X 클릭 종료' $false 'X 버튼 없음'
    }

    [void][PlayerWin]::SetForegroundWindow($handle)
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Milliseconds 900
    $enteredAgain = IsFullScreen $handle
    Check 'Enter 재진입' $enteredAgain ('fullscreen=' + $enteredAgain)
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 900
    Check 'Esc 종료' (-not (IsFullScreen $handle)) ('fullscreen=' + (IsFullScreen $handle))
} catch {
    Check '예외' $false $_.Exception.Message
} finally {
    try { $process.CloseMainWindow() | Out-Null } catch { }
    Start-Sleep -Milliseconds 700
    try { if (-not $process.HasExited) { $process.Kill() } } catch { }
    $verdict = 'RESULT FAIL'
    if ($script:pass) { $verdict = 'RESULT PASS' }
    $script:lines.Add($verdict)
    Set-Content -LiteralPath $Out -Value $script:lines -Encoding UTF8
}
if (-not $script:pass) { exit 1 }
