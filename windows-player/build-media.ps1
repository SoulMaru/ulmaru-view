param([string]$Output = (Join-Path $PSScriptRoot 'MediaPlayer.engine.exe'))
$ErrorActionPreference = 'Stop'
$wpf = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF'
$root = Split-Path $PSScriptRoot -Parent
& 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe' /nologo /target:winexe "/out:$Output" "/win32icon:$root\assets\eolmaru-video-baby.ico" "/reference:$wpf\PresentationFramework.dll" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\WindowsBase.dll" /reference:System.Xaml.dll /reference:System.Windows.Forms.dll /reference:System.Xml.Linq.dll "$PSScriptRoot\MediaPlayer.cs" "$PSScriptRoot\VideoPreview.cs"
if ($LASTEXITCODE -ne 0) { throw 'Media player compilation failed.' }
