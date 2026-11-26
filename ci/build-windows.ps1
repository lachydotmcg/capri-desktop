param([string]$OutputDirectory, [switch]$SmokeTest, [int]$SmokeSeconds = 60, [switch]$Store, [string[]]$Only, [string[]]$Group, [switch]$Steps, [int]$Step = -1, [switch]$Suggest)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Split-SmokeList([string[]]$items, [string]$flag)
{
    $list = @()
    if (-not $items) { return ,$list }
    foreach ($item in $items)
    {
        if ($null -eq $item) { continue }
        foreach ($part in $item.Split(','))
        {
            $trimmed = $part.Trim()
            if (-not $trimmed) { throw "$flag needs non-empty names." }
            $list += $trimmed
        }
    }
    return ,$list
}

function Get-SuggestedSmokeGroups([string]$repoRoot)
{
    $groups = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot '.git')) -or -not (Get-Command git -ErrorAction SilentlyContinue)) { return ,$groups }
    $changed = @()
    $changed += & git -C $repoRoot diff --name-only HEAD 2>$null
    $changed += & git -C $repoRoot ls-files --others --exclude-standard 2>$null
    $rules = @(
        @{ Pattern = '(^|/)(Voice|Ears|MicMix|CableSetup|Dictation|Hearing|Call|Husk|WatchAlong|Play)'; Group = 'Voice' },
        @{ Pattern = '(^|/)(Brain|PlanBrain|Care|Probes|Reminders|Leaks|LeakChecks|Language|RudeWords|Spend|Lite|SelfKnow|BetterModels|MediumChecks|FreeKeyChecks|Personal|Notice|ErrorChecks|ToolChecks|FollowUps|Crisis|Memory)'; Group = 'Brain' },
        @{ Pattern = '(^|/)(Mainframe|Talk|Workspace|FileEditor|FileChecks|FirstRun|Onboarding|Minis|Chess|Icons|ChaosScreen|Arrange|TreeEyes|TipChecks|SearchChecks|ArtColors|Neon|Companion|UninstallSettings|FileKeeper|Gifs|Gif|Timeline)'; Group = 'UI' },
        @{ Pattern = '(^|/)(Emotes|Ribbons|Animations|Ledges|Roam|Chime|Reactions|DoorIntro|ArtIntros)|^assets/(sprites|intros|emotes)/'; Group = 'Animations' },
        @{ Pattern = '(^|/)(Mod|Plugin|Mcp|StarterPlugins|ServiceLogos|Mail|ToolMods|Skin|Skill|SoundsLike)'; Group = 'Mods' },
        @{ Pattern = '(^|/)(Game|Hands|Computer|Ocr|Clip|ScrollForMe|Doomscroll|Weather|FileFinder|Files|Commands|Playlist|Favourites|Websites|Ink|Clones|Squad|Her|Frames|Agent|Mixer|Usage|Network|Presence|Player)'; Group = 'Games' },
        @{ Pattern = '(^|/)(Remote|RoomHost|CapriCamera)'; Group = 'Remote' }
    )
    foreach ($path in ($changed | Select-Object -Unique))
    {
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $norm = $path.Replace('\', '/')
        foreach ($rule in $rules)
        {
            if ($norm -match $rule.Pattern -and -not $groups.Contains($rule.Group)) { $groups.Add($rule.Group) }
        }
    }
    return ,$groups
}

