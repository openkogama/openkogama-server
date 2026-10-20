param([string]$Build, [string]$Server, [string]$Player, [string]$Out, [string]$Formats = "wav,ogg,mp3", [int]$Timeout = 300)
$ErrorActionPreference = "Stop"
$Server = (Resolve-Path $Server).Path
$Player = (Resolve-Path $Player).Path
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path

$agent = @{ "User-Agent" = "openkogama-server" }
$versions = (Invoke-RestMethod "https://cdn.openkogama.org/versions.json" -Headers $agent).versions
$work = Join-Path $env:RUNNER_TEMP "sound"
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $work | Out-Null
$serverLog = Join-Path $work "server.log"
$script:webPlayerReady = $false
$tone = Join-Path $PSScriptRoot "sound-tone.py"

. (Join-Path $PSScriptRoot "windows-clients.ps1")

function Listen($seconds, $label) {
    $line = & python $tone listen $seconds (Join-Path $Out "$label.wav") 2>&1 | Select-Object -Last 1
    try { return ($line | ConvertFrom-Json) } catch { Write-Host "listen failed: $line"; return $null }
}

function Lines($from) { $all = Log; if ($all.Count -le $from) { return @() } return @($all[$from..($all.Count - 1)]) }

Get-Process kogama, openkogama-player, openkogama-server -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$data = Join-Path $work "data"
$sounds = Join-Path $data "plugins\data\sounds"
New-Item -ItemType Directory -Force $sounds | Out-Null
'{"publicUrl":"http://127.0.0.1:8080"}' | Set-Content (Join-Path $data "server.json")
Get-ChildItem (Join-Path $Server "plugins") -Filter *.dll | Copy-Item -Destination (Join-Path $data "plugins")
& python $tone make $sounds
$env:OPENKOGAMA_TRACE = "1"
$serverProcess = Start-Process (Join-Path $Server "openkogama-server.exe") -WorkingDirectory $data -RedirectStandardOutput $serverLog -RedirectStandardError (Join-Path $work "server-errors.log") -PassThru -NoNewWindow
if (-not (Wait-Log "http 8080" 0 90)) { throw "server did not start" }

$entry = Pick $Build
$results = @()
try {
    foreach ($format in $Formats.Split(",")) {
        $tag = "$($Build.Replace('@', '-'))-$format"
        $quiet = Listen 2 "$tag-quiet"
        $world = (Invoke-RestMethod -Method Post "http://127.0.0.1:8080/api/worlds?name=sound-$format&template=soundcheck-$format" -Body "").id
        $mark = (Log).Count
        $client = Start-Client "a" $entry $world 1
        Find-Window $client
        Enter-World $client $mark | Out-Null
        if ($client.stage -eq "world") {
            Start-Sleep 6
            Find-Window $client
            if ($client.unity.StartsWith("3.") -or (Press-Play $client)) { $client.stage = "playing" }
        }
        Start-Sleep 15
        $heard = Listen 5 "$tag-heard"
        Shot $client $format
        $lines = Lines $mark
        $built = $lines | Where-Object { $_ -match "sound \d+ tone-${format}: .* bundle for (\S+)" } | Select-Object -First 1
        $key = if ($built) { [regex]::Match($built, "bundle for (\S+)").Groups[1].Value } else { $null }
        $errors = @($lines | Where-Object { $_ -match "client (error|exception)|Could not find asset info|Failed sound|SoundEmitter error|AssetBundle" } | Select-Object -First 8)
        $clientLog = Join-Path $work "a-client.log"
        $clientErrors = @()
        if (Test-Path $clientLog) {
            $clientErrors = @(Get-Content $clientLog -ErrorAction SilentlyContinue | Where-Object { $_ -match "Could not find asset info|Failed sound|SoundEmitter error|AssetBundle|can't be loaded|FMOD|audio" } | Select-Object -First 12)
            Copy-Item $clientLog (Join-Path $Out "$tag-client.log")
        }
        $ok = $heard -and $heard.tone -gt 0.2 -and $heard.rms -gt 0.003
        $result = [ordered]@{
            build = $Build; version = $client.version; unity = $client.unity; format = $format; stage = $client.stage; key = $key
            quiet = $quiet; heard = $heard; ok = $ok; errors = $errors; clientErrors = $clientErrors
        }
        $results += $result
        Write-Host ($result | ConvertTo-Json -Compress -Depth 4)
        Stop-Client $client
        Start-Sleep 3
    }
} finally {
    Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep 2
    Copy-Item $serverLog (Join-Path $Out "server.log") -ErrorAction SilentlyContinue
    foreach ($logs in @((Join-Path $env:LOCALAPPDATA "Unity\WebPlayer\log"), (Join-Path $env:USERPROFILE "AppData\LocalLow\Unity\WebPlayer\log"))) {
        Get-ChildItem $logs -Filter "*.txt" -ErrorAction SilentlyContinue | Copy-Item -Destination $Out
    }
    ConvertTo-Json @($results) -Depth 5 | Set-Content (Join-Path $Out "results.json")
}
