# Synthetic input for screensaver tests: -Move (mouse wiggle, ends where it started) or -Key (tap Shift).
# -Crop saves a full-resolution crop of the screen centre.
param([switch]$Move, [switch]$Key, [switch]$Click, [string]$Crop)
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SI {
  [StructLayout(LayoutKind.Explicit, Size=40)] public struct MIN { [FieldOffset(0)] public uint type; [FieldOffset(8)] public int dx; [FieldOffset(12)] public int dy; [FieldOffset(20)] public uint flags; }
  [StructLayout(LayoutKind.Explicit, Size=40)] public struct KIN { [FieldOffset(0)] public uint type; [FieldOffset(8)] public ushort vk; [FieldOffset(12)] public uint flags; }
  [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, MIN[] i, int size);
  [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, KIN[] i, int size);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public static void Move(int dx, int dy) { var i = new MIN[1]; i[0].type = 0; i[0].dx = dx; i[0].dy = dy; i[0].flags = 1; if (SendInput(1, i, 40) != 1) throw new Exception("move failed " + Marshal.GetLastWin32Error()); }
  public static void Btn(uint f) { var i = new MIN[1]; i[0].type = 0; i[0].flags = f; if (SendInput(1, i, 40) != 1) throw new Exception("btn failed"); }
  public static void Tap(ushort vk) { var i = new KIN[2]; i[0].type = 1; i[0].vk = vk; i[1].type = 1; i[1].vk = vk; i[1].flags = 2; if (SendInput(2, i, 40) != 2) throw new Exception("key failed " + Marshal.GetLastWin32Error()); }
}
"@
[SI]::SetProcessDPIAware() | Out-Null
if ($Move) { foreach ($s in 1..6) { [SI]::Move(40, 25); Start-Sleep -Milliseconds 60 }; foreach ($s in 1..6) { [SI]::Move(-40, -25); Start-Sleep -Milliseconds 60 }; "moved mouse around" }
if ($Click) { [SI]::Btn(2); Start-Sleep -Milliseconds 80; [SI]::Btn(4); "clicked" }
if ($Key) { [SI]::Tap(0x10); "tapped Shift" }
if ($Crop) {
  $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $w = [int]($b.Width * 0.6); $h = [int]($b.Height * 0.42)
  $x = [int](($b.Width - $w) / 2); $y = [int]($b.Height * 0.16)
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
  $g.Dispose(); $bmp.Save($Crop, [System.Drawing.Imaging.ImageFormat]::Png); "saved $Crop"
}

