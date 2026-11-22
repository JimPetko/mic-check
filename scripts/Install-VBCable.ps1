<#
.SYNOPSIS
    Downloads and installs VB-CABLE, the virtual audio cable Mic Check sends your processed voice to.

.DESCRIPTION
    Downloads the official VB-CABLE driver pack from vb-audio.com, verifies the archive's SHA-256 hash
    and the installer's Authenticode signature, then runs the installer as administrator.

    Windows will still show its own "install this device software?" prompt; drivers can't be installed
    without it. Restart Windows afterwards, then press Refresh in Mic Check.

    VB-CABLE is donationware by VB-Audio Software (https://vb-audio.com/Cable/). It is not part of
    Mic Check; consider supporting the author if you keep using it.

.PARAMETER Interactive
    Open VB-CABLE's own setup window instead of installing silently.

.PARAMETER Force
    Install even if a "CABLE Input" device already exists.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\Install-VBCable.ps1
#>
[CmdletBinding()]
param(
    [switch]$Interactive,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$PackUrl = 'https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip'
$PackSha256 = 'B950E39F01AF1D04EA623C8F6D8EB9B6EA5C477C637295FABF20631C85116BFB'
$ExpectedSigner = 'CN=BUREL VINCENT'

function Test-CableInstalled {
    [bool](Get-PnpDevice -Class AudioEndpoint -ErrorAction SilentlyContinue |
        Where-Object FriendlyName -like '*CABLE Input*')
}

if ((Test-CableInstalled) -and -not $Force) {
    Write-Host 'VB-CABLE is already installed ("CABLE Input" found). Use -Force to reinstall.' -ForegroundColor Green
    return
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host 'Administrator rights are needed to install a driver. Relaunching elevated...'
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($Interactive) { $arguments += '-Interactive' }
    if ($Force) { $arguments += '-Force' }
    $process = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $process.ExitCode
}

$installer = switch ($env:PROCESSOR_ARCHITECTURE) {
    'AMD64' { 'VBCABLE_Setup_x64.exe' }
    'x86' { 'VBCABLE_Setup.exe' }
    default { throw "VB-CABLE's driver pack has no installer for $env:PROCESSOR_ARCHITECTURE." }
}

# A fresh, empty folder: the installer must not be able to pick up anything else lying around.
$workDir = Join-Path ([IO.Path]::GetTempPath()) ("vbcable-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $workDir | Out-Null

try {
    $zip = Join-Path $workDir 'pack.zip'
    Write-Host "Downloading $PackUrl"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $PackUrl -OutFile $zip -UseBasicParsing

    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash
    if ($hash -ne $PackSha256) {
        throw "Downloaded file doesn't match the expected SHA-256.`n  expected $PackSha256`n  actual   $hash`n" +
            'VB-Audio may have published a new pack; check https://vb-audio.com/Cable/ and update this script.'
    }

    $extracted = Join-Path $workDir 'pack'
    Expand-Archive -Path $zip -DestinationPath $extracted
    $setup = Join-Path $extracted $installer

    $signature = Get-AuthenticodeSignature $setup
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate.Subject.StartsWith($ExpectedSigner)) {
        throw "$installer isn't validly signed by VB-Audio (status: $($signature.Status), signer: $($signature.SignerCertificate.Subject))."
    }

    Write-Host "Running $installer. Approve the Windows driver prompt if it appears."
    $setupArgs = if ($Interactive) { @() } else { @('-i', '-h') }
    $process = Start-Process -FilePath $setup -ArgumentList $setupArgs -WorkingDirectory $extracted -Wait -PassThru
    Write-Host "Installer finished (exit code $($process.ExitCode))."
}
finally {
    Remove-Item $workDir -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-CableInstalled) {
    Write-Host 'VB-CABLE is installed.' -ForegroundColor Green
} else {
    Write-Host 'The CABLE devices may only appear after a restart.' -ForegroundColor Yellow
}
Write-Host @'

Next steps:
  1. Restart Windows.
  2. In Mic Check, press Refresh, then tick "Send my processed voice to" (CABLE Input is picked automatically).
  3. In Discord, Zoom, Teams, OBS, etc., choose "CABLE Output (VB-Audio Virtual Cable)" as the microphone.
'@
