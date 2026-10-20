param([string]$Pair, [string]$Server, [string]$Player, [string]$Out, [string]$Template = "city", [int]$Timeout = 300)
$ErrorActionPreference = "Stop"
$Server = (Resolve-Path $Server).Path
$Player = (Resolve-Path $Player).Path
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path

$agent = @{ "User-Agent" = "openkogama-server" }
$versions = (Invoke-RestMethod "https://cdn.openkogama.org/versions.json" -Headers $agent).versions
$work = Join-Path $env:RUNNER_TEMP "mixed"
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $work | Out-Null
$serverLog = Join-Path $work "server.log"
$script:webPlayerReady = $false
$result = [ordered]@{ pair = $Pair; checks = [ordered]@{}; clients = @() }

. (Join-Path $PSScriptRoot "windows-clients.ps1")

Get-Process kogama, openkogama-player, openkogama-server -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$data = Join-Path $work "data"
New-Item -ItemType Directory -Force (Join-Path $data "plugins") | Out-Null
'{"publicUrl":"http://127.0.0.1:8080","mixedClients":true}' | Set-Content (Join-Path $data "server.json")
Get-ChildItem (Join-Path $Server "plugins") -Filter *.dll -ErrorAction SilentlyContinue | Copy-Item -Destination (Join-Path $data "plugins")
$env:OPENKOGAMA_TRACE = "1"
$serverProcess = Start-Process (Join-Path $Server "openkogama-server.exe") -WorkingDirectory $data -RedirectStandardOutput $serverLog -RedirectStandardError (Join-Path $work "server-errors.log") -PassThru -NoNewWindow
if (-not (Wait-Log "http 8080" 0 90)) { throw "server did not start" }
$world = (Invoke-RestMethod -Method Post "http://127.0.0.1:8080/api/worlds?name=$Template&template=$Template" -Body "").id
$result.world = $world

$entries = $Pair.Split(",") | ForEach-Object { Pick $_ }
$clients = @()
try {
    $mark = (Log).Count
    $index = 0
    foreach ($entry in $entries) {
        $name = [char](97 + $index)
        $client = Start-Client $name $entry $world ($index + 1)
        $clients += $client
        Find-Window $client
        $mark = Enter-World $client $mark
        Write-Host "$name $($client.version) $($client.stage) peer $($client.peer)"
        if ($client.stage -ne "world") { Shot $client "stuck" }
        $index++
    }
    if (($clients | Where-Object { $_.stage -eq "world" }).Count -eq $clients.Count) {
        Start-Sleep 8
        foreach ($client in $clients) { Find-Window $client; if ($client.unity.StartsWith("3.") -or (Press-Play $client)) { $client.stage = "playing" } else { Shot $client "stuck" } }
        Start-Sleep 10
    }
    if (($clients | Where-Object { $_.stage -eq "playing" }).Count -eq $clients.Count) {
        foreach ($client in $clients) { Shot $client "1-together" }
        $a = $clients[0]; $b = $clients[-1]
        [Win]::Hold($a.window, 0x57, 1500)
        Start-Sleep 3
        foreach ($client in $clients) { Shot $client "2-moved" }
        $before = (Log).Count
        Say $a "hello from a"
        Start-Sleep 3
        $result.checks["chat"] = [bool](Wait-Log "hello from a" $before 10)
        foreach ($client in $clients) { Shot $client "3-chat" }
        [Win]::Hold($b.window, 0x20, 300)
        Start-Sleep 3
        $before = (Log).Count
        for ($step = 0; $step -lt 12; $step++) {
            foreach ($client in $clients) { [Win]::Hold($client.window, @(0x57, 0x41, 0x53, 0x44, 0x20, 0x57)[$step % 6], 400) }
        }
        Start-Sleep 3
        foreach ($client in $clients) { Shot $client "4-fuzz" }
        $result.checks["fuzz clean"] = Clean $before
        $before = (Log).Count
        Say $a "!npc"
        Start-Sleep 6
        foreach ($client in $clients) { Shot $client "5-npc" }
        Say $b "!pos"
        Start-Sleep 3
        $result.checks["npc spawned"] = [bool](Wait-Log "spawned" $before 5)
        $result.checks["npc clean"] = Clean $before
        $before = (Log).Count
        Say $a "!hurt"
        Start-Sleep 4
        foreach ($client in $clients) { Shot $client "6-hurt" }
        Say $a "!hurt 200"
        Start-Sleep 12
        foreach ($client in $clients) { Shot $client "7-respawned" }
        $result.checks["died"] = [bool]((Log)[$before..((Log).Count)] -match "health=Single:0|health=Int32:0|Dead")
        $result.checks["hurt clean"] = Clean $before
        $before = (Log).Count
        Stop-Client $b
        $result.checks["leave"] = [bool](Wait-Log "peer $($b.peer): gone" $before 60)
        Start-Sleep 3
        Shot $a "8-left"
        $mark = (Log).Count
        $again = Start-Client "b" $entries[-1] $world 2
        $clients[-1] = $again
        Find-Window $again
        $mark = Enter-World $again $mark
        $result.checks["rejoin"] = $again.stage -eq "world"
        if ($again.stage -eq "world") { Start-Sleep 8; Find-Window $again; if ($again.unity.StartsWith("3.") -or (Press-Play $again)) { $again.stage = "playing"; Start-Sleep 8 } }
        foreach ($client in $clients) { Shot $client "9-rejoined" }
    }
} finally {
    foreach ($client in $clients) { Stop-Client $client }
    Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep 2
    $result.checks["one world"] = ((Log) -match "play opened").Count -eq 1
    $result.checks["all in"] = ($clients | Where-Object { $_.stage -eq "playing" }).Count -eq $clients.Count
    $result.clients = @($clients | ForEach-Object { [ordered]@{ name = $_.name; version = $_.version; unity = $_.unity; stage = $_.stage; peer = $_.peer } })
    $result.reports = @((Log) | Where-Object { $_ -match "exception|failed|no server operation|rejected|dropped|not handled" } | Select-Object -First 60)
    $result | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Out "result.json")
    Copy-Item $serverLog (Join-Path $Out "server.log") -ErrorAction SilentlyContinue
    Get-ChildItem $work -Filter "*-client.log" -ErrorAction SilentlyContinue | Copy-Item -Destination $Out
    foreach ($logs in @((Join-Path $env:LOCALAPPDATA "Unity\WebPlayer\log"), (Join-Path $env:USERPROFILE "AppData\LocalLow\Unity\WebPlayer\log"))) {
        Get-ChildItem $logs -Filter "*.txt" -ErrorAction SilentlyContinue | Copy-Item -Destination $Out
    }
    Write-Host ($result.checks | ConvertTo-Json -Compress)
}
