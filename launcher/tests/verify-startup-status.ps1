# Source-level regression for status request scheduling. No live endpoint is contacted.
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '..\VanadreamsLauncher'
$main = [IO.File]::ReadAllText((Join-Path $source 'MainWindow.xaml.cs'))
$settings = [IO.File]::ReadAllText((Join-Path $source 'Pages\SettingsPage.xaml.cs'))
$menu = [IO.File]::ReadAllText((Join-Path $source 'Pages\MenuPage.xaml.cs'))
$startup = [regex]::Match($main, '(?s)private async void OnLoaded\(.*?\n        }').Value
if (-not $startup) { throw 'Startup handler not found.' }
if ([regex]::Matches($main, 'await State\.RefreshStatusAsync\(\);').Count -ne 1 -or
    -not $startup.Contains('await State.RefreshStatusAsync();')) {
    throw 'The main window must check status exactly once at startup.'
}
$once = $startup.IndexOf('Loaded -= OnLoaded;')
$firstAwait = $startup.IndexOf('await ')
if ($once -lt 0 -or $once -gt $firstAwait) {
    throw 'The startup handler must unsubscribe before yielding so another Loaded cannot repeat it.'
}
if ($main -match '_statusTimer|DispatcherTimer') {
    throw 'The main window must not schedule recurring status checks.'
}
if ($startup.IndexOf('await State.RefreshStatusAsync();') -gt $startup.IndexOf('await State.CheckClientUpdateAsync();')) {
    throw 'The startup status must be fetched before the client-update check.'
}
if ($menu -match 'RefreshStatusAsync|StatusClient\.FetchAsync') {
    throw 'Play must use the startup status, not issue another status request.'
}
if ($settings -match 'RefreshStatusAsync|StatusClient\.FetchAsync') {
    throw 'Saving Settings must not issue another status request.'
}
if (-not $settings.Contains('s.StatusUrl = StatusBox.Text.Trim();')) {
    throw 'The status URL setting must still be saved for the next launch.'
}
$callers = @(Get-ChildItem -LiteralPath $source -Filter '*.cs' -Recurse |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    Select-String -Pattern '\.RefreshStatusAsync\(')
if ($callers.Count -ne 1) { throw "Unexpected status callers: $($callers.Count), expected startup only." }
'Status scheduling: one startup check; no timer, Play, or Settings save refresh.'
'Loaded handler unsubscribes before its first await; startup status precedes the client-update check.'
'Other status callers: none.'
