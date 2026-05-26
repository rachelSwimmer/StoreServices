#Requires -Version 5.1
<#
.SYNOPSIS
    Load-balancer example driver for StoreServices (Windows / PowerShell port of lb-demo.sh).

.DESCRIPTION
    Sits in front of the three ProductCatalog replicas behind the Nginx LB and
    demonstrates distribution, failover, passive health-check eviction, and
    round-robin vs least_conn.

    Prereq: `docker compose up --build` is running, and the Docker CLI is on PATH.

.EXAMPLE
    ./scripts/lb-demo.ps1 distribution   # round-robin spread across 3 replicas
    ./scripts/lb-demo.ps1 failover       # stop an instance, traffic keeps flowing
    ./scripts/lb-demo.ps1 eviction       # passive health-check takes a bad node out
    ./scripts/lb-demo.ps1 algorithm      # round-robin vs least_conn
    ./scripts/lb-demo.ps1 all            # all of the above, in order
#>
[CmdletBinding()]
param(
    [ValidateSet('distribution', 'failover', 'eviction', 'algorithm', 'all')]
    [string]$Demo = 'all'
)

$ErrorActionPreference = 'Stop'

$LbUrl        = 'http://localhost:8085/internal/lb/whoami'
$Inst1        = 'http://localhost:8091/internal/lb'
$Inst2Toggle  = 'http://localhost:8092/internal/lb/health/toggle'
$Inst2WhoAmI  = 'http://localhost:8092/internal/lb/whoami'
$NginxConf    = Join-Path (Split-Path -Parent $PSScriptRoot) 'nginx/catalog-lb.conf'
$Requests     = if ($env:REQUESTS) { [int]$env:REQUESTS } else { 30 }

function Write-Head([string]$Text) {
    $rule = '-' * 60
    Write-Host $rule
    Write-Host "  $Text"
    Write-Host $rule
}

# Returns the status code of a GET, plus the parsed "instance" on 200.
# Works on both Windows PowerShell 5.1 and PowerShell 7+ (no -SkipHttpErrorCheck).
function Get-Whoami([string]$Url) {
    try {
        $resp = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 10
        $inst = ($resp.Content | ConvertFrom-Json).instance
        return [pscustomobject]@{ Code = [int]$resp.StatusCode; Instance = $inst }
    }
    catch {
        $code = 0
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        return [pscustomobject]@{ Code = $code; Instance = $null }
    }
}

# Fire $Requests requests at the LB, print a per-instance tally and the count of
# non-200 responses (the failover/eviction "no client errors" proof).
function Invoke-Loop {
    $errors = 0
    $hits = @{}
    for ($i = 1; $i -le $Requests; $i++) {
        $r = Get-Whoami $LbUrl
        if ($r.Code -eq 200) {
            $key = if ($r.Instance) { $r.Instance } else { 'unknown' }
            if ($hits.ContainsKey($key)) { $hits[$key]++ } else { $hits[$key] = 1 }
        }
        else {
            $errors++
        }
    }
    Write-Host "Distribution over $Requests requests:"
    foreach ($k in ($hits.Keys | Sort-Object)) {
        Write-Host ("  {0,-22} {1}" -f $k, $hits[$k])
    }
    Write-Host "Non-200 responses: $errors"
}

# Concurrent burst — needed for least_conn to differ from round-robin.
# Uses PS7 -Parallel when available; falls back to sequential (with a warning)
# on Windows PowerShell 5.1, where the two algorithms won't visibly diverge.
function Invoke-Burst([int]$Total = 60, [int]$Concurrency = 12) {
    $results = $null
    if ($PSVersionTable.PSVersion.Major -ge 7) {
        $results = 1..$Total | ForEach-Object -ThrottleLimit $Concurrency -Parallel {
            try {
                (Invoke-WebRequest -Uri $using:LbUrl -UseBasicParsing -TimeoutSec 10).Content |
                    ConvertFrom-Json | Select-Object -ExpandProperty instance
            } catch { }
        }
    }
    else {
        Write-Warning 'PowerShell 5.1: running burst sequentially; least_conn will not visibly differ from round-robin. Use PowerShell 7+ for the full effect.'
        $results = 1..$Total | ForEach-Object { (Get-Whoami $LbUrl).Instance }
    }
    $results | Where-Object { $_ } | Group-Object | Sort-Object Name |
        ForEach-Object { Write-Host ("  {0,-22} {1}" -f $_.Name, $_.Count) }
}

