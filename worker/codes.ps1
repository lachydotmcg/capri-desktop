param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('new', 'list', 'off', 'on', 'limit', 'days')]
    [string]$Action,
    [Parameter(Position = 1)]
    [string]$Who,
    [decimal]$Dollars = 5,
    [double]$Days = 0,
    [switch]$Staff,
    [string]$Tag,
    [switch]$NoLimit,
    [switch]$Never
)

$ErrorActionPreference = 'Stop'
if ($Staff -and $Action -ne 'new') { throw 'Use Staff with new.' }
if ($PSBoundParameters.ContainsKey('Tag') -and $Action -ne 'new') { throw 'Use Tag with new.' }
if ($NoLimit -and ($Action -notin @('new', 'limit') -or ($Action -eq 'new' -and -not $Staff))) { throw 'Use NoLimit with new Staff or limit.' }
if ($NoLimit -and $PSBoundParameters.ContainsKey('Dollars')) { throw 'Give Dollars or NoLimit.' }
if ($Never -and $Action -ne 'days') { throw 'Use Never with days.' }
if ($Action -eq 'limit' -and -not $NoLimit -and (-not $PSBoundParameters.ContainsKey('Dollars') -or $Dollars -le 0)) { throw 'Give a positive dollar limit.' }
if ($Action -eq 'days' -and (($Never -and $PSBoundParameters.ContainsKey('Days')) -or (-not $Never -and (-not $PSBoundParameters.ContainsKey('Days') -or $Days -le 0 -or [double]::IsNaN($Days) -or [double]::IsInfinity($Days))))) { throw 'Give days above zero, or never.' }
if ($Action -eq 'new') {
    if ($NoLimit -or ($Staff -and -not $PSBoundParameters.ContainsKey('Dollars'))) { $Dollars = 0 }
    if ([string]::IsNullOrWhiteSpace($Who) -or $Dollars -lt 0 -or ($Dollars -eq 0 -and -not $Staff) -or $Days -lt 0 -or [double]::IsNaN($Days) -or [double]::IsInfinity($Days)) { throw 'Give a name, a dollar budget and days of zero or more.' }
    if ($PSBoundParameters.ContainsKey('Tag') -and $Tag -cnotmatch '^[A-Za-z0-9]{2,12}\z') { throw 'Give a tag of 2 to 12 letters or numbers.' }
}
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
        $answer.codes | Select-Object id, name, kind, tag, @{Name = 'dollars'; Expression = { if ($_.kind -eq 'staff' -and $_.dollars -eq 0) { 'no limit' } else { $_.dollars } } }, spent, @{Name = 'left'; Expression = { if ($null -eq $_.left) { 'no limit' } else { $_.left } } }, @{Name = 'expires'; Expression = { if ($_.expires) { [DateTimeOffset]::FromUnixTimeMilliseconds([long]$_.expires).LocalDateTime.ToString('yyyy-MM-dd HH:mm') } else { 'never' } } }, off, requests, @{Name = 'used'; Expression = { if ($_.used) { [DateTimeOffset]::FromUnixTimeMilliseconds([long]$_.used).LocalDateTime.ToString('yyyy-MM-dd HH:mm') } else { '' } } } | Format-Table -AutoSize
    } elseif ($Action -eq 'new') {
        $sent = @{ name = $Who; kind = $(if ($Staff) { 'staff' } else { 'giveaway' }); dollars = $Dollars; days = $Days }
        if ($PSBoundParameters.ContainsKey('Tag')) { $sent.tag = $Tag }
        $body = $sent | ConvertTo-Json -Compress
        $answer = Invoke-RestMethod -Uri $address -Headers $headers -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
        $answer.code
        if ($answer.named) { $answer.named }
        'Code ' + $answer.id + ' for ' + $answer.name + ' (' + $answer.kind + '): ' + $(if ($answer.kind -eq 'staff' -and $answer.dollars -eq 0) { 'no limit' } else { '$' + $answer.dollars }) + ', ' + $(if ($answer.expires) { 'ends ' + [DateTimeOffset]::FromUnixTimeMilliseconds([long]$answer.expires).LocalDateTime.ToString('yyyy-MM-dd') } else { 'never ends' }) + '.'
    } else {
        $id = 0L
        if (-not [long]::TryParse($Who, [ref]$id) -or $id -le 0) { throw 'Give a code id.' }
        $sent = @{ id = $id }
        if ($Action -eq 'limit') { $sent.dollars = $(if ($NoLimit) { 0 } else { $Dollars }) }
        if ($Action -eq 'days') { if ($Never) { $sent.never = $true } else { $sent.days = $Days } }
        $body = $sent | ConvertTo-Json -Compress
        $answer = Invoke-RestMethod -Uri ($address + '/' + $Action) -Headers $headers -Method Post -ContentType 'application/json' -Body $body
        $culture = [Globalization.CultureInfo]::InvariantCulture
        if ($Action -eq 'limit') {
            if ($answer.dollars -eq 0) {
                'Code ' + $answer.id + ' has no limit: $' + ([decimal]$answer.spent).ToString('0.00', $culture) + ' spent.'
            } else {
                'Code ' + $answer.id + ' has a $' + ([decimal]$answer.dollars).ToString('0.00', $culture) + ' limit: $' + ([decimal]$answer.spent).ToString('0.00', $culture) + ' spent, $' + ([decimal]$answer.left).ToString('0.00', $culture) + ' left.'
            }
        } elseif ($Action -eq 'days') {
            'Code ' + $answer.id + $(if ($answer.expires) { ' ends ' + [DateTimeOffset]::FromUnixTimeMilliseconds([long]$answer.expires).LocalDateTime.ToString('yyyy-MM-dd', $culture) } else { ' never ends' }) + '.'
        } else {
            'Code ' + $answer.id + ' is ' + $Action + '.'
        }
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
