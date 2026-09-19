$desktopPath = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktopPath "Guardian.lnk"
$targetExe = "C:\Users\kids\.gemini\antigravity-ide\scratch\Guardian\GuardianWpf\bin\Debug\net10.0-windows\GuardianWpf.exe"
$workingDir = "C:\Users\kids\.gemini\antigravity-ide\scratch\Guardian\GuardianWpf\bin\Debug\net10.0-windows"

$wshShell = New-Object -ComObject WScript.Shell
$shortcut = $wshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $targetExe
$shortcut.WorkingDirectory = $workingDir
$shortcut.Description = "Guardian Parental Control Desktop App"
$shortcut.Save()

Write-Host "Desktop shortcut created successfully at: $shortcutPath"
