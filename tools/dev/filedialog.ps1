# Fills the file-name box of a common Open/Save dialog owned by the "Hanami" settings window and presses OK.
# Usage: filedialog.ps1 -DialogTitle "Import Hanami pack" -Path C:\some\file.hanamipack
# (Start the bridge call that opens the dialog first, e.g. in a background job — it blocks until the dialog closes.)
param([string]$DialogTitle, [string]$Path)
. "$PSScriptRoot\uia.ps1"
Add-Type -Namespace HanamiDlg -Name U -MemberDefinition @'
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, string l);
[DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
[DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string w);
'@
$main = Find-Window "Hanami"
$nameCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $DialogTitle
$dlg = $null
for ($i = 0; $i -lt 30 -and -not $dlg; $i++) { $dlg = $main.FindFirst([System.Windows.Automation.TreeScope]::Children, $nameCond); if (-not $dlg) { Start-Sleep -Milliseconds 300 } }
if (-not $dlg) { throw "dialog '$DialogTitle' not open" }
$h = [IntPtr]$dlg.Current.NativeWindowHandle
# The file-name box is ComboBoxEx32 (id 1148 in Open dialogs, 1001 in Save dialogs) → ComboBox → Edit.
$edit = [IntPtr]::Zero
foreach ($id in 1148, 1001) {
    $cbx = [HanamiDlg.U]::GetDlgItem($h, $id)
    if ($cbx -eq [IntPtr]::Zero) { continue }
    $cb = [HanamiDlg.U]::FindWindowEx($cbx, [IntPtr]::Zero, "ComboBox", [NullString]::Value)
    $edit = [HanamiDlg.U]::FindWindowEx($(if ($cb -ne [IntPtr]::Zero) { $cb } else { $cbx }), [IntPtr]::Zero, "Edit", [NullString]::Value)
    if ($edit -ne [IntPtr]::Zero) { break }
}
if ($edit -eq [IntPtr]::Zero) { throw "file name box not found" }
[HanamiDlg.U]::SendMessage($edit, 0x000C, [IntPtr]::Zero, $Path) | Out-Null                               # WM_SETTEXT
[HanamiDlg.U]::PostMessage([HanamiDlg.U]::GetDlgItem($h, 1), 0xF5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null  # BM_CLICK on OK
"submitted $Path"
