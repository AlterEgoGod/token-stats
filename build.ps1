param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler not found.' }
$output = Join-Path $PSScriptRoot 'dist'
$checks = Join-Path $PSScriptRoot '.build'
New-Item -ItemType Directory -Path $output,$checks -Force | Out-Null
$source = @(Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter *.cs | Select-Object -ExpandProperty FullName)
$common = @('/nologo', '/optimize+', '/codepage:65001', '/warnaserror+',
    "/win32manifest:$PSScriptRoot/src/app.manifest", "/win32icon:$PSScriptRoot/assets/TokenStats.ico",
    "/resource:$PSScriptRoot/assets/REICON-LICENSE.txt,ReiconLicense",
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll')
if (-not $SkipTests) {
    $tests = @(Get-ChildItem (Join-Path $PSScriptRoot 'tests') -Filter *.cs | Select-Object -ExpandProperty FullName)
    & $compiler @common /define:TESTS /target:exe "/out:$checks/TokenStats.Check.exe" @source @tests
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & "$checks/TokenStats.Check.exe" --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
& $compiler @common /target:winexe "/out:$output/TokenStats.exe" @source
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Close the running application and retry.' }
$setupArguments = @('/nologo','/warnaserror+','/target:winexe','/r:System.Windows.Forms.dll',
    ('/win32icon:' + (Join-Path $PSScriptRoot 'assets/TokenStats.ico')),
    ('/resource:' + (Join-Path $output 'TokenStats.exe') + ',TokenStats.exe'),
    ('/out:' + (Join-Path $output 'TokenStats-Setup.exe')),
    (Join-Path $PSScriptRoot 'Setup.cs'))
& $compiler @setupArguments
if ($LASTEXITCODE -ne 0) { throw 'Setup build failed.' }
Write-Output "Built: $output/TokenStats.exe and TokenStats-Setup.exe"
