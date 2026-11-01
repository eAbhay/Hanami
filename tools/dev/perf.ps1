# Measures Hanami (host + its WebView2 processes) CPU for a few wallpaper/effect combos by
# sending config messages straight into the wallpaper page via CDP.
param([int]$Seconds = 8)
Set-Location (Join-Path $PSScriptRoot "..\..")
$env:HANAMI_DEBUG_PORT = "9333"
function Cpu {
    $ids = (Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object { $_.CommandLine -like '*Hanami\WebView2*' }).ProcessId
    (Get-Process -Id $ids | Measure-Object CPU -Sum).Sum + (Get-Process Hanami).CPU
}
$combos = @(
    @{ name = "twilight + none";  wp = "{id:'builtin:twilight',name:'t',type:'builtin',scene:'twilight',fit:'cover'}"; fx = "none" },
    @{ name = "twilight + sakura"; wp = "{id:'builtin:twilight',name:'t',type:'builtin',scene:'twilight',fit:'cover'}"; fx = "sakura" },
    @{ name = "night + none";     wp = "{id:'builtin:night',name:'n',type:'builtin',scene:'night',fit:'cover'}"; fx = "none" },
    @{ name = "video + none";     wp = "{id:'demo-sea',name:'v',type:'video',src:'https://library.hanami/wallpapers/demo-sea/media.mp4',fit:'cover'}"; fx = "none" },
    @{ name = "video + sakura";   wp = "{id:'demo-sea',name:'v',type:'video',src:'https://library.hanami/wallpapers/demo-sea/media.mp4',fit:'cover'}"; fx = "sakura" },
    @{ name = "video + snow";     wp = "{id:'demo-sea',name:'v',type:'video',src:'https://library.hanami/wallpapers/demo-sea/media.mp4',fit:'cover'}"; fx = "snow" },
    @{ name = "video + fireflies"; wp = "{id:'demo-sea',name:'v',type:'video',src:'https://library.hanami/wallpapers/demo-sea/media.mp4',fit:'cover'}"; fx = "fireflies" }
)
foreach ($c in $combos) {
    $js = "(()=>{window.chrome.webview.dispatchEvent(new MessageEvent('message',{data:{type:'config',wallpaper:$($c.wp),effect:{preset:'$($c.fx)',density:1,speed:1,size:1,opacity:1,wind:0,interaction:'repel',interactionStrength:1,interactionRadius:160,clickBurst:true},fpsCap:60,mode:'full'}})); return 'ok'})()"
    node tools\cdp.mjs "wallpaper/" $js | Out-Null
    Start-Sleep 3
    $a = Cpu; Start-Sleep $Seconds; $b = Cpu
    "{0,-20} {1,5:N1}% of one core" -f $c.name, (($b - $a) / $Seconds * 100)
}


