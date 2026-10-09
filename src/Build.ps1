param([string]$TestRoot = '')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path $compiler
$appDirectory = Join-Path (Split-Path $PSScriptRoot) 'dist\拾序'
New-Item -ItemType Directory -Path $appDirectory -Force | Out-Null
$target = Join-Path $appDirectory '拾序.exe'
$references = @('System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Runtime.Serialization.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_)) }
$files = @('Core.cs', 'App.cs', 'Ui.cs', 'MainWindow.cs', 'WidgetWindow.cs', 'WidgetSurface.cs', 'NativeDesktop.cs', 'Focus.cs', 'StudyStats.cs', 'UiSmoke.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output ('/out:' + $target) ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')) ('/win32icon:' + (Join-Path $PSScriptRoot 'App.ico')) ('/resource:' + (Join-Path $PSScriptRoot 'Styles.xaml') + ',Styles.xaml') ('/resource:' + (Join-Path $PSScriptRoot 'App.ico') + ',App.ico') $references $files
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination ($target + '.config') -Force
Write-Output ('Built: ' + $target)
if ($TestRoot) {
    New-Item -ItemType Directory -Path $TestRoot -Force | Out-Null
    $testExe = Join-Path $TestRoot 'core-tests.exe'
    & $compiler /nologo /target:exe /platform:x64 /utf8output /reference:System.Runtime.Serialization.dll ('/out:' + $testExe) (Join-Path $PSScriptRoot 'Core.cs') (Join-Path $PSScriptRoot 'CoreTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
    & $testExe $TestRoot
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
}
