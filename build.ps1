# Builds bin\AutoClicker.exe with the C# compiler that ships with Windows (.NET Framework 4.x, WPF).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path $fx)) { $fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$csc = Join-Path $fx 'csc.exe'
$wpf = Join-Path $fx 'WPF'

$icon = Join-Path $root 'app.ico'
& (Join-Path $root 'make-icon.ps1') -Out $icon -Logo (Join-Path $root 'src\logo.png')

$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }

& $csc /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu `
    "/out:$bin\AutoClicker.exe" "/win32icon:$icon" "/win32manifest:$root\app.manifest" `
    "/lib:$wpf" /r:System.dll /r:System.Xaml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:WindowsBase.dll /r:PresentationCore.dll /r:PresentationFramework.dll `
    "/resource:$root\src\MainWindow.xaml,AutoClicker.MainWindow.xaml" `
    "/resource:$root\src\logo.png,AutoClicker.logo.png" `
    $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "OK -> $bin\AutoClicker.exe"
