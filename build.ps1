$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Microsoft.CSharp.dll ("/win32manifest:" + (Join-Path $taskRoot 'app.manifest')) ("/out:" + (Join-Path $taskRoot '游戏定时助手.exe')) (Join-Path $taskRoot 'GameScheduler.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll ("/win32manifest:" + (Join-Path $taskRoot 'app.manifest')) ("/win32icon:" + (Join-Path $taskRoot 'BlackScreenMute.ico')) ("/out:" + (Join-Path $taskRoot 'BlackScreenMute.Game.exe')) (Join-Path $taskRoot 'BlackScreenMute.Game.cs')
if ($LASTEXITCODE -ne 0) { throw 'Black screen build failed' }
