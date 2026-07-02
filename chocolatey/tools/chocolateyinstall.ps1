$ErrorActionPreference = 'Stop'

$packageArgs = @{
  packageName    = 'aishot'
  fileType       = 'exe'
  url            = 'https://github.com/thiagovasconcelosti/AiShot/releases/download/v0.1.0/AiShot-Setup-0.1.0.exe'
  checksum       = '509B53264787C58D8EC736581A6D7F1EA9DC413266D5C5BAF67109E53067D601'
  checksumType   = 'sha256'
  # Inno Setup: instalação silenciosa
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
  validExitCodes = @(0)
}

Install-ChocolateyPackage @packageArgs
