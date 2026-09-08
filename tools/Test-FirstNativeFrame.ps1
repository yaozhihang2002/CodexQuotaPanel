param([Parameter(Mandatory)][string]$ExecutablePath,[Parameter(Mandatory)][string]$DataRoot,[Parameter(Mandatory)][string]$OutputPath,[switch]$AllowKnownFailure)
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
public static class FirstFrameProbe {
 public delegate bool Callback(IntPtr h,IntPtr p);
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int L,T,R,B;}
 [DllImport("user32.dll")] static extern bool EnumWindows(Callback c,IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(IntPtr h,out uint key,out byte alpha,out uint flags);
 [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll")] static extern int GetSystemMetrics(int n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h,StringBuilder text,int length);
 public static IntPtr Find(uint pid,bool orb){IntPtr found=IntPtr.Zero;EnumWindows((h,p)=>{uint owner;GetWindowThreadProcessId(h,out owner);Rect r;if(owner==pid&&GetWindowRect(h,out r)){int w=r.R-r.L, height=r.B-r.T;var title=new StringBuilder(256);GetWindowTextW(h,title,256);if(orb ? w>=56&&w<=240&&height>=56&&height<=240&&IsWindowVisible(h) : w>=350&&height>=450&&title.ToString().StartsWith("Codex "))found=h;}return true;},IntPtr.Zero);return found;}
 public static string[] Capture(uint pid){
  var old=SetThreadDpiAwarenessContext(new IntPtr(-4));
  try{
   var orb=Find(pid,true);if(orb==IntPtr.Zero)throw new Exception("No isolated orb");Rect o;GetWindowRect(orb,out o);
   var click=new IntPtr((((o.B-o.T)/2)<<16)|((o.R-o.L)/2));
   var rows=new List<string>{"ms,remote,handle,visible,alpha,dpi,x,y,width,height,orbVisible"};var watch=Stopwatch.StartNew();bool clicked=false;
   while(watch.ElapsedMilliseconds<1800){
    if(!clicked&&watch.ElapsedMilliseconds>=100){PostMessageW(orb,0x201,new IntPtr(1),click);PostMessageW(orb,0x202,IntPtr.Zero,click);clicked=true;}
    var h=Find(pid,false);Rect r=default(Rect);GetWindowRect(h,out r);uint key,flags;byte alpha;
    var a=GetLayeredWindowAttributes(h,out key,out alpha,out flags)?(int)alpha:-1;
    rows.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0:F2},{1},{2:X},{3},{4},{5},{6},{7},{8},{9},{10}",watch.Elapsed.TotalMilliseconds,GetSystemMetrics(0x1000),h.ToInt64(),IsWindowVisible(h),a,GetDpiForWindow(h),r.L,r.T,r.R-r.L,r.B-r.T,IsWindowVisible(orb)));
    System.Threading.Thread.Sleep(2);
   }return rows.ToArray();
  }finally{SetThreadDpiAwarenessContext(old);}
 }
}
'@
$env:CODEXQUOTA_ALLOW_ISOLATED_SMOKE='1'
$env:CODEXQUOTA_SMOKE_DATA_ROOT=[IO.Path]::GetFullPath($DataRoot)
$env:CODEX_HOME=Join-Path $env:CODEXQUOTA_SMOKE_DATA_ROOT 'empty-codex-home'
$env:CODEX_CLI_PATH='C:\Windows\System32\cmd.exe'
New-Item -ItemType Directory -Path $env:CODEX_HOME -Force | Out-Null
$p=Start-Process -FilePath $ExecutablePath -ArgumentList '--isolated-smoke' -WindowStyle Hidden -PassThru
try {
 $deadline=[DateTime]::UtcNow.AddSeconds(12)
 while([FirstFrameProbe]::Find($p.Id,$true) -eq [IntPtr]::Zero){if([DateTime]::UtcNow -gt $deadline){throw 'Startup timed out'};Start-Sleep -Milliseconds 100}
 Start-Sleep -Milliseconds 500
 $rows=[FirstFrameProbe]::Capture($p.Id)
 [IO.File]::WriteAllLines([IO.Path]::GetFullPath($OutputPath),$rows)
 $samples=$rows | ConvertFrom-Csv
 $visible=@($samples | Where-Object visible -eq 'True')
 $unprotected=@($visible | Where-Object alpha -eq '-1')
 $first=$visible | Select-Object -First 1
 $last=$samples | Select-Object -Last 1
 [pscustomobject]@{Samples=$samples.Count;UnprotectedVisibleSamples=$unprotected.Count;FirstVisibleAlpha=$first.alpha;Dpi=$last.dpi;Remote=$last.remote;FinalAlpha=$last.alpha;FinalOrbVisible=$last.orbVisible;Output=$OutputPath} | ConvertTo-Json -Compress
 if(!$AllowKnownFailure -and ($unprotected.Count -gt 0 -or $first.alpha -ne '0' -or $last.alpha -ne '255' -or $last.orbVisible -ne 'False')){throw 'First-frame opacity gate failed'}
}finally {if(!$p.HasExited){Stop-Process -Id $p.Id}}
