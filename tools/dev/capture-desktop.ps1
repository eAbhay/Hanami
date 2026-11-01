param([string]$Out = "$PSScriptRoot\desktop.png")
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Cap {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string w);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string w);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public struct RECT { public int L, T, R, B; }
  public static string Cls(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
}
"@
[Cap]::SetProcessDPIAware() | Out-Null
$progman = [Cap]::FindWindow("Progman", [NullString]::Value)
# list Progman's children in z-order
$c = [Cap]::GetWindow($progman, 5)  # GW_CHILD
while ($c -ne [IntPtr]::Zero) { "child: {0} {1}" -f $c, [Cap]::Cls($c); $c = [Cap]::GetWindow($c, 2) }
$r = New-Object Cap+RECT
[Cap]::GetWindowRect($progman, [ref]$r) | Out-Null
$w = $r.R - $r.L; $h = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[Cap]::PrintWindow($progman, $hdc, 2) | Out-Null   # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc); $g.Dispose()
$small = New-Object System.Drawing.Bitmap $bmp, ([int]($w/2)), ([int]($h/2))
$small.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"saved $Out ($w x $h)"

