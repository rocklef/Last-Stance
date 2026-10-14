# Builds the Android APK (Build\LastStance.apk) and, with -Install, puts it on the phone over USB
# and starts it.   powershell -File tools\build_android.ps1 [-Install]
# One attempt only: retrying straight after a failed native (IL2CPP) build crashes Unity's build
# backend. The full log is kept in unity-android.log.
param([switch]$Install)

$root = "C:\Users\ASUS\LastStance"
$proj = "$root\UnityProject"
$unity = "C:\Users\ASUS\OneDrive\Desktop\6000.6.2f1\Editor\Unity.exe"
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
$log = "$root\unity-android.log"

Get-Process Unity, bee_backend -ErrorAction SilentlyContinue | Stop-Process -Force
$args = @('-batchmode', '-quit', '-buildTarget', 'Android', '-projectPath', "`"$proj`"", '-executeMethod', 'BuildTools.BuildAndroid', '-logFile', "`"$log`"")
$p = Start-Process $unity -ArgumentList $args -PassThru
if (-not $p.WaitForExit(3600000)) { Get-Process Unity, bee_backend -ErrorAction SilentlyContinue | Stop-Process -Force }
$built = [bool](Select-String -Path $log -Pattern "BuildTools: Succeeded" -Quiet)
if (-not $built) {
    $errors = Select-String -Path $log -Pattern "error CS" -ErrorAction SilentlyContinue | ForEach-Object { $_.Line } | Select-Object -Unique
    if ($errors) { "ANDROID: compile errors"; $errors; exit 1 }
    "ANDROID: build failed"
    Select-String -Path $log -Pattern "failed with output|error:|Error:|Exception" -Context 0, 6 | Select-Object -First 12 | ForEach-Object { $_.ToString() }
    exit 1
}
Select-String -Path $log -Pattern "BuildTools: Succeeded" | Select-Object -First 1 | ForEach-Object { "ANDROID: " + $_.Line }

if ($Install) {
    & $adb install -r "$root\Build\LastStance.apk"
    & $adb shell monkey -p com.laststance.game -c android.intent.category.LAUNCHER 1
}
