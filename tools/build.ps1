# Rebuilds the arena scene and the Windows player, optionally renders the pose sheet,
# runs an AI-vs-AI capture, and launches the game.
#   powershell -File tools\build.ps1 [-SkipScene] [-Poses] [-Capture] [-Open]
param([switch]$SkipScene, [switch]$Poses, [switch]$Capture, [switch]$Open, [switch]$PosesOnly)

Get-Process LastStance -ErrorAction SilentlyContinue | Stop-Process -Force   # a running game locks the .exe

$root = "C:\Users\ASUS\LastStance"
$proj = "$root\UnityProject"
$unity = "C:\Users\ASUS\OneDrive\Desktop\6000.6.2f1\Editor\Unity.exe"

function Run-Unity($method, $log, $extra = @()) {
    $args = @('-batchmode', '-quit', '-projectPath', "`"$proj`"", '-executeMethod', $method, '-logFile', "`"$log`"") + $extra
    $p = Start-Process $unity -ArgumentList $args -PassThru
    if (-not $p.WaitForExit(1800000)) {
        Get-Process Unity, bee_backend -ErrorAction SilentlyContinue | Stop-Process -Force
        return $false
    }
    return $p.ExitCode -eq 0
}

function Compile-Errors($log) {
    Select-String -Path $log -Pattern "error CS" -ErrorAction SilentlyContinue | ForEach-Object { $_.Line } | Select-Object -Unique
}

if (-not $SkipScene -and -not $PosesOnly) {
    $null = Run-Unity 'SceneBuilder.BuildBatch' "$root\unity-scene.log"
    $errors = Compile-Errors "$root\unity-scene.log"
    if ($errors) { "SCENE: compile errors"; $errors; exit 1 }
    Select-String -Path "$root\unity-scene.log" -Pattern "SceneBuilder: |Exception" | Select-Object -First 8 | ForEach-Object { "SCENE: " + $_.Line }
}

if ($Poses -or $PosesOnly) {
    $null = Run-Unity 'AnimAnalyzer.BuildBatch' "$root\unity-poses.log" @('-sheet', "`"$root\Build\animsheet.png`"")
    $errors = Compile-Errors "$root\unity-poses.log"
    if ($errors) { "POSES: compile errors"; $errors; exit 1 }
    Select-String -Path "$root\unity-poses.log" -Pattern "AnimAnalyzer: |MixamoMaterials: |Exception" | Select-Object -First 80 | ForEach-Object { "POSES: " + $_.Line }
    if ($PosesOnly) { exit 0 }
}

$built = $false
for ($i = 1; $i -le 3 -and -not $built; $i++) {
    $null = Run-Unity 'BuildTools.BuildWindows' "$root\unity-player.log"
    $built = [bool](Select-String -Path "$root\unity-player.log" -Pattern "BuildTools: Succeeded" -Quiet)
    if (-not $built) {
        $errors = Compile-Errors "$root\unity-player.log"
        if ($errors) { "PLAYER: compile errors"; $errors; exit 1 }
        "PLAYER: attempt $i failed, retrying"
        # a hung or killed build leaves stale build caches that make every later attempt fail
        Get-Process Unity, bee_backend -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 3
        Remove-Item -Recurse -Force "$proj\Library\Bee", "$proj\Library\BuildPlayerData" -ErrorAction SilentlyContinue
    }
}
if (-not $built) { "PLAYER: build failed"; exit 1 }
"PLAYER: built"

if ($Capture) {
    $cap = "$root\Build\captures"
    Remove-Item $cap -Recurse -Force -ErrorAction SilentlyContinue
    $g = Start-Process "$root\Build\LastStance.exe" -ArgumentList @('-screen-fullscreen', '0', '-screen-width', '1600', '-screen-height', '900', '-autocapture', "`"$cap`"", '-logFile', "`"$root\Build\autocapture.log`"") -PassThru
    if (-not $g.WaitForExit(320000)) { Stop-Process -Id $g.Id -Force; "CAPTURE: timed out" }
    Select-String -Path "$root\Build\autocapture.log" -Pattern "\[AUTO\] (round|KO|match|summary|quit|t=[0-9.]+ fps)|Exception" | Select-Object -First 40 | ForEach-Object { $_.Line }
    "CAPTURE: " + ((Get-ChildItem $cap -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name) -join ", ")
}

if ($Open) {
    Start-Process "$root\Build\LastStance.exe" -ArgumentList @('-screen-fullscreen', '0', '-screen-width', '1600', '-screen-height', '900')
    "OPENED: LastStance.exe"
}
