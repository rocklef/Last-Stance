# Runs the PC build as if it were the phone: a 20:9 window (like the OnePlus Nord CE 3 Lite),
# touch controls on, the mouse acting as a finger.   powershell -File tools\phone_preview.ps1 [-Boss]
param([switch]$Boss)
$exe = "C:\Users\ASUS\LastStance\Build\LastStance.exe"
$a = @('-touch', '-screen-width', '1600', '-screen-height', '720', '-screen-fullscreen', '0')
if ($Boss) { $a += '-boss' }
Start-Process $exe -ArgumentList $a
