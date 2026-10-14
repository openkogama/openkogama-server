param([string]$Ids, [string]$Server, [string]$Player, [string]$Out, [int]$Width = 1080, [int]$Height = 1920, [int]$Timeout = 180, [string]$Template = "city", [switch]$Spawn, [int]$Settle = 6)
$ErrorActionPreference = "Stop"
$Server = (Resolve-Path $Server).Path
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path

Add-Type -ReferencedAssemblies System.Drawing @"
using System; using System.Runtime.InteropServices; using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging;
public static class Shot {
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] static extern bool AdjustWindowRect(ref RECT r, int style, bool menu);
  [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
  public static bool AllowScreen;
  public static void Click(IntPtr h, int x, int y) {
    var p = new POINT { X = x, Y = y }; ClientToScreen(h, ref p);
    SetForegroundWindow(h); System.Threading.Thread.Sleep(300);
    SetCursorPos(p.X, p.Y); System.Threading.Thread.Sleep(200);
    mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(80); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
  }
  public static int[] LightButton(Bitmap b) {
    int x = b.Width / 2, start = -1;
    for (int y = b.Height / 8; y < b.Height * 7 / 8; y++) {
      var c = b.GetPixel(x, y);
      bool light = c.R > 200 && c.G > 200 && c.B > 190 && Math.Abs(c.R - c.B) < 30;
      if (light && start < 0) start = y;
      if (!light && start >= 0) { if (y - start > b.Height / 40) return new[] { x, (start + y) / 2 }; start = -1; }
    }
    return null;
  }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public static void Size(IntPtr h, int w, int hh) {
    var r = new RECT { L = 0, T = 0, R = w, B = hh };
    AdjustWindowRect(ref r, GetWindowLong(h, -16), false);
    SetWindowPos(h, IntPtr.Zero, 0, 0, r.R - r.L, r.B - r.T, 0x0004 | 0x0010);
  }
  public static bool Blank(Bitmap b) {
    Color first = b.GetPixel(0, 0); int dark = 0, total = 0, same = 0;
    for (int y = 0; y < b.Height; y += 23) for (int x = 0; x < b.Width; x += 23) {
      var c = b.GetPixel(x, y); total++;
      if (c.R + c.G + c.B < 30) dark++;
      if (Math.Abs(c.R - first.R) + Math.Abs(c.G - first.G) + Math.Abs(c.B - first.B) < 12) same++;
    }
    return dark * 10 > total * 9 || same * 100 > total * 99;
  }
  public static Bitmap Capture(IntPtr h) {
    RECT r; GetClientRect(h, out r); int w = r.R - r.L, hh = r.B - r.T;
    var b = new Bitmap(w, hh);
    using (var g = Graphics.FromImage(b)) { var dc = g.GetHdc(); PrintWindow(h, dc, 3); g.ReleaseHdc(dc); }
    if (!Blank(b) || !AllowScreen) return b;
    var p = new POINT(); ClientToScreen(h, ref p);
    using (var g = Graphics.FromImage(b)) g.CopyFromScreen(p.X, p.Y, 0, 0, new Size(w, hh));
    return b;
  }
  public static void Save(Bitmap b, int w, int hh, string path) {
    using (var o = new Bitmap(w, hh)) {
      using (var g = Graphics.FromImage(o)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(b, 0, 0, w, hh); }
      o.Save(path, ImageFormat.Png);
    }
  }
}
"@

[Shot]::AllowScreen = $env:GITHUB_ACTIONS -eq "true"
Add-Type -AssemblyName System.Windows.Forms
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$scale = [Math]::Min(1.0, [Math]::Min($screen.Width / $Width, $screen.Height / $Height))
$renderWidth = [int][Math]::Round($Width * $scale)
$renderHeight = [int][Math]::Round($Height * $scale)
Write-Host "screen $($screen.Width)x$($screen.Height) render ${renderWidth}x$renderHeight"

