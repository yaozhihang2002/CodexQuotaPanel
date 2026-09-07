param([int]$TargetProcessId, [string]$DataRoot)
$ErrorActionPreference = 'Stop'
$target = Get-CimInstance Win32_Process -Filter "ProcessId=$TargetProcessId"
if ($target.CommandLine -notlike '*CodexQuotaPanel*--isolated-smoke*') { throw 'Only isolated smoke instances may be tested.' }
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class RdpSmokeNative {
 public delegate bool Callback(IntPtr h, IntPtr p);
 [StructLayout(LayoutKind.Sequential)] public struct POINT {public int X,Y;}
 [DllImport("user32.dll")] static extern bool EnumWindows(Callback cb,IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr h,ref POINT p);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 public static Dictionary<string,IntPtr> Windows(uint pid) {
  var found=new Dictionary<string,IntPtr>();
  EnumWindows((h,p)=>{uint owner;GetWindowThreadProcessId(h,out owner);if(owner==pid){var s=new StringBuilder(256);GetWindowText(h,s,256);if(s.Length>0)found[s.ToString()]=h;}return true;},IntPtr.Zero);
  return found;
 }
 public static void Click(IntPtr h,int x,int y){
  var p=new POINT {X=x,Y=y}; ScreenToClient(h,ref p);
  var packed=new IntPtr((p.Y<<16)|(p.X&65535));
  PostMessageW(h,0x200,IntPtr.Zero,packed);
  PostMessageW(h,0x201,new IntPtr(1),packed);
  PostMessageW(h,0x202,IntPtr.Zero,packed);
 }
}
'@
$previous=[RdpSmokeNative]::SetThreadDpiAwarenessContext([IntPtr](-4))
try {
 function Get-Events { @(Get-Content (Join-Path $DataRoot 'display-recovery.log') | ForEach-Object { $_ | ConvertFrom-Json }) }
 function Wait-Until([scriptblock]$Condition,[string]$Label) {
  $deadline=[DateTime]::UtcNow.AddSeconds(12)
  do { if (& $Condition) { return }; Start-Sleep -Milliseconds 100 } while([DateTime]::UtcNow -lt $deadline)
  throw "Timed out: $Label"
 }
 function Get-Dashboard {
  $windows=[RdpSmokeNative]::Windows($TargetProcessId)
  foreach($entry in $windows.GetEnumerator()) {if($entry.Key -like 'Codex *details'){ return $entry.Value }}
  return [IntPtr]::Zero
 }
 function Read-Buttons([IntPtr]$Handle) {
  $root=[System.Windows.Automation.AutomationElement]::FromHandle($Handle)
  $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button)
  @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition))
 }
 function Click-Button([IntPtr]$Handle,[string]$Name) {
  $button=Read-Buttons $Handle | Where-Object { $_.Current.Name -eq $Name } | Select-Object -First 1
  if(!$button -or !$button.Current.IsEnabled -or $button.Current.IsOffscreen){throw "Button unavailable: $Name"}
  $r=$button.Current.BoundingRectangle
  [RdpSmokeNative]::Click($Handle,[int]($r.X+$r.Width/2),[int]($r.Y+$r.Height/2))
  "Native coordinate click: $Name ($r)"
 }
 Wait-Until { @(Get-Events | Where-Object Reason -eq 'opened').Count -gt 0 } 'startup open'
 $before=Get-Events | Where-Object Reason -eq 'opened' | Select-Object -Last 1
 $old=Get-Dashboard
 $replacementCount=@(Get-Events | Where-Object Reason -eq 'replace-after').Count
 $monitor=[RdpSmokeNative]::Windows($TargetProcessId)['CodexQuotaDisplayMonitor']
 if(!$monitor -or !$old){throw 'Native windows missing'}
 "Before: PID=$TargetProcessId HWND=$($old.ToString('X')) Orb=$($before.Orb) Anchor=$($before.Anchor)"
 # Events go only to the isolated test process, never to the installed app or OS session.
 [RdpSmokeNative]::PostMessageW($monitor,0x2B1,[IntPtr]4,[IntPtr]::Zero) | Out-Null
 Start-Sleep -Milliseconds 900
 if(@(Get-Events | Where-Object Reason -eq 'replace-after').Count -ne $replacementCount){throw 'Recovered while disconnected'}
 [RdpSmokeNative]::PostMessageW($monitor,0x2B1,[IntPtr]3,[IntPtr]::Zero) | Out-Null
 foreach($n in 1..5) { [RdpSmokeNative]::PostMessageW($monitor,0x7E,[IntPtr]32,[IntPtr]::Zero) | Out-Null }
 Wait-Until { @(Get-Events | Where-Object Reason -eq 'replace-after').Count -gt $replacementCount } 'visible surface replacement'
 $after=Get-Events | Where-Object Reason -eq 'replace-after' | Select-Object -Last 1
 if($after.Handle -eq $before.Handle -or [RdpSmokeNative]::IsWindow($old)){throw 'Old native window was not destroyed'}
 if($after.Orb -ne $before.Orb -or $after.Anchor -ne $before.Anchor){throw 'Orb anchor changed'}
 if([Math]::Abs($after.Native.Dpi/96.0-$after.Scaling) -gt .01){throw 'DPI mismatch'}
 $size=$after.Client -split ', '
 if([Math]::Abs([double]$size[0]*$after.Scaling-$after.Native.ClientWidth) -gt 2){throw 'Native/layout width mismatch'}
 $new=Get-Dashboard
 $root=[System.Windows.Automation.AutomationElement]::FromHandle($new)
 $text=@($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {$_.Current.Name}) -join '|'
 if($text -notmatch '44%'){throw 'Quota fixture was not preserved'}
 "After: HWND=$($new.ToString('X')) Native=$($after.Native.ClientWidth)x$($after.Native.ClientHeight) Scaling=$($after.Scaling); quota 44% preserved"
 $openedCount=@(Get-Events | Where-Object Reason -eq 'opened').Count
 Click-Button $new 'Collapse to orb'
 Wait-Until { ![RdpSmokeNative]::IsWindow($new) } 'collapse destroys remote surface'
 $collapsed=Get-Events | Where-Object Reason -eq 'discard-after-collapse' | Select-Object -Last 1
 if($collapsed.Orb -ne $before.Orb){throw 'Collapse moved orb'}
 $orbWindows=[RdpSmokeNative]::Windows($TargetProcessId)
 $orbEntry=$orbWindows.GetEnumerator() | Where-Object { $_.Key -ne 'CodexQuotaDisplayMonitor' -and [RdpSmokeNative]::IsWindowVisible($_.Value) } | Select-Object -First 1
 if(!$orbEntry){throw 'Orb is missing'}
 $orbElement=[System.Windows.Automation.AutomationElement]::FromHandle($orbEntry.Value)
 $orbRect=$orbElement.Current.BoundingRectangle
 [RdpSmokeNative]::Click($orbEntry.Value,[int]($orbRect.X+$orbRect.Width/2),[int]($orbRect.Y+$orbRect.Height/2))
 Wait-Until { @(Get-Events | Where-Object Reason -eq 'opened').Count -gt $openedCount } 'orb coordinate click opens detail'
 $reopened=Get-Events | Where-Object Reason -eq 'opened' | Select-Object -Last 1
 if($reopened.Anchor -ne $before.Orb){throw 'Reopen changed restore anchor'}
 $pointers=@(Get-Events | Where-Object Reason -eq 'pointer-pressed')
 if($pointers.Count -lt 1){throw 'No native pointer reached Avalonia'}
 "PASS: reconnect replacement, event coalescing, DPI/layout, data retention, native button click, collapse, orb click, anchor retention; process still running=$(!(Get-Process -Id $TargetProcessId).HasExited)"
} finally { [RdpSmokeNative]::SetThreadDpiAwarenessContext($previous) | Out-Null }