$onlyList = Split-SmokeList $Only '-Only'
$groupList = Split-SmokeList $Group '-Group'
if ($Steps -and ($onlyList.Count -gt 0 -or $groupList.Count -gt 0 -or $Step -ge 0)) { throw '-Steps cannot be combined with -Only, -Group or -Step.' }
if ($Step -ge 0 -and ($onlyList.Count -gt 0 -or $groupList.Count -gt 0)) { throw '-Step cannot be combined with -Only or -Group.' }
if ($Suggest)
{
    $suggested = Get-SuggestedSmokeGroups $root
    if ($suggested.Count -gt 0)
    {
        $joined = $suggested -join ','
        Write-Output "Suggested smoke groups for local changes: $joined"
        Write-Output "Run: ./ci/build-windows.ps1 -SmokeTest -Group $joined"
    }
    else { Write-Output 'No changed files mapped to a specific group; run -Group <name>, -Steps or full -SmokeTest.' }
    if (-not $SmokeTest) { return }
}
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'
$winmd = Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\UnionMetadata') -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\d+(\.\d+)+$' } | Sort-Object { [version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName 'Windows.winmd' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not (Test-Path -LiteralPath $compiler)) { throw 'csc.exe not found. Capri needs the .NET Framework 4.8 that comes with Windows 10 and 11.' }
if (-not $winmd) { throw 'Windows.winmd not found. Install the Windows 10 or 11 SDK.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root 'out\windows' }
$out = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $out -Force | Out-Null
$app = Join-Path $out 'Capri.exe'
$setup = Join-Path $out 'Capri-Setup.exe'
$icon = Join-Path $root 'assets\chip.ico'
$shared = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', '/warn:4', ('/win32manifest:' + (Join-Path $root 'app\app.manifest')), ('/win32icon:' + $icon), '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll')
$references = @('/reference:System.Runtime.Serialization.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll', '/reference:System.Security.dll', ('/reference:' + (Join-Path $wpf 'UIAutomationClient.dll')), ('/reference:' + (Join-Path $wpf 'UIAutomationTypes.dll')), ('/reference:' + (Join-Path $wpf 'WindowsBase.dll')), ('/reference:' + (Join-Path $wpf 'PresentationCore.dll')), ('/reference:' + (Join-Path $wpf 'System.Speech.dll')), ('/reference:' + $winmd), ('/reference:' + (Join-Path $framework 'System.Runtime.dll')), ('/reference:' + (Join-Path $framework 'System.Runtime.WindowsRuntime.dll')), ('/reference:' + (Join-Path $framework 'System.Runtime.InteropServices.WindowsRuntime.dll')))
$resources = @(('/resource:' + $icon + ',Capri.Chip.ico'), ('/resource:' + (Join-Path $root 'assets\chip.png') + ',Capri.Chip.png'), ('/resource:' + (Join-Path $root 'assets\fonts\ShareTechMono-Regular.ttf') + ',Capri.Font.Mono.ttf'))
$resources += ('/resource:' + (Join-Path $root 'assets\fonts\CapriMainframe-Bold.ttf') + ',Capri.Font.Title.ttf')
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\words') -Filter *.txt | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Words.' + $_.Name }
$resources += ('/resource:' + (Join-Path $root 'assets\cursor.png') + ',Capri.Cursor.png')
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\her-computer') -Filter *.png | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Her.' + $_.Name }
foreach ($file in @('idle.png', 'speaking.png', 'thinking.png', 'intro-30.zip'))
{
    $resources += ('/resource:' + (Join-Path $root ('assets\holocall\' + $file)) + ',Capri.Holo.' + $file)
}
if (-not $Store)
{
    foreach ($file in @('companion.mjs', 'package.json', 'package-lock.json'))
    {
        $resources += ('/resource:' + (Join-Path $root ('discord-companion\' + $file)) + ',Capri.Discord.' + $file)
    }
}
# The Store copy carries no driver: policy 10.1.5 leaves non-Microsoft drivers out of what an app may bring in.
if (-not $Store) { $resources += ('/resource:' + (Join-Path $root 'assets\vbcable\VBCABLE_Driver_Pack45.zip') + ',Capri.VBCABLE.zip') }
if (-not $Store)
{
    $camera = Join-Path $out 'camera'
    if (& (Join-Path $PSScriptRoot 'build-camera.ps1') -OutputDirectory $camera) { foreach ($bits in @('64', '32')) { $resources += ('/resource:' + (Join-Path $camera ('CapriCamera' + $bits + '.dll')) + ',Capri.Camera' + $bits + '.dll') } }
}
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\sprites') -Filter *.png | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Sprite.' + $_.Name }
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\intros') -Recurse -Filter *.png | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Intro.' + $_.Directory.Name + '.' + $_.Name }
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\emotes') -Filter *.zip | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Emote.' + $_.Name }
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\voice') -Filter *.wav | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Voice.' + $_.Name }
$sources = @('Capri', 'Asides', 'Bolts', 'Commentary', 'Lookout', 'Brain', 'Leaks', 'PlanBrain', 'Workspace', 'WorkspaceWindow', 'WorkspaceParts', 'WorkspaceLook', 'WorkspaceWords', 'Talk', 'Ribbons', 'Emotes', 'Mainframe', 'MainframeKeys', 'MainframeMods', 'MainframeModsWindow', 'MainframeSlide', 'Mods', 'ModDirectory', 'ModApi', 'CodeMods', 'DoorIntro', 'ArtIntros', 'MainframeIntros', 'ModHooks', 'ModMusic', 'SoundsLike', 'SkinMods', 'SkinMoves', 'SkillMods', 'PluginMods', 'Plugins', 'PluginJson', 'Retention', 'Mcp', 'ModFloor', 'McpOnline', 'McpSignIn', 'PluginChecks', 'PluginCatalogChecks', 'MainframePlugins', 'PluginArt', 'ServiceLogos', 'MainframeMail', 'MailMime', 'MailBox', 'MailSmtp', 'MailTools', 'MailPhish', 'MailWatch', 'MailWatchSettings', 'MailChecks', 'StarterPlugins', 'ArtColors', 'MainframeTips', 'TipChecks', 'MainframeSearch', 'SearchChecks', 'MediumChecks', 'FreeKeyChecks', 'FirstRun', 'FirstRunChecks', 'PersonalSettings', 'Probes', 'PersonalChecks', 'Neon', 'Notice', 'Engine', 'Ledges', 'Voice', 'Ears', 'MicMix', 'CableSetup', 'Packaged', 'Dictation', 'HearingStatus', 'Agent', 'Hands', 'GameHud', 'Computer', 'TreeEyes', 'TreeEyesChecks', 'Arrange', 'Mixer', 'Usage', 'Network', 'Play', 'VoicePack', 'Startup', 'Presence', 'Player', 'Ocr', 'GameEars', 'GameNotes', 'GameKeys', 'GameInput', 'GameClicks', 'GameClickChecks', 'Frames', 'Reminders', 'Weather', 'WeatherSettings', 'Doomscroll', 'DoomscrollSettings', 'HerBrowser', 'HerComputer', 'HerJobs', 'HerComputerWindow', 'HerComputerSettings', 'HerVault', 'ScrollForMe', 'GameSense', 'GameWatch', 'SelfKnow', 'Care', 'Clips', 'ClipSettings', 'ClipChecks', 'Chimes', 'ChimeSettings', 'Minis', 'Chess', 'ChessBoard', 'Files', 'Commands', 'Playlist', 'Favourites', 'FileFinder', 'Websites', 'Ink', 'Clones', 'Squad', 'SquadShow', 'ChaosScreen', 'Icons', 'FollowUps', 'WatchAlong', 'CallMode', 'Husk', 'CallEars', 'CallPanel', 'HoloInk', 'HoloWindow', 'HoloModes', 'FileEditor', 'FileChecks', 'Reactions', 'ToolChecks', 'LeakChecks', 'ErrorChecks', 'Remote', 'RemoteCrypto', 'RemoteLink', 'RemoteQr', 'RemoteVectors', 'RemoteBrainKeys', 'RemoteBrains', 'RemoteChat', 'RemoteWake', 'RemotePush', 'RoomHost', 'CapriCamera', 'MainframeRemote', 'MainframeDiscordBot', 'DiscordCompanion', 'DiscordBotProcess', 'MainframeSystem', 'SystemUse', 'ResourceChecks', 'RemoteScreen', 'RemoteCall', 'RemoteLive', 'SmokeRun', 'Setup', 'AssemblyInfo', 'Animations', 'UninstallSettings', 'FileKeeper', 'FileKeeperChecks', 'Onboarding', 'MemoryBook', 'MemoryWindow', 'Gifs', 'GifWindow', 'GifChecks', 'GameSpeedChecks', 'GameInputChecks', 'BetterModels', 'ToolMods', 'HerReplay', 'CrashReports', 'Crisis', 'LanguagePair', 'LanguageChecks', 'Lite', 'LiteChecks', 'MainframeSpend', 'Spend', 'SpendChecks', 'Timeline', 'TimelineChecks', 'CompanionPacks', 'CompanionChecks', 'CompanionSwitch', 'RudeWords', 'Roam') | ForEach-Object { Join-Path $root ('app\' + $_ + '.cs') }
$compileArgs = $shared + $references + $resources + @('/unsafe+', ('/out:' + $app)) + $sources
$responseFile = [IO.Path]::GetTempFileName()
try
{
    [IO.File]::WriteAllLines($responseFile, [string[]]@($compileArgs | ForEach-Object { '"' + $_ + '"' }), [Text.UTF8Encoding]::new($false))
    & $compiler ('@' + $responseFile)
    if ($LASTEXITCODE -ne 0) { throw 'Capri compilation failed.' }
}
finally { Remove-Item -LiteralPath $responseFile -Force }
if ($Store) { Write-Output "Built $app for the Microsoft Store"; return }
Copy-Item -LiteralPath $app -Destination $setup -Force
Write-Output "Built $app and $setup"
if (-not $SmokeTest) { return }
$filtered = $Steps -or $Step -ge 0 -or $onlyList.Count -gt 0 -or $groupList.Count -gt 0
if (-not $filtered -and -not $Suggest)
{
    $suggested = Get-SuggestedSmokeGroups $root
    if ($suggested.Count -gt 0) { Write-Output ('Suggested smoke groups for local changes: ' + ($suggested -join ',')) }
}
$runtime = Join-Path $out 'smoke\runtime'
$package = Join-Path $out 'smoke\package'
$smokeArgs = @('--smoke-test', ('"' + $runtime + '"'))
if ($Steps) { $smokeArgs += '--steps' }
elseif ($Step -ge 0) { $smokeArgs += @('--step', $Step.ToString()) }
else
{
    if ($groupList.Count -gt 0) { $smokeArgs += @('--group', ($groupList -join ',')) }
    if ($onlyList.Count -gt 0) { $smokeArgs += @('--only', ($onlyList -join ',')) }
}
$check = Start-Process -FilePath $app -ArgumentList $smokeArgs -PassThru -WindowStyle Hidden
if (-not $check.WaitForExit($SmokeSeconds * 1000)) { $check.Kill(); throw 'Runtime smoke test timed out.' }
if (Test-Path -LiteralPath (Join-Path $runtime 'smoke-test.txt')) { Get-Content -LiteralPath (Join-Path $runtime 'smoke-test.txt') }
elseif ($check.ExitCode -eq 0) { throw 'Runtime smoke test wrote no results. Another smoke test probably held the lock; rerun it when that one ends.' }
if ($check.ExitCode -ne 0) {
    if (Test-Path -LiteralPath (Join-Path $runtime 'failure.txt')) { Get-Content -LiteralPath (Join-Path $runtime 'failure.txt') }
    throw 'Runtime smoke test failed.'
}
if ($filtered) { return }
$verify = Start-Process -FilePath $setup -ArgumentList @('--verify', ('"' + $package + '"')) -PassThru -WindowStyle Hidden
if (-not $verify.WaitForExit($SmokeSeconds * 1000)) { $verify.Kill(); throw 'Installer check timed out.' }
if ($verify.ExitCode -ne 0) {
    if (Test-Path -LiteralPath (Join-Path $package 'failure.txt')) { Get-Content -LiteralPath (Join-Path $package 'failure.txt') }
    throw 'Installer check failed.'
}
if ((Get-FileHash -LiteralPath (Join-Path $package 'Capri.exe')).Hash -ne (Get-FileHash -LiteralPath $app).Hash) { throw 'Installer payload differs from the app executable.' }
Get-Content -LiteralPath (Join-Path $package 'packaging-test.txt')
Write-Output 'PASS installer payload matches the app'
