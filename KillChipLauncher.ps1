Get-Process | Where-Object { $_.Modules.FileName -like "*ChipLauncher*" } | Stop-Process -Force -ErrorAction SilentlyContinue
