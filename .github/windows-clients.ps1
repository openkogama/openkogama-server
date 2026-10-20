# Helpers to drive game clients on a Windows runner: every build from the 2012 web player to 3.5.
Add-Type -ReferencedAssemblies System.Drawing @"
using System; using System.Runtime.InteropServices; using System.Drawing; using System.Drawing.Imaging;
public static class Win {
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] static extern bool AdjustWindowRect(ref RECT r, int style, bool menu);
  [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public static void Place(IntPtr h, int x, int y, int w, int hh) {
    var r = new RECT { L = 0, T = 0, R = w, B = hh };
    AdjustWindowRect(ref r, GetWindowLong(h, -16), false);
    SetWindowPos(h, IntPtr.Zero, x, y, r.R - r.L, r.B - r.T, 0x0004 | 0x0010);
  }
  public static void Click(IntPtr h, int x, int y) {
    var p = new POINT { X = x, Y = y }; ClientToScreen(h, ref p);
    SetForegroundWindow(h); System.Threading.Thread.Sleep(300);
    SetCursorPos(p.X, p.Y); System.Threading.Thread.Sleep(200);
    mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(120); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    IntPtr target = WindowFromPoint(p);
    if (target != IntPtr.Zero && target != h) {
      var q = p; ScreenToClient(target, ref q); IntPtr l = (IntPtr)((q.Y << 16) | (q.X & 0xffff));
      System.Threading.Thread.Sleep(150);
      PostMessage(target, 0x0201, (IntPtr)1, l); System.Threading.Thread.Sleep(100); PostMessage(target, 0x0202, IntPtr.Zero, l);
    }
  }
  public static void Hold(IntPtr h, byte key, int ms) {
    SetForegroundWindow(h); System.Threading.Thread.Sleep(200);
    keybd_event(key, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(ms); keybd_event(key, 0, 2, UIntPtr.Zero);
  }
  public static Bitmap Capture(IntPtr h) {
    RECT r; GetClientRect(h, out r); int w = Math.Max(r.R - r.L, 1), hh = Math.Max(r.B - r.T, 1);
    var b = new Bitmap(w, hh);
    using (var g = Graphics.FromImage(b)) { var dc = g.GetHdc(); PrintWindow(h, dc, 3); g.ReleaseHdc(dc); }
    var p = new POINT(); ClientToScreen(h, ref p);
    using (var g = Graphics.FromImage(b)) g.CopyFromScreen(p.X, p.Y, 0, 0, new Size(w, hh));
    return b;
  }
  public static int[] Button(Bitmap b) {
    int left = b.Width * 42 / 100, right = b.Width * 58 / 100, start = -1;
    var counts = new System.Collections.Generic.Dictionary<int, int>();
    for (int y = 0; y < b.Height; y += 4) for (int x = 0; x < b.Width; x += 4) { var c = b.GetPixel(x, y); int k = (c.R / 32) * 1024 + (c.G / 32) * 32 + c.B / 32; counts[k] = counts.ContainsKey(k) ? counts[k] + 1 : 1; }
    int best = 0, bestKey = 0; foreach (var kv in counts) if (kv.Value > best) { best = kv.Value; bestKey = kv.Key; }
    int br = (bestKey / 1024) * 32 + 16, bg = ((bestKey / 32) % 32) * 32 + 16, bb = (bestKey % 32) * 32 + 16;
    for (int y = b.Height / 10; y < b.Height; y++) {
      int filled = 0; for (int x = left; x < right; x++) { var c = b.GetPixel(x, y); if (Math.Abs(c.R - br) + Math.Abs(c.G - bg) + Math.Abs(c.B - bb) > 90) filled++; }
      bool on = filled >= (right - left) * 3 / 4;
      if (on && start < 0) start = y;
      if (!on && start >= 0) { if (y - start >= b.Height / 25) return new[] { b.Width / 2, (start + y) / 2 }; start = -1; }
    }
    return null;
  }
}
"@
Add-Type -AssemblyName System.Windows.Forms

function Pick($wanted) {
    $version, $unity = $wanted.Split("@")
    if ($version -eq "2012") { return $versions | Where-Object { -not $_.version -and $_.unityVersion.StartsWith("3.4") } | Select-Object -First 1 }
    $found = $versions | Where-Object { $_.version -eq $version -and (-not $unity -or $_.unityVersion.StartsWith($unity)) } | Sort-Object timestamp -Descending | Select-Object -First 1
    if (-not $found) { throw "no build for $wanted" }
    return $found
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

function Log { return @(Get-Content $serverLog -ErrorAction SilentlyContinue) }

function Wait-Log($pattern, $from, $seconds) {
    for ($i = 0; $i -lt $seconds; $i++) {
        $lines = Log
        for ($n = $from; $n -lt $lines.Count; $n++) { if ($lines[$n] -match $pattern) { return $n + 1 } }
        Start-Sleep 1
    }
    return $null
}

function Start-Client($name, $entry, $world, $profile) {
    $version = if ($entry.version) { $entry.version } else { "2012" }
    $dir = Join-Path $work $name
    $zip = Join-Path $work "$name.zip"
    if (-not (Test-Path $zip)) { Invoke-WebRequest ($entry.urls | Select-Object -First 1) -OutFile $zip -Headers $agent }
    Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
    Expand-Archive $zip $dir
    $clientLog = Join-Path $work "$name-client.log"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables["http_proxy"] = "http://127.0.0.1:8081"
    $psi.EnvironmentVariables["no_proxy"] = "127.0.0.1,localhost"
    if ($entry.unityVersion.StartsWith("3.")) {
        if (-not $script:webPlayerReady) { Install-WebPlayer; $script:webPlayerReady = $true }
        $webFile = (Get-ChildItem $dir -Recurse -Include *.unity3d, *.unityweb | Select-Object -First 1).FullName
        $psi.FileName = Join-Path $Player "openkogama-player.exe"
        $psi.WorkingDirectory = $Player
        if ($entry.unityVersion.StartsWith("3.4")) {
            $psi.Arguments = "`"${webFile}?Username=$profile&Password=&PlanetName=$world&EditMode=false`" --serve-as http://127.0.0.1:8080/kogama2012/WebPlayer.unity3d --title KoGaMa-$name --log `"$clientLog`""
        } else {
            $session = "http://127.0.0.1:8080/session?mode=play&world=$world&client=$version&profile=$profile"
            $psi.Arguments = "`"$webFile`" --version $version --reply `"sendPlayerParams=$session&client=$version`" --title KoGaMa-$name --log `"$clientLog`""
        }
    } else {
        $exe = Get-ChildItem $dir -Recurse -Filter *.exe | Where-Object { $_.Name -notmatch "crash|handler|unins" } | Sort-Object Length -Descending | Select-Object -First 1
        if (-not $exe) { throw "no executable in the $version build" }
        $dataDir = Get-ChildItem $exe.DirectoryName -Directory -Filter "*_Data" | Select-Object -First 1
        if ($dataDir -and $dataDir.Name -ne "$($exe.BaseName)_Data") {
            $renamed = Join-Path $exe.DirectoryName ($dataDir.Name.Substring(0, $dataDir.Name.Length - 5) + ".exe")
            Move-Item $exe.FullName $renamed
            $exe = Get-Item $renamed
        }
        $managed = Get-ChildItem $exe.DirectoryName -Recurse -Filter Assembly-CSharp.dll | Select-Object -First 1
        if ($script:clientFix -and $managed) { Write-Host (& (Join-Path $Server "openkogama-server.exe") client fix $managed.FullName $managed.FullName) }
        $session = "http://127.0.0.1:8080/session?mode=play&world=$world&client=$version&unity=$($entry.unityVersion)&profile=$profile"
        $psi.FileName = $exe.FullName
        $psi.WorkingDirectory = $exe.DirectoryName
        $psi.Arguments = "kogamaPackage:" + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($session)) + " -popupwindow -screen-width 640 -screen-height 400 -logFile `"$clientLog`""
    }
    $process = [System.Diagnostics.Process]::Start($psi)
    return [ordered]@{ name = $name; version = $version; unity = $entry.unityVersion; process = $process; stage = "started"; peer = $null; window = [IntPtr]::Zero }
}

function Find-Window($client) {
    for ($i = 0; $i -lt 30 -and $client.window -eq [IntPtr]::Zero; $i++) {
        $client.process.Refresh()
        if ($client.process.HasExited) { return }
        if ($client.process.MainWindowHandle -ne [IntPtr]::Zero) { $client.window = $client.process.MainWindowHandle; break }
        $child = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*KoGaMa-$($client.name)*" -or ($_.ProcessName -eq "kogama" -and $_.MainWindowHandle -ne [IntPtr]::Zero -and $_.Id -eq $client.process.Id) } | Select-Object -First 1
        if ($child) { $client.window = $child.MainWindowHandle; break }
        Start-Sleep 1
    }
    if ($client.window -ne [IntPtr]::Zero) { [Win]::Place($client.window, $(if ($client.name -eq "a") { 0 } else { 660 }), 0, 640, 400) }
}

function Enter-World($client, $mark) {
    $joined = Wait-Log "peer (\d+): (joined|2012 client joined)" $mark $Timeout
    if (-not $joined) { $client.stage = "no join"; return $mark }
    $client.peer = [regex]::Match((Log)[$joined - 1], "peer (\d+):").Groups[1].Value
    $client.stage = "joined"
    $sent = Wait-Log "peer $($client.peer): (world sent|2012 actor ready)" $joined $Timeout
    if (-not $sent) { $client.stage = "no world"; return $joined }
    $client.stage = "world"
    return $sent
}

function Shot($client, $label) {
    if ($client.window -eq [IntPtr]::Zero) { return }
    try {
        $bitmap = [Win]::Capture($client.window)
        $bitmap.Save((Join-Path $Out "$($client.name)-$label.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
    } catch { Write-Host "screenshot $label failed: $_" }
}

function Press-Play($client) {
    if ($client.window -eq [IntPtr]::Zero) { return $false }
    for ($attempt = 0; $attempt -lt 8; $attempt++) {
        $bitmap = [Win]::Capture($client.window)
        $path = Join-Path $Out "$($client.name)-menu.png"
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $point = $null
        $found = & (Join-Path $PSScriptRoot "ocr.ps1") -Image $path -Word PLAY | Select-Object -Last 1
        if ($found) { $point = $found.Split(' ') | ForEach-Object { [int]$_ }; $how = "ocr" }
        else { $point = [Win]::Button($bitmap); $how = "button" }
        $bitmap.Dispose()
        if ($point) {
            [Win]::Click($client.window, $point[0], $point[1])
            Write-Host "$($client.name): clicked play at $($point[0]),$($point[1]) via $how"
            return $true
        }
        Start-Sleep 5
    }
    Write-Host "$($client.name): no play button"
    return $false
}

function Say($client, $text) {
    if ($client.window -eq [IntPtr]::Zero) { return }
    $bitmap = [Win]::Capture($client.window)
    [Win]::Click($client.window, $bitmap.Width / 2, $bitmap.Height / 2)
    $bitmap.Dispose()
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait($(if ($client.unity.StartsWith("3.4")) { "t" } else { "{ENTER}" }))
    Start-Sleep -Milliseconds 1200
    foreach ($char in $text.ToCharArray()) {
        $key = if ("+^%~(){}[]".Contains([string]$char)) { "{$char}" } else { [string]$char }
        [System.Windows.Forms.SendKeys]::SendWait($key)
        Start-Sleep -Milliseconds 60
    }
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    Start-Sleep -Milliseconds 500
    $bitmap = [Win]::Capture($client.window)
    if ($client.unity.StartsWith("3.5")) { [Win]::Click($client.window, $bitmap.Width * 0.37, $bitmap.Height * 0.94); Start-Sleep -Milliseconds 500 }
    [Win]::Click($client.window, $bitmap.Width / 2, $bitmap.Height / 2)
    $bitmap.Dispose()
}

function Clean($from) { return -not ((Log)[$from..((Log).Count)] -match "exception|failed") }

function Stop-Client($client) {
    if ($client.process -and -not $client.process.HasExited) { Stop-Process -Id $client.process.Id -Force -ErrorAction SilentlyContinue }
    Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*KoGaMa-$($client.name)*" } | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep 2
}
