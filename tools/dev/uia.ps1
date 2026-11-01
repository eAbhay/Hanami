# Tiny UI Automation helpers for driving Hanami's native dialogs in tests.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement

function Find-Window([string]$title, [int]$timeoutSec = 20) {
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $title
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $w = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if ($w) { return $w }
        Start-Sleep -Milliseconds 300
    }
    throw "window '$title' not found"
}

function Find-Child($parent, [string]$name, $type) {
    $c1 = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $name
    $c2 = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), $type
    $and = New-Object System.Windows.Automation.AndCondition $c1, $c2
    $e = $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $and)
    if (-not $e) { throw "control '$name' not found" }
    return $e
}

function Click($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
