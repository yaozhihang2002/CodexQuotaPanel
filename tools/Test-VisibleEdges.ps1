param([Parameter(Mandatory)][int]$TargetProcessId)
$ErrorActionPreference='Stop'
$target=Get-CimInstance Win32_Process -Filter "ProcessId=$TargetProcessId"
if ($target.CommandLine -notlike '*CodexQuotaPanel*--isolated-smoke*') { throw 'Only isolated smoke instances may be tested.' }
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class VisibleEdgeTest {
 public delegate bool Callback(IntPtr h,IntPtr p);
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int L,T,R,B;}
 [StructLayout(LayoutKind.Sequential)] public struct Monitor {public int Size;public Rect Bounds,Work;public uint Flags;}
 [StructLayout(LayoutKind.Sequential)] public struct Point {public int X,Y;}
 [DllImport("user32.dll")] static extern bool EnumWindows(Callback cb,IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
 [DllImport("user32.dll")] static extern bool GetMonitorInfoW(IntPtr h,ref Monitor m);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
 [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr h,ref Point p);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h,int a,out Rect r,int size);
 public static Dictionary<string,IntPtr> Windows(uint pid){var d=new Dictionary<string,IntPtr>();EnumWindows((h,p)=>{uint owner;GetWindowThreadProcessId(h,out owner);if(owner==pid){var s=new StringBuilder(256);GetWindowText(h,s,256);if(s.Length>0)d[s.ToString()]=h;}return true;},IntPtr.Zero);return d;}
 public static Rect Native(IntPtr h){Rect r;GetWindowRect(h,out r);return r;}
 public static Rect Visible(IntPtr h){Rect r;if(DwmGetWindowAttribute(h,9,out r,16)!=0)throw new Exception("DWM frame unavailable");return r;}
 public static Rect Work(IntPtr h){var m=new Monitor{Size=Marshal.SizeOf(typeof(Monitor))};GetMonitorInfoW(MonitorFromWindow(h,2),ref m);return m.Work;}
 public static void Move(IntPtr h,int x,int y){SetWindowPos(h,IntPtr.Zero,x,y,0,0,0x15);}
 public static void DisplayChanged(IntPtr h){PostMessageW(h,0x7E,new IntPtr(32),IntPtr.Zero);}
 public static void Click(IntPtr h,int x,int y){var p=new Point{X=x,Y=y};ScreenToClient(h,ref p);var l=new IntPtr((p.Y<<16)|(p.X&65535));PostMessageW(h,0x200,IntPtr.Zero,l);PostMessageW(h,0x201,new IntPtr(1),l);PostMessageW(h,0x202,IntPtr.Zero,l);}
}
'@
$previous=[VisibleEdgeTest]::SetThreadDpiAwarenessContext([IntPtr](-4))
try {
 $windows=[VisibleEdgeTest]::Windows($TargetProcessId)
 $orb=$windows.GetEnumerator() | Where-Object { $_.Key -notlike '*details*' -and $_.Key -ne 'CodexQuotaDisplayMonitor' -and [VisibleEdgeTest]::IsWindowVisible($_.Value) } | Select-Object -First 1
 if(!$orb){throw 'Start the isolated instance in orb mode'}
 $orb=$orb.Value
 $work=[VisibleEdgeTest]::Work($orb)
 foreach($edge in @('right','left','right-bottom','left-bottom','right')) {
  $r=[VisibleEdgeTest]::Native($orb)
  $x=if($edge.StartsWith('right')){$work.R-($r.R-$r.L)}else{$work.L}
  $y=if($edge.EndsWith('bottom')){$work.B-($r.B-$r.T)}else{$work.T+100}
  [VisibleEdgeTest]::Move($orb,$x,$y)
  Start-Sleep -Milliseconds 250
  $before=[VisibleEdgeTest]::Native($orb)
  [VisibleEdgeTest]::Click($orb,[int](($before.L+$before.R)/2),[int](($before.T+$before.B)/2))
  Start-Sleep -Milliseconds 1400
  $panel=([VisibleEdgeTest]::Windows($TargetProcessId).GetEnumerator() | Where-Object Key -like '*details' | Select-Object -First 1).Value
  if(!$panel){throw 'Dashboard did not open'}
  $visible=[VisibleEdgeTest]::Visible($panel)
  $native=[VisibleEdgeTest]::Native($panel)
  $gap=if($edge.StartsWith('right')){$work.R-$visible.R}else{$visible.L-$work.L}
  "Edge=$edge gap=$gap native=[$($native.L),$($native.R)] visible=[$($visible.L),$($visible.R)] work=[$($work.L),$($work.R)]"
  if([Math]::Abs($gap) -gt 1){throw "Visible $edge edge has a $gap pixel gap"}
  if($edge.EndsWith('bottom') -and [Math]::Abs($work.B-$visible.B) -gt 1){throw 'Visible bottom edge is not flush'}
  $oldPanel=$panel
  $monitor=[VisibleEdgeTest]::Windows($TargetProcessId)['CodexQuotaDisplayMonitor']
  [VisibleEdgeTest]::DisplayChanged($monitor)
  Start-Sleep -Milliseconds 1500
  $panel=([VisibleEdgeTest]::Windows($TargetProcessId).GetEnumerator() | Where-Object Key -like '*details' | Select-Object -First 1).Value
  $restored=[VisibleEdgeTest]::Visible($panel)
  "Recovery: old=$oldPanel new=$panel before=[$($visible.L),$($visible.T),$($visible.R),$($visible.B)] after=[$($restored.L),$($restored.T),$($restored.R),$($restored.B)]"
  if($panel -eq $oldPanel -or [Math]::Abs($restored.L-$visible.L) -gt 1 -or [Math]::Abs($restored.R-$visible.R) -gt 1 -or [Math]::Abs($restored.T-$visible.T) -gt 1){throw 'Display recovery changed visible edge or monitor'}
  'Display recovery retained visible placement and monitor'
  $root=[System.Windows.Automation.AutomationElement]::FromHandle($panel)
  $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'Collapse to orb')
  $button=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
  if(!$button){throw 'Collapse button missing'}
  $b=$button.Current.BoundingRectangle
  [VisibleEdgeTest]::Click($panel,[int]($b.X+$b.Width/2),[int]($b.Y+$b.Height/2))
  Start-Sleep -Milliseconds 600
  $after=[VisibleEdgeTest]::Native($orb)
  if(![VisibleEdgeTest]::IsWindowVisible($orb) -or $before.L -ne $after.L -or $before.T -ne $after.T){throw 'Collapse failed or orb anchor moved'}
 }
 'PASS: native visible left/right alignment, native coordinate collapse, unchanged orb anchor'
} finally { [VisibleEdgeTest]::SetThreadDpiAwarenessContext($previous) | Out-Null }
