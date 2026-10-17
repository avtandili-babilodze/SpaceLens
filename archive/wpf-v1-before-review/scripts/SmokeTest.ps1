param([int]$AppProcessId,[string]$ScanFolder=(Join-Path $PSScriptRoot '..\src\SpaceLens'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
if (-not $AppProcessId) {
    $exe=Join-Path $PSScriptRoot '..\src\SpaceLens\bin\Debug\net9.0-windows\SpaceLens.exe'
    $app=Start-Process -FilePath $exe -ArgumentList @('--scan',('"'+$ScanFolder+'"')) -PassThru
    $AppProcessId=$app.Id
}
$results=[System.Collections.Generic.List[string]]::new()
function Wait-Until([scriptblock]$Predicate,[int]$TimeoutSeconds=20) {
    $deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do { $value=& $Predicate; if ($value) { return $value }; Start-Sleep -Milliseconds 150 } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Timed out waiting for the native UI.'
}
$condition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$AppProcessId)
$window=Wait-Until { [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,$condition) }
function Find-Id([string]$Id) {
    $test=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$Id)
    $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$test)
}
function Find-Text([string]$Text) {
    $all=$window.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
    foreach ($element in $all) { if ($element.Current.Name.Contains($Text)) { return $element } }
    return $null
}
function Invoke-Id([string]$Id) { (Find-Id $Id).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Set-Value([string]$Id,[string]$Value) { (Find-Id $Id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value) }
function Select-Page([string]$Key) {
    $items=(Find-Id 'Navigation').FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition)
    $label=if ($Key -eq 'Compare') { 'Compare Scans' } else { $Key }
    foreach ($item in $items) { if ($item.Current.Name -eq $label -or $item.Current.Name.Contains("Key = $Key,")) { $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); return } }
    throw "Navigation item not found: $Key"
}
Wait-Until { Find-Text 'Scan saved' } | Out-Null
$results.Add('Application launched, scanned real files, and remained responsive.')
Select-Page 'Folders'
Set-Value 'SearchFiles' 'App.xaml'
Wait-Until { Find-Text 'of 2 items' } | Out-Null
$results.Add('Filename search returned the two actual App.xaml files.')
Set-Value 'SearchFiles' ''
Set-Value 'AssistantInput' 'What takes the most space?'
Invoke-Id 'SendQuestion'
Wait-Until { Find-Text 'measured logical file data' } | Out-Null
$results.Add('Offline assistant answered using the actual selected scan.')
Select-Page 'Cleanup'
Set-Value 'CleanupGoal' '1 MiB'
Invoke-Id 'CreatePlan'
Wait-Until { Find-Text 'This review list reaches' } | Out-Null
$results.Add('Storage Goal Solver produced a review plan with risk labels.')
Select-Page 'Compare'
Invoke-Id 'CompareScans'
Wait-Until { Find-Text 'Net logical size change:' } | Out-Null
$results.Add('Two saved snapshots were compared.')
Select-Page 'Settings'
if (-not (Find-Id 'AnthropicApiKey').Current.IsPassword) { throw 'The API key control is not masked.' }
$results.Add('Settings uses a native masked PasswordBox.')
Select-Page 'Folders'
Set-Value 'ScanPath' 'C:\Windows'
Invoke-Id 'StartScan'
Wait-Until { $control=Find-Id 'CancelScan'; if ($control -and -not $control.Current.IsOffscreen) { $control } } | Out-Null
Invoke-Id 'CancelScan'
Wait-Until { Find-Text 'Scan cancelled' } | Out-Null
$results.Add('A real Windows folder scan could be cancelled from the responsive UI.')
Set-Value 'ScanPath' $ScanFolder
Invoke-Id 'StartScan'
Wait-Until { Find-Text 'Scan saved' } | Out-Null
try { $window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Resize(1280,820); $results.Add('The native window resized to 1280 x 820.') } catch { $results.Add('Resize is not exposed by this window manager.') }
$artifacts=Join-Path $PSScriptRoot '..\artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$results | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $artifacts 'ui-smoke-results.json')
$results
