param(
    [Parameter(Mandatory = $true)][string]$UnityEditor,
    [string]$WorkDirectory = [IO.Path]::GetTempPath(),
    [string]$ReuseProject
)
$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ($ReuseProject) {
    $testProject = (Resolve-Path $ReuseProject).Path
    if (!(Test-Path "$testProject/.scene-layer-visual-check-project")) { throw 'Reuse requires a project created by this checker.' }
} else {
    $testProject = Join-Path $WorkDirectory ('SceneLayerVisualPackage-' + [Guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Force "$testProject/Assets/Editor", "$testProject/Assets/Checks", "$testProject/Packages/com.marmunc.scene-layer-visual", "$testProject/ProjectSettings" | Out-Null
Set-Content "$testProject/.scene-layer-visual-check-project" 'Disposable Scene Layer Visual check project'
Set-Content "$testProject/ProjectSettings/ProjectVersion.txt" 'm_EditorVersion: 6000.2.7f2'
# Initial dependencies of this checker's own disposable project; never targets the caller's game.
Set-Content "$testProject/Packages/manifest.json" '{"dependencies":{"com.unity.render-pipelines.universal":"17.2.0","com.unity.modules.physics2d":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}'
Get-ChildItem -LiteralPath $packageRoot -Force | Where-Object Name -notin @('.git','Artifacts~') | Copy-Item -Destination "$testProject/Packages/com.marmunc.scene-layer-visual/" -Recurse -Force
Copy-Item "$PSScriptRoot/Editor/*.cs" "$testProject/Assets/Editor/" -Force
Copy-Item "$PSScriptRoot/Runtime/*.cs" "$testProject/Assets/Checks/" -Force
$baseline = "using UnityEngine;`npublic static class SceneLayerVisualBaseline { public static object Run() {`n" + [IO.File]::ReadAllText("$PSScriptRoot/Verify.cs") + "`n} }"
[IO.File]::WriteAllText("$testProject/Assets/Editor/SceneLayerVisualBaseline.cs", $baseline)
$logPath = "$testProject/checks.log"
$process = Start-Process -FilePath $UnityEditor -ArgumentList @('-batchmode', '-projectPath', "`"$testProject`"", '-executeMethod', 'SceneLayerVisualPackageChecks.Run', '-logFile', "`"$logPath`"") -WindowStyle Hidden -PassThru
Write-Output "Project: $testProject"
$deadline = (Get-Date).AddMinutes(15)
while (!$process.WaitForExit(1000)) {
    if ((Get-Date) -gt $deadline) { throw "Unity check timed out; inspect $logPath (process $($process.Id))." }
}
Get-Content $logPath | Select-String 'SLV_|error CS|Shader error|Exception:|FAIL:'
if ($process.ExitCode -ne 0 -or !(Select-String $logPath -Pattern 'SLV_PACKAGE_PASS' -Quiet)) { throw "Package checks failed ($($process.ExitCode)). Log: $logPath" }
$player = Start-Process -FilePath "$testProject/Build/SceneLayerVisualDemo.exe" -ArgumentList @('-batchmode', '-screen-width', '960', '-screen-height', '540', '-slv-check', '-logFile', "`"$testProject/player.log`"") -WindowStyle Hidden -PassThru
if (!$player.WaitForExit(120000)) { throw "Player verification timed out; inspect process $($player.Id) and $testProject/player.log" }
Get-Content "$testProject/player.log" | Select-String 'SLV_|Exception:|Shader error'
if ($player.ExitCode -ne 0 -or !(Select-String "$testProject/player.log" -Pattern 'SLV_PLAYER_PASS' -Quiet)) { throw "Player checks failed: $testProject/player.log" }
Write-Output "Results: $testProject/results.json and $testProject/Build/player-results.json"