$agent = @{ "User-Agent" = "openkogama-server" }
$versions = (Invoke-RestMethod "https://cdn.openkogama.org/versions.json" -Headers $agent).versions
$results = Join-Path $Out "results.jsonl"
$webPlayerReady = $false

function Stop-Leftovers {
    Get-Process kogama, openkogama-player, openkogama-server -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep 2
}

function Install-WebPlayer {
    $root = Join-Path $env:USERPROFILE "AppData\LocalLow\Unity\WebPlayer"
    $zip = Join-Path $env:RUNNER_TEMP "webplayer.zip"
    Invoke-WebRequest "https://cdn.openkogama.org/webplayer/unity-webplayer-4.6.6f2-win.zip" -OutFile $zip -Headers $agent
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
    foreach ($entry in $archive.Entries) {
        $path = Join-Path $root ($entry.FullName -replace '/', '\')
        if ($entry.FullName.EndsWith('/') -or $entry.FullName.EndsWith('\')) { New-Item -ItemType Directory -Force $path | Out-Null; continue }
        New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $path, $true)
    }
    $archive.Dispose()
    New-Item -Force "HKCU:\Software\Unity\WebPlayer" | Out-Null
    Set-ItemProperty "HKCU:\Software\Unity\WebPlayer" Directory $root
    Set-ItemProperty "HKCU:\Software\Unity\WebPlayer" UnityWebPlayerReleaseChannel Stable
    Set-ItemProperty "HKCU:\Software\Unity\WebPlayer" UnityWebPlayerDevelopment no
}

function Press-Play($handle, $work) {
    $menu = [Shot]::Capture($handle)
    $path = Join-Path $work "menu.png"
    $menu.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $point = $null
    $found = & (Join-Path $PSScriptRoot "ocr.ps1") -Image $path -Word PLAY | Select-Object -Last 1
    if ($found) { $point = $found.Split(' ') | ForEach-Object { [int]$_ } ; $how = "ocr" }
    else { $point = [Shot]::LightButton($menu); $how = "button" }
    $menu.Dispose()
    if (-not $point) { Write-Host "play button not found"; return "none" }
    [Shot]::Click($handle, $point[0], $point[1])
    Write-Host "clicked play at $($point[0]),$($point[1]) via $how"
    return $how
}

function Capture-Build($entry) {
    $version = if ($entry.version) { $entry.version } else { "2012" }
    $name = "{0}-{1}" -f $entry.timestamp, $version
    $work = Join-Path $env:RUNNER_TEMP "build"
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $work | Out-Null
    Write-Host "== $name unity $($entry.unityVersion)"

    $zip = Join-Path $work "client.zip"
    Invoke-WebRequest ($entry.urls | Select-Object -First 1) -OutFile $zip -Headers $agent
    if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $entry.sha256.ToUpper()) { throw "sha256 mismatch" }
    $client = Join-Path $work "client"
    Expand-Archive $zip $client
    Remove-Item $zip

    Get-ChildItem $Server -Filter "server.db*" | Remove-Item -Force
    $serverLog = Join-Path $work "server.log"
    $serverProcess = Start-Process (Join-Path $Server "openkogama-server.exe") -WorkingDirectory $Server -RedirectStandardOutput $serverLog -RedirectStandardError (Join-Path $work "server-errors.log") -PassThru -NoNewWindow
    for ($i = 0; $i -lt 60 -and -not ((Test-Path $serverLog) -and (Select-String -Path $serverLog -Pattern "http 8080" -Quiet)); $i++) { Start-Sleep 1 }
    $world = (Invoke-RestMethod -Method Post "http://127.0.0.1:8080/api/worlds?name=$Template&template=$Template" -Body "").id

    $clientLog = Join-Path $work "client.log"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables["http_proxy"] = "http://127.0.0.1:8081"
    $psi.EnvironmentVariables["no_proxy"] = "127.0.0.1,localhost"
    if ($entry.unityVersion.StartsWith("3.")) {
        if (-not $script:webPlayerReady) { Install-WebPlayer; $script:webPlayerReady = $true }
        $webFile = (Get-ChildItem $client -Recurse -Include *.unity3d, *.unityweb | Select-Object -First 1).FullName
        $psi.FileName = Join-Path $Player "openkogama-player.exe"
        $psi.WorkingDirectory = $Player
        if ($entry.unityVersion.StartsWith("3.4")) {
            $psi.Arguments = "`"${webFile}?Username=1&Password=&PlanetName=$world&EditMode=false`" --serve-as http://127.0.0.1:8080/kogama2012/WebPlayer.unity3d --title KoGaMa --log `"$clientLog`""
        } else {
            $session = "http://127.0.0.1:8080/session?mode=play&world=$world&client=$version"
            $psi.Arguments = "`"$webFile`" --version $version --reply `"sendPlayerParams=$session&client=$version`" --title KoGaMa --log `"$clientLog`""
        }
    } else {
        $exe = Get-ChildItem $client -Recurse -Filter kogama.exe | Select-Object -First 1
        $session = "http://127.0.0.1:8080/session?mode=play&world=$world&client=$version&unity=$($entry.unityVersion)"
        $psi.FileName = $exe.FullName
        $psi.WorkingDirectory = $exe.DirectoryName
        $psi.Arguments = "kogamaPackage:" + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($session)) + " -popupwindow -logFile `"$clientLog`""
    }
    $game = [System.Diagnostics.Process]::Start($psi)

    $ready = $false
    for ($i = 0; $i -lt $Timeout; $i++) {
        Start-Sleep 1
        if (Select-String -Path $serverLog -Pattern "actor ready|synchronize finished" -Quiet) { $ready = $true; break }
        if ($game.HasExited) { break }
    }
    Start-Sleep 10
    $file = $null
    $blank = $true
    $clicked = $null
    $game.Refresh()
    if (-not $game.HasExited -and $game.MainWindowHandle -ne [IntPtr]::Zero) {
        [Shot]::Size($game.MainWindowHandle, $renderWidth, $renderHeight)
        Start-Sleep 8
        if ($Spawn) { $clicked = Press-Play $game.MainWindowHandle $work; Start-Sleep $Settle }
        $bitmap = [Shot]::Capture($game.MainWindowHandle)
        $blank = [Shot]::Blank($bitmap)
        $file = "$name.png"
        [Shot]::Save($bitmap, $Width, $Height, (Join-Path $Out $file))
        $bitmap.Dispose()
    }
    Write-Host "ready $ready blank $blank file $file"

    Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
    Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
    Stop-Leftovers
    New-Item -ItemType Directory -Force (Join-Path $Out "logs") | Out-Null
    foreach ($log in @($serverLog, $clientLog)) { if (Test-Path $log) { Copy-Item $log (Join-Path $Out "logs\$name-$(Split-Path $log -Leaf)") } }
    [ordered]@{ id = $entry.id; version = $version; unity = $entry.unityVersion; timestamp = $entry.timestamp; ready = $ready; blank = $blank; file = $file; clicked = $clicked } | ConvertTo-Json -Compress | Add-Content $results
}

foreach ($id in $Ids.Split(',')) {
    $entry = $versions | Where-Object { $_.id -eq $id } | Select-Object -First 1
    if (-not $entry) { Write-Host "no build $id"; continue }
    try { Capture-Build $entry }
    catch {
        Write-Host "failed $id $_"
        Stop-Leftovers
        [ordered]@{ id = $entry.id; version = $entry.version; unity = $entry.unityVersion; timestamp = $entry.timestamp; ready = $false; blank = $true; file = $null; error = "$_" } | ConvertTo-Json -Compress | Add-Content $results
    }
}
