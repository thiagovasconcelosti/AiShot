$ErrorActionPreference = 'Stop'

$packageArgs = @{
  packageName    = 'aishot'
  fileType       = 'exe'
  url            = 'https://github.com/thiagovasconcelosti/AiShot/releases/download/v0.1.2/AiShot-Setup-0.1.2.exe'
  checksum       = 'B68053B20E61EBFBA56DFA1D179A3A29D91F5D9128778B27456A3070BF5E5D53'
  checksumType   = 'sha256'
  # Inno Setup: instalação silenciosa
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
  validExitCodes = @(0)
}

Install-ChocolateyPackage @packageArgs
