param([string]$Repository=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Uninstall-SentoBar.ps1')
$workspace=[IO.Path]::GetFullPath($Repository)
$fixture=[IO.Path]::GetFullPath((Join-Path $workspace ('outputs/uninstall-plan-'+[Guid]::NewGuid().ToString('N'))))
$allowed=[IO.Path]::GetFullPath((Join-Path $workspace 'outputs'))+[IO.Path]::DirectorySeparatorChar
if(-not $fixture.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe fixture location'}
Write-Output "Isolated cleanup fixture: $fixture"
$local=Join-Path $fixture 'Local';$temp=Join-Path $local 'Temp';$outside=Join-Path $fixture 'PersonalModels'
$link=$null
New-Item -ItemType Directory -Path $local,$temp,$outside -Force | Out-Null
try {
    $sentinel=Join-Path $outside 'imported-model.onnx';'personal model' | Set-Content -LiteralPath $sentinel
    $roots=Get-SentoBarDataRoots $local $temp
    foreach($folder in @('SentoBar/local-speech/models','SentoBar/native-runtime/fixture-hash','SentoBar/native-host-spool','LumaIsland/local-speech/voices','SentoBar-Updates/rollback')) {
        $path=Join-Path $local $folder;New-Item -ItemType Directory -Path $path -Force | Out-Null
        'downloaded by app' | Set-Content -LiteralPath (Join-Path $path 'owned.bin')
    }
    $plan=New-SentoBarRemovalPlan $roots
    if($plan.Files.Count -ne 4){throw 'Managed model, native renderer cache or update roots were not included'}
    Remove-SentoBarPlannedData $plan
    if((Test-Path -LiteralPath (Join-Path $local 'SentoBar')) -or (-not (Test-Path -LiteralPath (Join-Path $local 'LumaIsland/local-speech/voices/owned.bin'))) -or (Test-Path -LiteralPath (Join-Path $local 'SentoBar-Updates'))){throw 'App-owned files remain'}
    if((Get-Content -LiteralPath $sentinel -Raw).Trim() -ne 'personal model'){throw 'Imported model was changed'}
    'PASS complete app-owned profiles/models/update cleanup preserves external imported files'

    $rejected=$false
    try{Assert-SentoBarOwnedPath $sentinel (Join-Path $local 'SentoBar') | Out-Null}catch{$rejected=$true}
    if(-not $rejected){throw 'An outside path was accepted'}
    'PASS paths outside the dedicated root are rejected'

    $appRoot=Join-Path $local 'SentoBar';New-Item -ItemType Directory -Path $appRoot -Force | Out-Null
    $preserved=Join-Path $appRoot 'settings.json';'keep until preflight passes' | Set-Content -LiteralPath $preserved
    $link=Join-Path $appRoot 'external-model-link'
    New-Item -ItemType Junction -Path $link -Target $outside | Out-Null
    $rejected=$false
    try{New-SentoBarRemovalPlan (,$appRoot) | Out-Null}catch{$rejected=$true}
    if(-not $rejected -or -not (Test-Path -LiteralPath $sentinel) -or -not (Test-Path -LiteralPath $preserved)){throw 'Linked tree was not rejected before deletion'}
    'PASS junctions are rejected before any model or settings deletion'
    [IO.Directory]::Delete($link,$false);$link=$null
    Remove-SentoBarPlannedData (New-SentoBarRemovalPlan (,$appRoot))

    $migration=Join-Path $local ('SentoBar.migration-'+[Guid]::NewGuid().ToString('N'))
    $speech=Join-Path $temp ('SentoBar-speech-'+[Guid]::NewGuid().ToString('N'))
    $other=Join-Path $temp 'SentoBar-unrelated-folder'
    New-Item -ItemType Directory -Path $migration,$speech,$other -Force | Out-Null
    $roots=Get-SentoBarDataRoots $local $temp
    if($roots -notcontains $migration -or $roots -notcontains $speech -or $roots -contains $other){throw 'Temporary folder ownership rules are incorrect'}
    'PASS only exact migration/speech job names and app-specific runtime caches are selected'

    $net=Join-Path $temp '.net'
    $owned=Join-Path $net 'SentoBar-1.2.0_b.15-win-x64-Setup'
    $stable=Join-Path $net 'SentoBar-1.2.0-win-arm64-Setup'
    $foreign=Join-Path $net 'NabyScape-1.2.0_b.14-win-x64-Setup'
    $similar=Join-Path $net 'SentoBar-personal-setup'
    New-Item -ItemType Directory -Path $owned,$stable,$foreign,$similar -Force | Out-Null
    $roots=Get-SentoBarDataRoots $local $temp
    if ($roots -notcontains $owned -or $roots -notcontains $stable -or $roots -contains $foreign -or $roots -contains $similar) {
        throw 'Versioned installer cache ownership is incorrect'
    }
    'PASS current stable and beta setup caches are included and similarly named foreign folders are preserved'

    $tokens=$null;$parseErrors=$null
    [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Uninstall-SentoBar.ps1'),[ref]$tokens,[ref]$parseErrors) | Out-Null
    if($parseErrors.Count){throw 'Uninstaller syntax is invalid'}
    'PASS complete uninstaller parses without running Registry or credential cleanup'
} finally {
    # Every cleanup target is under the checked absolute fixture directory.
    if($link -and (Test-Path -LiteralPath $link)){[IO.Directory]::Delete($link,$false)}
    if(Test-Path -LiteralPath $fixture){Remove-SentoBarPlannedData (New-SentoBarRemovalPlan (,$fixture))}
}
