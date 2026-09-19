$desktopPath = [Environment]::GetFolderPath('Desktop')
$wshShell = New-Object -ComObject WScript.Shell

# 1. Desktop App Shortcut
$appExe = "C:\Users\kids\.gemini\antigravity-ide\scratch\Guardian\GuardianWpf\bin\Debug\net10.0-windows\GuardianWpf.exe"
$appWorkingDir = "C:\Users\kids\.gemini\antigravity-ide\scratch\Guardian\GuardianWpf\bin\Debug\net10.0-windows"

$appShortcuts = @(
    (Join-Path $desktopPath "Guardian.lnk"),
    (Join-Path $desktopPath "Guardian Parental Controls.lnk")
)

foreach ($path in $appShortcuts) {
    if (Test-Path $path) { Remove-Item $path -Force }
    $s = $wshShell.CreateShortcut($path)
    $s.TargetPath = $appExe
    $s.WorkingDirectory = $appWorkingDir
    $s.Description = "Guardian Windows Protection App"
    $s.Save()
}

# 2. Web Server Shortcut
$serverScript = "C:\Users\kids\.gemini\antigravity-ide\scratch\Guardian\Start_Guardian_Web_Server.cmd"
$serverWorkingDir = "C:\Users\kids\.gemini\antigravity-ide\scratch\Guardian"
$serverShortcut = Join-Path $desktopPath "Start Guardian Web Server.lnk"

if (Test-Path $serverShortcut) { Remove-Item $serverShortcut -Force }
$s2 = $wshShell.CreateShortcut($serverShortcut)
$s2.TargetPath = $serverScript
$s2.WorkingDirectory = $serverWorkingDir
$s2.Description = "Start Guardian Parent Web Dashboard Server"
$s2.Save()

Write-Host "All desktop shortcuts created successfully!"
