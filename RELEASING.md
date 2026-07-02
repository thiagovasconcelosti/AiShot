# Publicando um release

Passos para cortar uma versão do AiShot.

## Build
```powershell
# Bundle web (embutido no exe) é rebuildado pelo próprio dotnet publish.
dotnet publish src/AiShot/AiShot.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=false -p:DebugType=none -o dist/app
```

> **Não usar `PublishSingleFile=true` + compressão** para o artefato distribuído:
> o exe auto-extraível dispara falso-positivo de antivírus (VirusTotal / Chocolatey).
> A pasta self-contained não-single-file empacotada pelo instalador reduz isso.

## Instalador
```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\AiShot.iss
# gera dist\AiShot-Setup-<versao>.exe
```

## Verificação antivírus (obrigatório antes de publicar)
Escanear o instalador no VirusTotal usando a skill global **virus-scan**:

```powershell
& "$HOME\.claude\skills\virus-scan\scan.ps1" -Path "dist\AiShot-Setup-0.1.0.exe"
```

- **0 malicious**: seguir.
- **1-5 malicious**: falso-positivo comum de .NET não assinado; Chocolatey aprova.
  Para zerar de vez: assinar (Authenticode) ou revisar o build.
- A chave da API do VirusTotal fica em `~/.claude/secrets/virustotal.key`
  (**fora do repositório** — nunca commitar chaves).

## Release GitHub
```powershell
gh release create v<versao> --title "AiShot v<versao>" --notes "..." `
  dist\AiShot-Setup-<versao>.exe
# portátil (zip da pasta), se desejar:
Compress-Archive dist\app\* dist\AiShot-<versao>-portable.zip -Force
gh release upload v<versao> dist\AiShot-<versao>-portable.zip --clobber
```

## Chocolatey
Atualizar o `checksum` (SHA256 do novo instalador) em
`chocolatey/tools/chocolateyinstall.ps1`, depois:
```powershell
cd chocolatey
choco pack
choco push aishot.<versao>.nupkg --source https://push.chocolatey.org/ --api-key <SUA_KEY>
```
> Repush da **mesma versão** é permitido enquanto o pacote está em review
> (ex.: para corrigir o instalador e disparar novo scan).
