param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('new', 'list', 'off', 'on')]
    [string]$Action,
    [Parameter(Position = 1)]
    [string]$Who,
    [decimal]$Dollars = 5,
    [double]$Days = 0
)

$ErrorActionPreference = 'Stop'
$secret = $env:CAPRI_CODES_SECRET
if ([string]::IsNullOrWhiteSpace($secret)) {
    $path = Join-Path $PSScriptRoot '.codes-secret'
    if (Test-Path -LiteralPath $path) { $secret = [IO.File]::ReadAllText($path).Trim() }
}
if ([string]::IsNullOrWhiteSpace($secret)) { throw 'Set CAPRI_CODES_SECRET or put the secret in worker/.codes-secret.' }
$headers = @{ Authorization = 'Bearer ' + $secret }
$address = 'https://capri-reactions.lachydotmcg.workers.dev/codes'
$before = [Net.ServicePointManager]::SecurityProtocol
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    if ($Action -eq 'list') {
        $answer = Invoke-RestMethod -Uri $address -Headers $headers -Method Get
        $answer.codes | Select-Object id, name, dollars, spent, left, @{Name = 'expires'; Expression = { if ($_.expires) { [DateTimeOffset]::FromUnixTimeMilliseconds([long]$_.expires).LocalDateTime.ToString('yyyy-MM-dd HH:mm') } else { 'never' } } }, off, requests, @{Name = 'used'; Expression = { if ($_.used) { [DateTimeOffset]::FromUnixTimeMilliseconds([long]$_.used).LocalDateTime.ToString('yyyy-MM-dd HH:mm') } else { '' } } } | Format-Table -AutoSize
    } elseif ($Action -eq 'new') {
        if ([string]::IsNullOrWhiteSpace($Who) -or $Dollars -le 0 -or $Days -lt 0 -or [double]::IsNaN($Days) -or [double]::IsInfinity($Days)) { throw 'Give a name, a positive dollar budget and days of zero or more.' }
        $body = @{ name = $Who; dollars = $Dollars; days = $Days } | ConvertTo-Json -Compress
        $answer = Invoke-RestMethod -Uri $address -Headers $headers -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
        $answer.code
        'Code ' + $answer.id + ' for ' + $answer.name + ': $' + $answer.dollars + ', ' + $(if ($answer.expires) { 'ends ' + [DateTimeOffset]::FromUnixTimeMilliseconds([long]$answer.expires).LocalDateTime.ToString('yyyy-MM-dd') } else { 'never ends' }) + '.'
    } else {
        $id = 0L
        if (-not [long]::TryParse($Who, [ref]$id) -or $id -le 0) { throw 'Give a code id.' }
        $body = @{ id = $id } | ConvertTo-Json -Compress
        $answer = Invoke-RestMethod -Uri ($address + '/' + $Action) -Headers $headers -Method Post -ContentType 'application/json' -Body $body
        'Code ' + $answer.id + ' is ' + $Action + '.'
    }
} catch {
    # The worker's own error line never holds the secret, so it's shown as it is.
    $said = $null
    if ($_.ErrorDetails -and $_.ErrorDetails.Message) { try { $said = ($_.ErrorDetails.Message | ConvertFrom-Json).error } catch { } }
    $status = $null
    if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
    if ($said) { throw ('Code request failed' + $(if ($status) { ' (' + $status + ')' } else { '' }) + ': ' + $said) }
    throw 'Code request failed. Check the secret, command and worker.'
} finally {
    [Net.ServicePointManager]::SecurityProtocol = $before
}
