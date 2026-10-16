# install.ps1 -- copies Hand Poser from dist\ to %LOCALAPPDATA%\Programs\HandPoser and puts its icon on the desktop.
# The in-game script is NOT put into any game here: the app's own button does that for the game it finds.
#   powershell -ExecutionPolicy Bypass -File install.ps1 [-From <folder with the built files>]
param([string]$From = "")
# from a source folder the built files are in dist\; from the download zip they sit next to this script
if ($From -eq "") { $From = if (Test-Path (Join-Path $PSScriptRoot "dist\HandPoser.exe")) { Join-Path $PSScriptRoot "dist" } else { $PSScriptRoot } }
$ErrorActionPreference = "Stop"
$to = Join-Path $env:LOCALAPPDATA "Programs\HandPoser"
New-Item -ItemType Directory -Force $to | Out-Null
foreach ($f in "HandPoser.exe", "hand_poser.lua", "hand_poser.ico") {
    if (Test-Path (Join-Path $From $f)) { Copy-Item (Join-Path $From $f) (Join-Path $to $f) -Force }
}
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath("Desktop")) "Hand Poser.lnk"))
$lnk.TargetPath = Join-Path $to "HandPoser.exe"
$lnk.WorkingDirectory = $to
$lnk.IconLocation = (Join-Path $to "hand_poser.ico") + ",0"
$lnk.Description = "Pose the player's hands in RE Engine games with sliders"
$lnk.Save()
Write-Output "Installed to $to, icon on the desktop."
