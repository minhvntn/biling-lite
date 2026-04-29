param(
    [string]$TaskName = "ServerManagerBillingAgent",
    [string]$ServiceName = "ServerManagerBillingWatchdog"
)

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    throw "Please run this script as Administrator."
}

Write-Host "Stopping and deleting service $ServiceName ..."
& sc.exe stop $ServiceName | Out-Null
& sc.exe delete $ServiceName | Out-Null

Write-Host "Deleting scheduled task $TaskName ..."
& schtasks.exe /Delete /TN "$TaskName" /F | Out-Null

Write-Host "Uninstall completed."
