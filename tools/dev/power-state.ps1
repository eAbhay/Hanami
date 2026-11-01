param([switch]$Hold, [int]$Seconds = 12)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class PR {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  public struct REASON_CONTEXT { public uint Version; public uint Flags; public string SimpleReasonString; }
  [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr PowerCreateRequest(ref REASON_CONTEXT ctx);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern bool PowerSetRequest(IntPtr h, int type);
  [DllImport("powrprof.dll")] public static extern uint CallNtPowerInformation(int level, IntPtr inBuf, uint inLen, out uint outBuf, uint outLen);
  public static uint State() { uint s; CallNtPowerInformation(16, IntPtr.Zero, 0, out s, 4); return s; }
}
"@
if ($Hold) {
  $ctx = New-Object PR+REASON_CONTEXT; $ctx.Version = 0; $ctx.Flags = 1; $ctx.SimpleReasonString = "test video playback"
  $h = [PR]::PowerCreateRequest([ref]$ctx)
  [PR]::PowerSetRequest($h, 0) | Out-Null   # PowerRequestDisplayRequired
  "holding display request"
  Start-Sleep $Seconds
} else {
  "SystemExecutionState = 0x{0:X}" -f [PR]::State()
}
