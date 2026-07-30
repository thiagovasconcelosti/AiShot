# Publicando um release

O release é automatizado: empurrar uma tag `v*` dispara o fluxo
[`release.yml`](.github/workflows/release.yml), que compila, testa, gera o
instalador, calcula o checksum, monta o pacote do Chocolatey e publica tudo.

## Passos

1. **Atualize a versão** em [`Directory.Build.props`](Directory.Build.props) —
   é a fonte única do repositório. O assembly, o instalador, o nuspec e o script
   de instalação do Chocolatey derivam desse valor; não edite nenhum deles à mão.

2. **Faça o commit e crie a tag.** A tag precisa concordar com a versão do
   arquivo — o fluxo confere as duas e falha se divergirem, porque um instalador
   nomeado com uma versão e um assembly com outra colocaria o auto-update em
   laço de reinstalação.

   ```powershell
   git commit -am "release: v0.2.0"
   git tag v0.2.0
   git push origin master --tags
   ```

3. **Acompanhe o fluxo.** Ao terminar, o release traz o instalador, o arquivo
   `.sha256`, o pacote portátil e o `.nupkg` do Chocolatey.

## O que o fluxo faz

| Etapa | Observação |
| --- | --- |
| Confere versão × tag | Falha cedo se divergirem |
| Testes | O release não sai com a suíte vermelha |
| `dotnet publish` | Self-contained, **sem** `PublishSingleFile` (veja abaixo) |
| Inno Setup | Recebe a versão por `/DAppVersion` |
| Checksum | SHA-256 calculado do próprio artefato publicado |
| Pacote do Chocolatey | Gerado dos `.template`, com versão e checksum injetados |
| Publicação | Release do GitHub com todos os artefatos |

> **Não usar `PublishSingleFile=true` + compressão** para o artefato
> distribuído: o executável auto-extraível dispara falso-positivo de antivírus
> (VirusTotal / Chocolatey). A pasta self-contained empacotada pelo instalador
> reduz isso. A solução definitiva é assinar com Authenticode — veja a issue
> [#31](https://github.com/thiagovasconcelosti/AiShot/issues/31).

## Verificação antivírus

Enquanto o binário não for assinado, vale escanear o instalador publicado no
VirusTotal:

```powershell
& "$HOME\.claude\skills\virus-scan\scan.ps1" -Path "dist\AiShot-Setup-<versao>.exe"
```

- **0 malicious**: seguir.
- **1-5 malicious**: falso-positivo comum de .NET não assinado; o Chocolatey
  costuma aprovar.
- A chave da API do VirusTotal fica em `~/.claude/secrets/virustotal.key`
  (**fora do repositório** — nunca commitar chaves).

## Publicar no Chocolatey

O `.nupkg` sai pronto no release. Para enviá-lo:

```powershell
choco push aishot.<versao>.nupkg --source https://push.chocolatey.org/ --api-key <SUA_KEY>
```

> Repush da **mesma versão** é permitido enquanto o pacote está em review
> (ex.: para corrigir o instalador e disparar novo scan).

## Compilação local

Para gerar o instalador na máquina, sem passar pelo fluxo:

```powershell
# O bundle web é reconstruído pelo próprio dotnet publish.
dotnet publish src/AiShot/AiShot.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=false -p:DebugType=none -o dist/app

& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\AiShot.iss
# gera dist\AiShot-Setup-<versao>.exe
```

Sem `/DAppVersion`, o `.iss` usa o padrão declarado no próprio arquivo — útil
para testar, mas o artefato de distribuição deve sempre sair do fluxo, onde a
versão vem de `Directory.Build.props` e o checksum é calculado do artefato real.
