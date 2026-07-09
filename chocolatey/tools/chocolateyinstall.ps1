$ErrorActionPreference = 'Stop'

$packageArgs = @{
  packageName    = 'aishot'
  fileType       = 'exe'
  url            = 'https://github.com/thiagovasconcelosti/AiShot/releases/download/v0.1.3/AiShot-Setup-0.1.3.exe'
  checksum       = '116B689374E078C96BE186A1790794A588A4E6E12C3B177F35CB095F58223122'
  checksumType   = 'sha256'
  # Inno Setup: instalação silenciosa
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
  validExitCodes = @(0)
}

Install-ChocolateyPackage @packageArgs
