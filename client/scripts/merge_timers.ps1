$filePath = "i:\servermanagerbilling\client\src\Client.Agent.Wpf\App.xaml.cs"
$content = [System.IO.File]::ReadAllText($filePath)

# Pattern to match both old handlers
$pattern = '(?s)private async void BackgroundSyncTimer_Tick\(object\? sender, EventArgs e\)\s*\{.*?_isReadyAutoShutdownTickRunning = false;\s*\}\s*\}\s*private async void MemberUsageSyncTimer_Tick\(object\? sender, EventArgs e\)\s*\{.*?await EnforceMemberAutoLockIfNoRemainingTimeAsync\("PERIODIC"\);\s*\}'

$replacement = @'
/// <summary>
    /// Consolidated background timer (10s): handles auto-shutdown check + member usage sync.
    /// Merging 2 timers into 1 reduces UI thread context switches.
    /// </summary>
    private async void BackgroundSyncTimer_Tick(object? sender, EventArgs e)
    {
        // --- Auto-shutdown check ---
        if (!_isReadyAutoShutdownTickRunning)
        {
            _isReadyAutoShutdownTickRunning = true;
            try
            {
                await RefreshClientRuntimeSettingsIfDueAsync();
                await EvaluateReadyAutoShutdownAsync();
            }
            finally
            {
                _isReadyAutoShutdownTickRunning = false;
            }
        }

        // --- Member usage sync (merged from former _memberUsageSyncTimer) ---
        await SyncActiveMemberUsageAsync("PERIODIC", false);
        EvaluateMemberRemainingTimeWarnings();
        await EnforceMemberAutoLockIfNoRemainingTimeAsync("PERIODIC");
    }
'@

$newContent = [System.Text.RegularExpressions.Regex]::Replace($content, $pattern, $replacement, [System.Text.RegularExpressions.RegexOptions]::Singleline)

if ($newContent -ne $content) {
    [System.IO.File]::WriteAllText($filePath, $newContent)
    Write-Host "SUCCESS: Timer handlers merged"
} else {
    Write-Host "WARNING: Pattern not matched"
}
