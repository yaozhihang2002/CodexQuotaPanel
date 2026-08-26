[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$ProcessId,
    [Parameter(Mandatory)][string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('CodexQuotaCapture.Native' -as [type])) {
    Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
namespace CodexQuotaCapture {
  public static class Native {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr state);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr state);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    public static int DarkMode(IntPtr hwnd) { int value; return DwmGetWindowAttribute(hwnd, 20, out value, 4) == 0 ? value : -1; }
    public static IntPtr[] WindowsFor(uint pid) {
      var result = new List<IntPtr>();
      EnumWindows((hwnd, state) => { uint owner; GetWindowThreadProcessId(hwnd, out owner);
        if (owner == pid && IsWindowVisible(hwnd)) result.Add(hwnd); return true; }, IntPtr.Zero);
      return result.ToArray();
    }
  }
}
'@
}

$fullOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $fullOutput -Force | Out-Null
$captured = @()
$index = 0
foreach ($handle in [CodexQuotaCapture.Native]::WindowsFor([uint32]$ProcessId)) {
    $rect = New-Object CodexQuotaCapture.Native+RECT
    if (-not [CodexQuotaCapture.Native]::GetWindowRect($handle, [ref]$rect)) { continue }
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -lt 16 -or $height -lt 16) { continue }
    $titleBuffer = [Text.StringBuilder]::new(512)
    [void][CodexQuotaCapture.Native]::GetWindowText($handle, $titleBuffer, $titleBuffer.Capacity)
    $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    try { $ok = [CodexQuotaCapture.Native]::PrintWindow($handle, $hdc, 2) }
    finally { $graphics.ReleaseHdc($hdc); $graphics.Dispose() }
    if (-not $ok) { $bitmap.Dispose(); continue }
    $path = Join-Path $fullOutput ("window-{0}.png" -f $index++)
    $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    $captured += [pscustomobject]@{ Path=$path; Handle=$handle.ToInt64(); DarkMode=[CodexQuotaCapture.Native]::DarkMode($handle); Title=$titleBuffer.ToString(); X=$rect.Left; Y=$rect.Top; Width=$width; Height=$height }
}
$captured | ConvertTo-Json -Compress
