# Asks a running Hanami (dev or installed) to quit cleanly, e.g. before rebuilding (the exe is locked while it runs).
try {
    $q = [System.Threading.EventWaitHandle]::OpenExisting('Local\Hanami.Quit'); $q.Set() | Out-Null; $q.Dispose()
    for ($i = 0; $i -lt 20 -and (Get-Process Hanami -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 250 }
    "Hanami quit"
} catch { "Hanami not running" }
