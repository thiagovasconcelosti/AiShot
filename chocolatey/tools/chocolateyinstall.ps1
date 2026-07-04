$ErrorActionPreference = 'Stop'

$packageArgs = @{
  packageName    = 'aishot'
  fileType       = 'exe'
  url            = 'https://github.com/thiagovasconcelosti/AiShot/releases/download/v0.1.2/AiShot-Setup-0.1.2.exe'
  checksum       = 'A88E8A18A8FE4A3AA4BECE30E5DC5FD4C1F4911405F45DA6252BC86B025C959D'
  checksumType   = 'sha256'
  # Inno Setup: instalação silenciosa
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
  validExitCodes = @(0)
}

Install-ChocolateyPackage @packageArgs
