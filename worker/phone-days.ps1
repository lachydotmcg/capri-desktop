param(
    [ValidateRange(1, 366)]
    [int]$Days = 30
)

# How many phones used Capri Remote with no PC, a day at a time, from the phone_days table (0009_phone_days.sql). Days
# are UTC. It only reads: wrangler signs in with your own Cloudflare login, and no secret is kept here.
$ErrorActionPreference = 'Stop'
$since = [DateTime]::UtcNow.Date.AddDays(1 - $Days).ToString('yyyy-MM-dd')
$query = "SELECT day, phones, first_time, also_paired FROM phone_days WHERE day >= '$since' ORDER BY day DESC"
Push-Location $PSScriptRoot
try {
    $text = & npx --yes wrangler@4.152.0 d1 execute capri-reactions --remote --json --command $query
    if ($LASTEXITCODE -ne 0) { throw 'wrangler could not read the phone count. Check your Cloudflare login with: npx wrangler whoami' }
    $rows = @((($text | Out-String) | ConvertFrom-Json)[0].results)
} finally {
    Pop-Location
}
if ($rows.Count -eq 0) { 'No phone days since ' + $since + ' (UTC).'; return }
$rows | Select-Object day, phones, @{Name = 'phone_only'; Expression = { $_.phones - $_.also_paired } }, also_paired, @{Name = 'new'; Expression = { $_.first_time } } | Format-Table -AutoSize
$new = ($rows | Measure-Object first_time -Sum).Sum
$busiest = ($rows | Measure-Object phones -Maximum).Maximum
'Since ' + $since + ' (UTC): ' + $new + ' new phones, and ' + $busiest + ' on the busiest day. A phone that used her on several days is in each of those days.'
