$ErrorActionPreference = 'Stop'

$packageArgs = @{
  packageName    = 'aishot'
  fileType       = 'exe'
  url            = 'https://github.com/thiagovasconcelosti/AiShot/releases/download/v0.1.1/AiShot-Setup-0.1.1.exe'
  checksum       = 'BC83E86BBA4512D4FA8F37E69CC2F2119AE114B1E8851F82FC5C6D458B905CCE'
  checksumType   = 'sha256'
  # Inno Setup: instalação silenciosa
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
  validExitCodes = @(0)
}

Install-ChocolateyPackage @packageArgs
