$ErrorActionPreference = 'Stop'

# Localiza a entrada de desinstalação do Inno Setup e remove silenciosamente.
[array]$keys = Get-UninstallRegistryKey -SoftwareName 'AiShot*'
foreach ($key in $keys) {
  $uninstall = $key.UninstallString
  if (-not $uninstall) { continue }
  $file = $uninstall.Trim('"')
  Uninstall-ChocolateyPackage -PackageName 'aishot' -FileType 'exe' `
    -SilentArgs '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -ValidExitCodes @(0) -File $file
}
