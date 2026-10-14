# Voices the trailer's narration with the Windows speech engine (Microsoft David): one WAV per line
# in tools\showcase\voice\, slow and low; the finale lines slower still.
#   powershell -File tools\showcase\narrate.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here 'voice'
New-Item -ItemType Directory -Force $out | Out-Null
Add-Type -AssemblyName System.Speech
$lines = (Get-Content (Join-Path $here 'lines.json') -Raw | ConvertFrom-Json).lines
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.SelectVoice('Microsoft David Desktop')
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(44100, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
foreach ($l in $lines) {
    $slow = $l.id -like 'final*' -or $l.id -eq 'end' -or $l.id -like 'open*'
    $rate = if ($slow) { '-28%' } else { '-14%' }
    $text = [System.Security.SecurityElement]::Escape($l.text) -replace '\.\.\.', '<break time="450ms"/>'
    $ssml = "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'><prosody rate='$rate' pitch='-12%'>$text</prosody></speak>"
    $synth.SetOutputToWaveFile((Join-Path $out ($l.id + '.wav')), $fmt)
    $synth.SpeakSsml($ssml)
    $synth.SetOutputToNull()
}
$synth.Dispose()
Write-Output "voiced $($lines.Count) lines"
