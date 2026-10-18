<#
.SYNOPSIS
    Drives the real SpaceLens window through UI Automation and checks the main flows.

.DESCRIPTION
    Builds nothing and changes nothing on disk except artifacts\ui-smoke-results.json: it scans a folder
    (the repository's src folder by default), searches, asks the offline assistant, builds a cleanup plan,
    opens Compare/Duplicates/Settings, cancels a drive scan and resizes the window. It never deletes files
    and never sends anything to Anthropic (with a saved key and consent, the assistant step would).

.EXAMPLE
    dotnet build SpaceLens.sln
    powershell -ExecutionPolicy Bypass -File scripts\SmokeTest.ps1
#>
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\src\SpaceLens\bin\Debug\net9.0-windows\SpaceLens.exe'),
    [string]$ScanFolder = (Join-Path $PSScriptRoot '..\src')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$results = [System.Collections.Generic.List[object]]::new()

function Find-Id([string]$Id) {
    $script:Window.FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id))
}

function Wait-For([scriptblock]$Condition, [int]$Seconds = 30, [string]$What = 'condition') {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        $value = & $Condition
        if ($value) { return $value }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    throw "Timed out waiting for $What"
}

function Invoke-Id([string]$Id) { (Find-Id $Id).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Set-Text([string]$Id, [string]$Text) { (Find-Id $Id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text) }
function Text-Of([string]$Id) { $e = Find-Id $Id; if ($e) { $e.Current.Name } else { '' } }

function Select-Page([string]$Label) {
    $items = (Find-Id 'Navigation').FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition)
    $item = $items | Where-Object { $_.Current.Name -eq $Label } | Select-Object -First 1
    if (-not $item) { throw "No navigation item '$Label'" }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 400
}

function Step([string]$Name, [scriptblock]$Body) {
    try {
        $detail = & $Body
        $results.Add([pscustomobject]@{ Step = $Name; Passed = $true; Detail = "$detail" })
        Write-Host "PASS  $Name  $detail" -ForegroundColor Green
    }
    catch {
        $results.Add([pscustomobject]@{ Step = $Name; Passed = $false; Detail = $_.Exception.Message })
        Write-Host "FAIL  $Name  $($_.Exception.Message)" -ForegroundColor Red
    }
}

$scanPath = (Resolve-Path $ScanFolder).Path
$app = Start-Process $Exe -ArgumentList "--scan `"$scanPath`"" -PassThru
try {
    $processCondition = [System.Windows.Automation.PropertyCondition]::new($AE::ProcessIdProperty, $app.Id)
    $script:Window = Wait-For { $AE::RootElement.FindFirst($Scope::Children, $processCondition) } 20 'the main window'

    Step 'Scan a folder' {
        Wait-For { (Text-Of 'StatusText') -like 'Scanned*' } 60 'the scan to finish' | Out-Null
        Text-Of 'StatusText'
    }

    Step 'Explorer lists the scanned folder' {
        $caption = Wait-For { $c = Text-Of 'ListCaption'; if ($c -match '\d+ item') { $c } } 10 'the item list'
        $caption
    }

    Step 'Search filters across the scan' {
        Set-Text 'SearchBox' 'ViewModel'
        $caption = Wait-For { $c = Text-Of 'ListCaption'; if ($c -match 'match') { $c } } 10 'search results'
        Set-Text 'SearchBox' ''
        $caption
    }

    Step 'Offline assistant answers from the scan' {
        Set-Text 'AssistantInput' 'What takes the most space?'
        Invoke-Id 'SendQuestion'
        $messages = Wait-For {
            $list = Find-Id 'AssistantMessages'
            $count = $list.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition).Count
            if ($count -ge 2) { $count }
        } 30 'an answer'
        "$messages messages"
    }

    Step 'Cleanup builds a plan' {
        Select-Page 'Cleanup'
        Set-Text 'CleanupGoal' '1 MB'
        Invoke-Id 'CreatePlan'
        Wait-For { $h = Text-Of 'PlanHeadline'; if ($h) { $h } } 10 'a plan headline'
    }

    Step 'Compare page opens' {
        Select-Page 'Compare Scans'
        if (-not (Find-Id 'CompareScans')) { throw 'Compare button missing' }
        'ok'
    }

    Step 'Duplicates finishes' {
        Select-Page 'Duplicates'
        Invoke-Id 'FindDuplicates'
        Wait-For { $s = Text-Of 'DuplicateStatus'; if ($s -notmatch 'Comparing|Collecting' -and $s -match 'found|groups|No identical') { $s } } 60 'the duplicate search'
    }

    Step 'API key field is masked' {
        Select-Page 'Settings'
        $box = Find-Id 'ApiKey'
        if (-not $box.Current.IsPassword) { throw 'The API key box is not a password field' }
        Text-Of 'ConnectionState'
    }

    Step 'A drive scan can be cancelled' {
        Select-Page 'Overview'
        Invoke-Id 'StartScan'
        Wait-For { Find-Id 'CancelScan' } 10 'the scan overlay' | Out-Null
        Invoke-Id 'CancelScan'
        Wait-For { $s = Text-Of 'StatusText'; if ($s -match 'stopped|Scanned') { $s } } 30 'the scan to stop'
    }

    Step 'Window resizes without errors' {
        # SpaceLens opens full screen; its restore button gives back a normal, resizable window.
        $byName = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), 'Maximize'
        $script:Window.FindFirst($Scope::Descendants, $byName).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 800
        $transform = $script:Window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
        $transform.Resize(1180, 760); Start-Sleep -Milliseconds 600
        $transform.Resize(1600, 940); Start-Sleep -Milliseconds 600
        if ($app.HasExited) { throw 'The app exited' }
        'ok'
    }
}
finally {
    if (-not $app.HasExited) { $app | Stop-Process -Force }
}

$output = Join-Path $PSScriptRoot '..\artifacts\ui-smoke-results.json'
New-Item -ItemType Directory -Force (Split-Path $output) | Out-Null
$results | ConvertTo-Json | Set-Content -Encoding utf8 $output
$failed = @($results | Where-Object { -not $_.Passed }).Count
Write-Host ""
Write-Host "$($results.Count - $failed) of $($results.Count) steps passed. Results: $output"
exit $failed