# Flip the `least_conn;` directive in the Nginx config on/off, then reload.
function Set-LeastConn([ValidateSet('on', 'off')][string]$State) {
    $lines = Get-Content $NginxConf
    if ($State -eq 'on') {
        $lines = $lines -replace '^(\s*)#\s*least_conn;', '$1least_conn;'
    }
    else {
        $lines = $lines -replace '^(\s*)least_conn;', '$1# least_conn;'
    }
    Set-Content -Path $NginxConf -Value $lines
    docker compose restart nginx-catalog-lb | Out-Null
    Start-Sleep -Seconds 3
}

function Demo-Distribution {
    Write-Head 'DISTRIBUTION - load balancer spreads requests across replicas'
    Invoke-Loop
}

function Demo-Failover {
    Write-Head 'FAILOVER - stop one replica, traffic continues with no client errors'
    Write-Host 'Stopping product-catalog-2 ...'
    docker stop product-catalog-2 | Out-Null
    Invoke-Loop
    Write-Host ''
    Write-Host '(expected: 0 non-200, only instances 1 and 3 appear)'
    Write-Host 'Restarting product-catalog-2 ...'
    docker start product-catalog-2 | Out-Null
    for ($i = 1; $i -le 30; $i++) {
        if ((Get-Whoami $Inst2WhoAmI).Code -eq 200) { break }
        Start-Sleep -Seconds 2
    }
    Start-Sleep -Seconds 11   # let nginx fail_timeout expire so it probes the node again
    Write-Host 'After restart:'
    Invoke-Loop
}

function Demo-Eviction {
    Write-Head 'HEALTH-CHECK EVICTION - bad-but-running node is taken out of rotation'
    Write-Host 'Marking product-catalog-2 unhealthy (it stays running) ...'
    Invoke-RestMethod -Uri $Inst2Toggle -Method Post | Out-Null
    Start-Sleep -Seconds 1
    Invoke-Loop
    Write-Host ''
    docker ps --filter name=product-catalog-2 --format '  still running: {{.Names}} ({{.Status}})'
    Write-Host '(expected: instance-2 absent from rotation though its container is up)'
    Write-Host 'Restoring product-catalog-2 to healthy ...'
    Invoke-RestMethod -Uri $Inst2Toggle -Method Post | Out-Null
    Start-Sleep -Seconds 11   # let fail_timeout expire so nginx re-adds it
    Write-Host 'After restore:'
    Invoke-Loop
}

function Demo-Algorithm {
    Write-Head 'ALGORITHM - round-robin vs least_conn (under concurrent load)'
    $slow = (Get-Whoami "$Inst1/whoami").Instance
    Write-Host "Making instance $slow artificially slow (400ms) ..."
    Invoke-RestMethod -Uri "$Inst1/slow/400" -Method Post | Out-Null

    Set-LeastConn off
    Write-Host '[round-robin] - assigns evenly regardless of latency:'
    Invoke-Burst 60 12
    Write-Host ''
    Set-LeastConn on
    Write-Host "[least_conn] - steers away from the slow node ($slow gets fewer):"
    Invoke-Burst 60 12

    Invoke-RestMethod -Uri "$Inst1/slow/0" -Method Post | Out-Null   # reset latency
    Set-LeastConn off                                               # restore default algorithm
    Write-Host ''
    Write-Host '(latency reset; config restored to round-robin)'
}

switch ($Demo) {
    'distribution' { Demo-Distribution }
    'failover'     { Demo-Failover }
    'eviction'     { Demo-Eviction }
    'algorithm'    { Demo-Algorithm }
    'all' {
        Demo-Distribution
        Demo-Failover
        Demo-Eviction
        Demo-Algorithm
    }
}
