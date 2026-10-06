param([string]$Version, [string]$Server, [string]$Out, [int]$Width = 1080, [int]$Height = 1920)
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
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public static void Size(IntPtr h, int w, int hh) {
    var r = new RECT { L = 0, T = 0, R = w, B = hh };
    AdjustWindowRect(ref r, GetWindowLong(h, -16), false);
    SetWindowPos(h, IntPtr.Zero, 0, 0, r.R - r.L, r.B - r.T, 0x0004 | 0x0010);
  }
  static bool Blank(Bitmap b) {
    for (int y = 0; y < b.Height; y += 37) for (int x = 0; x < b.Width; x += 37) { var c = b.GetPixel(x, y); if (c.R + c.G + c.B > 30) return false; }
    return true;
  }
  public static Bitmap Capture(IntPtr h) {
    RECT r; GetClientRect(h, out r); int w = r.R - r.L, hh = r.B - r.T;
    var b = new Bitmap(w, hh);
    using (var g = Graphics.FromImage(b)) { var dc = g.GetHdc(); PrintWindow(h, dc, 3); g.ReleaseHdc(dc); }
    if (!Blank(b)) return b;
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

try { Set-DisplayResolution -Width 1920 -Height 1080 -Force } catch { Write-Host "resolution: $_" }
Add-Type -AssemblyName System.Windows.Forms
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Write-Host "screen $($screen.Width)x$($screen.Height)"

$agent = @{ "User-Agent" = "openkogama-server" }
$versions = (Invoke-RestMethod "https://cdn.openkogama.org/versions.json" -Headers $agent).versions
$entry = $versions | Where-Object { $_.version -eq $Version } | Sort-Object timestamp | Select-Object -Last 1
if (-not $entry) { throw "no build $Version" }
Write-Host "client $Version unity $($entry.unityVersion) il2cpp $($entry.il2cpp)"
$zip = Join-Path $env:RUNNER_TEMP "client.zip"
Invoke-WebRequest ($entry.urls | Select-Object -First 1) -OutFile $zip -Headers $agent
if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $entry.sha256.ToUpper()) { throw "sha256 mismatch" }
$client = Join-Path $env:RUNNER_TEMP "client"
Expand-Archive $zip $client
$exe = Get-ChildItem $client -Recurse -Filter kogama.exe | Select-Object -First 1
if (-not $exe) { throw "no kogama.exe" }

$serverLog = Join-Path $Out "server.log"
$serverProcess = Start-Process (Join-Path $Server "openkogama-server.exe") -WorkingDirectory $Server -RedirectStandardOutput $serverLog -RedirectStandardError (Join-Path $Out "server-errors.log") -PassThru -NoNewWindow
for ($i = 0; $i -lt 60 -and -not ((Test-Path $serverLog) -and (Select-String -Path $serverLog -Pattern "http 8080" -Quiet)); $i++) { Start-Sleep 1 }
$world = (Invoke-RestMethod -Method Post "http://127.0.0.1:8080/api/worlds?name=City&template=city" -Body "").id
Write-Host "world $world"

$session = "http://127.0.0.1:8080/session?mode=play&world=$world&client=$Version&unity=$($entry.unityVersion)"
$package = "kogamaPackage:" + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($session))
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe.FullName
$psi.Arguments = "$package -popupwindow -logFile `"$(Join-Path $Out 'client.log')`""
$psi.WorkingDirectory = $exe.DirectoryName
$psi.UseShellExecute = $false
$psi.EnvironmentVariables["http_proxy"] = "http://127.0.0.1:8081"
$psi.EnvironmentVariables["no_proxy"] = "127.0.0.1,localhost"
$game = [System.Diagnostics.Process]::Start($psi)

$ready = $false
for ($i = 0; $i -lt 300; $i++) {
    Start-Sleep 1
    if (Select-String -Path $serverLog -Pattern "actor ready" -Quiet) { $ready = $true; break }
    if ($game.HasExited) { break }
}
Write-Host "ready $ready after $i s"
Start-Sleep 10

$game.Refresh()
$handle = $game.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) { throw "no game window" }
$scale = [Math]::Min(1.0, [Math]::Min($screen.Width / $Width, $screen.Height / $Height))
$renderWidth = [int][Math]::Round($Width * $scale)
$renderHeight = [int][Math]::Round($Height * $scale)
[Shot]::Size($handle, $renderWidth, $renderHeight)
Start-Sleep 8
$bitmap = [Shot]::Capture($handle)
Write-Host "captured $($bitmap.Width)x$($bitmap.Height)"
[Shot]::Save($bitmap, $Width, $Height, (Join-Path $Out "menu-$Version-${Width}x$Height.png"))

Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
if (-not $ready) { throw "client did not get ready" }
