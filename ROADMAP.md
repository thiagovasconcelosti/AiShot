# Roadmap — AiShot

Backlog priorizado a partir da revisão de código de 30/07/2026 (baseline: v0.1.3).

Legenda de esforço: **P** pequeno (< 2h) · **M** médio (meio dia) · **G** grande (1+ dia).

| Fase | Tema | Alvo | Milestone |
| --- | --- | --- | --- |
| [0](#fase-0--correções-críticas) | Correções críticas | v0.1.4 | [Fase 0](../../milestone/1) |
| [1](#fase-1--segurança) | Segurança | v0.1.5 | [Fase 1](../../milestone/2) |
| [2](#fase-2--fundação-de-qualidade) | Testes + CI | v0.2.0 | [Fase 2](../../milestone/3) |
| [3](#fase-3--refatoração) | Refatoração estrutural | v0.2.x | [Fase 3](../../milestone/4) |
| [4](#fase-4--features-de-edição) | Features de edição | v0.3.0 | [Fase 4](../../milestone/5) |
| [5](#fase-5--features-de-ia-e-produtividade) | IA e produtividade | v0.4.0 | [Fase 5](../../milestone/6) |
| [6](#fase-6--distribuição) | Distribuição | v0.2.x | [Fase 6](../../milestone/7) |

### Rastreio das issues

| Item | Issue | Item | Issue |
| --- | --- | --- | --- |
| 0.1 Instância única | [#3](../../issues/3) | 4.1 Borrão / pixelização | [#18](../../issues/18) |
| 0.2 Vazamento de `Bitmap` | [#4](../../issues/4) | 4.2 Refazer | [#19](../../issues/19) |
| 0.3 Overlay duplicado | [#5](../../issues/5) | 4.3 Numeração de passos | [#20](../../issues/20) |
| 0.4 Escrita atômica | [#6](../../issues/6) | 4.4 Atalhos de ferramenta | [#21](../../issues/21) |
| 0.5 Hook de teclado | [#7](../../issues/7) | 4.5 Histórico de capturas | [#22](../../issues/22) |
| 0.6 `FALLBACK__BASEURL` | [#8](../../issues/8) | 4.6 Acessibilidade | [#33](../../issues/33) |
| 0.7 Modelo padrão | [#9](../../issues/9) | 5.1 Streaming da IA | [#23](../../issues/23) |
| 0.8 Chave fictícia no mock | [#27](../../issues/27) | 5.2 Markdown no chat | [#24](../../issues/24) |
| 0.9 Caches órfãos | [#28](../../issues/28) | 5.3 OCR | [#25](../../issues/25) |
| 1.1 Assinatura do instalador | [#10](../../issues/10) | 5.4 Copiar resposta | [#26](../../issues/26) |
| 1.2 Extração da interface web | [#11](../../issues/11) | 5.5 Internacionalização | [#34](../../issues/34) |
| 1.3 Sanitizar segredos | [#12](../../issues/12) | 6.1 Fonte única de versão | [#29](../../issues/29) |
| 2.1 Testes unitários | [#13](../../issues/13) | 6.2 Checksum do Chocolatey | [#30](../../issues/30) |
| 2.2 Integração contínua | [#14](../../issues/14) | 6.3 Assinatura Authenticode | [#31](../../issues/31) |
| 2.3 `HttpClient` | [#15](../../issues/15) | 6.4 Limpeza na desinstalação | [#32](../../issues/32) |
| 2.4 Limpeza do csproj | [#16](../../issues/16) | | |
| 3.1 Quebrar `CaptureOverlay` | [#17](../../issues/17) | | |

---

## Fase 0 — Correções críticas

Bugs reprodutíveis com impacto direto no usuário. Bloqueiam o próximo release.

### 0.1 · Instância única do aplicativo — **P**

**Problema.** [`Program.cs`](src/AiShot/Program.cs) não impede duas execuções simultâneas.
Duas instâncias registram dois hooks de teclado globais na mesma tecla e exibem
dois ícones na bandeja. O instalador Inno Setup não bloqueia isso.

**Solução.** `Mutex` nomeado (escopo `Local\`) verificado no início de `Main`. Se já
existir, enviar mensagem de janela para a instância viva trazer as Configurações ao
foco e encerrar a nova.

**Critério de aceite.** Executar o `.exe` duas vezes resulta em um único ícone na
bandeja e um único hook de teclado ativo.

---

### 0.2 · Vazamento de `Bitmap` em `RenderFinal()` — **P**

**Problema.** [`CaptureOverlay.OnBottomAction`](src/AiShot/Capture/CaptureOverlay.cs#L252-L264)
chama `RenderFinal()` sem `using` nos casos `copy`, `save`, `upload` e `share`. Cada
clique aloca um bitmap do tamanho da seleção (ex.: 1920×1080×4 ≈ 8 MB) que só é
liberado pelo coletor de lixo. Apenas `OpenInPaint` descarta corretamente.

**Atenção.** `Clipboard.SetImage` copia o conteúdo para a área de transferência de
forma síncrona, então descartar depois da chamada é seguro. Já `UploadAsync` é
assíncrono: o bitmap precisa sobreviver até a conversão para PNG dentro de
`AppHost.ToPng`. Descartar cedo demais gera `ObjectDisposedException`.

**Solução.** Passar `Bitmap` com `using` em `copy`/`save`; em `upload`/`share`,
converter para PNG antes de iniciar a tarefa assíncrona, ou envolver o descarte
em `try/finally` dentro de `DoUploadAsync`.

**Critério de aceite.** Cem ciclos de captura + cópia não elevam o conjunto de
trabalho do processo de forma monotônica.

---

### 0.3 · Guarda `_capturing` não protege contra overlay duplicado — **M**

**Problema.** [`TrayAppContext.StartCapture`](src/AiShot/App/TrayAppContext.cs#L109-L128)
usa `_capturing` como semáforo, mas `overlay.Show()` não bloqueia — o `finally`
zera a flag imediatamente. Pressionar a tecla de atalho duas vezes rápido empilha
dois overlays em tela cheia, ambos capturando o fundo.

**Solução.** Guardar a referência do overlay ativo em campo. Retornar cedo se não
for nulo e não estiver descartado; limpar o campo no evento `FormClosed`.

**Critério de aceite.** Pressionar a tecla de atalho repetidamente resulta em um
único overlay.

---

### 0.4 · Escrita de configuração não é atômica — **P**

**Problema.** [`AppConfig.Save`](src/AiShot/Config/AppConfig.cs#L82-L85) usa
`File.Move(tmp, path, overwrite: true)`. O comentário promete atomicidade, mas
`File.Move` com sobrescrita não garante a mesma semântica de `ReplaceFile` do
Win32 quanto a preservação de atributos e recuperação em falha.

**Solução.** Usar `File.Replace(tmp, path, destinationBackupFileName)` quando o
destino existir, com queda para `File.Move` na primeira gravação. Preserva um
backup automático da versão anterior.

**Critério de aceite.** Interromper o processo durante a gravação nunca deixa
`appsettings.json` truncado ou ausente.

---

### 0.5 · Hook de teclado morre em silêncio — **M**

**Problema.** O Windows desinstala hooks `WH_KEYBOARD_LL` cujo callback exceda
`LowLevelHooksTimeout` (padrão aproximado de 5 s). Quando isso ocorre,
[`GlobalHotKey`](src/AiShot/HotKey/GlobalHotKey.cs) continua com `_hook` não-nulo,
mas a tecla de atalho para de funcionar sem qualquer aviso ao usuário.

**Solução.** `System.Windows.Forms.Timer` periódico (ex.: 30 s) que verifica a
saúde do hook e o reinstala quando necessário. Notificar via balão da bandeja
apenas na primeira falha, para não incomodar.

**Critério de aceite.** Após a desinstalação forçada do hook, a tecla de atalho
volta a funcionar dentro de um ciclo do temporizador.

---

### 0.6 · `AI__FALLBACK__BASEURL` ausente nos overrides — **P**

**Problema.** [`AppConfig.ApplyEnvironmentOverrides`](src/AiShot/Config/AppConfig.cs#L152-L157)
cobre `PROVIDER`, `APIKEY` e `MODEL` do fallback, mas não `BASEURL` — assimetria
em relação ao endpoint principal e ao de visão.

**Solução.** Acrescentar a linha e documentar todas as variáveis suportadas em
[`docs/technical.md`](docs/technical.md).

---

### 0.7 · Validar o identificador de modelo padrão — **P**

**Problema.** O padrão em [`AiConfig.Model`](src/AiShot/Config/AppConfig.cs#L176) é
`claude-opus-4-8`. Não confirmei esse identificador contra a API da Anthropic; o
formato destoa do padrão publicado. Se estiver incorreto, a primeira execução do
aplicativo falha com erro 404 da API.

**Solução.** Consultar a documentação oficial de modelos, corrigir o padrão e
adicionar uma nota no README indicando que o identificador precisa acompanhar as
versões publicadas pelo provedor.

---

### 0.8 · Chave de API fictícia no pacote da interface web — **P**

**Problema.** [`bridge.ts`](web/src/bridge.ts#L84) traz `apiKey: "sk-mock-key-1234567890"`
em `mockConfig()`. A chave é falsa e o caminho só executa no servidor de
desenvolvimento, quando `window.chrome.webview` não existe — o risco real é baixo.
O incômodo é que a string entra no pacote compilado embarcado no executável, e o
prefixo `sk-` dispara ferramentas de varredura de segredo.

**Solução.** Trocar por string vazia, como já fazem os demais endpoints do mock.

---

### 0.9 · `CleanupTempFiles` ignora caches órfãos da interface — **P**

**Problema.** [`TrayAppContext.CleanupTempFiles`](src/AiShot/App/TrayAppContext.cs#L46-L59)
remove os PNG temporários, mas não toca em `%TEMP%\AiShot.webui\`. Como o
diretório é chaveado por `ModuleVersionId` — que muda a cada compilação — toda
atualização abandona uma cópia do pacote da interface, indefinidamente.

**Solução.** Varrer o diretório e remover todo subdiretório cuja chave não
corresponda ao `ModuleVersionId` atual. Deve apontar para o caminho definido em
1.2, e não para os dois.

---

## Fase 1 — Segurança

### 1.1 · Verificar assinatura do instalador antes de executá-lo — **M**

**Problema.** [`UpdateService.DownloadAndRunAsync`](src/AiShot/App/UpdateService.cs#L86-L97)
valida o esquema e o host da URL — o que já protege contra redirecionamento para
domínios arbitrários — mas executa o `.exe` baixado sem verificar sua procedência.
Um comprometimento do canal de distribuição levaria à execução de binário
arbitrário na máquina do usuário.

**Solução.** Comparar o SHA-256 do arquivo baixado com o checksum publicado no
release (o processo de release já gera esse valor) e/ou validar a assinatura
Authenticode via `WinVerifyTrust`. Abortar e avisar o usuário em caso de
divergência.

**Critério de aceite.** Um instalador adulterado é rejeitado antes da execução,
com mensagem clara ao usuário.

---

### 1.2 · Endurecer a extração da interface web — **M**

**Problema.** [`SettingsForm.ExtractWebUI`](src/AiShot/Settings/SettingsForm.cs#L273-L292)
extrai o pacote HTML/JS para `%TEMP%\AiShot.webui\<mvid>` e o WebView2 carrega
esse conteúdo. O diretório temporário é por usuário, então o risco é limitado ao
contexto do próprio usuário — mas ainda existe uma janela TOCTOU (Time Of Check
to Time Of Use) entre a checagem de cache e a carga da página.

**Solução.** Mover a extração para `%LOCALAPPDATA%\AiShot\webui\<mvid>` com listas
de controle de acesso restritas ao usuário atual, e validar o hash de `index.html`
contra o recurso embarcado antes de apontar o `Source` do WebView2.

---

### 1.3 · Sanitizar segredos nas mensagens de erro — **P**

**Problema.** [`HttpUtil.Truncate`](src/AiShot/Ai/HttpUtil.cs#L10-L15) limita o
tamanho do corpo de erro, mas não remove segredos. Alguns provedores ecoam
cabeçalhos ou trechos da requisição em respostas de erro, e esse texto chega à
janela de chat e às caixas de mensagem.

**Solução.** Aplicar substituição por expressão regular sobre prefixos conhecidos
de chave (`sk-`, `sk-ant-`, `Bearer `) antes de truncar, trocando o miolo por
`***`.

**Critério de aceite.** Um corpo de erro contendo uma chave de API é exibido
mascarado.

---

## Fase 2 — Fundação de qualidade

### 2.1 · Projeto de testes unitários — **G**

Não existe nenhum teste no repositório. Vários componentes já são funções puras,
prontos para teste sem interface gráfica.

**Escopo inicial** — projeto `tests/AiShot.Tests` com xUnit:

| Alvo | Casos-limite a cobrir |
| --- | --- |
| [`SelectionGeometry`](src/AiShot/Capture/SelectionGeometry.cs) | Seleção invertida, largura zero, `Clamp` fora dos limites, cada `ResizeHandle` |
| [`ToolbarLayout`](src/AiShot/Capture/ToolbarLayout.cs) | Seleção colada em cada borda do monitor, seleção maior que o monitor |
| [`SecretProtector`](src/AiShot/Config/SecretProtector.cs) | Ida e volta, entrada vazia, valor já cifrado, base64 corrompido |
| [`AppConfig`](src/AiShot/Config/AppConfig.cs) | Migração do caminho legado, precedência dos overrides de ambiente, ida e volta com segredos |
| [`GlobalHotKey.ParseHotKey`](src/AiShot/HotKey/GlobalHotKey.cs#L155-L175) | Combinações com modificadores, apelidos (`prtsc`), entrada inválida, string vazia |
| [`UpdateService.IsTrustedUrl`](src/AiShot/App/UpdateService.cs#L75-L83) | HTTP simples, host semelhante (`github.com.evil.tld`), subdomínio válido |

`ParseHotKey` é privado; extrair para tipo interno testável ou expor via
`InternalsVisibleTo`.

**Critério de aceite.** `dotnet test` verde com cobertura das funções puras
listadas.

---

### 2.2 · Integração contínua — **M**

O diretório `.github/` contém apenas modelos de issue. Não há workflow algum.

**Escopo.**
- `build.yml`: em `push` e `pull_request` — `npm ci` + `npm run build` na
  interface web, depois `dotnet build` e `dotnet test`.
- `release.yml`: acionado por tag — compila, gera o instalador Inno Setup, calcula
  o SHA-256 e publica o release. Substitui o passo manual descrito em
  [`RELEASING.md`](RELEASING.md).

**Critério de aceite.** Pull request com teste quebrado é bloqueado pelo CI.

---

### 2.3 · Endurecer o `HttpClient` compartilhado — **P**

**Problema.** [`TrayAppContext`](src/AiShot/App/TrayAppContext.cs#L19) cria um
`HttpClient` com `Timeout.InfiniteTimeSpan` — correto, já que o tempo limite é
definido por operação — mas usa o handler padrão. Em um aplicativo de bandeja que
roda por dias, as conexões agrupadas nunca reciclam e o DNS resolvido fica
obsoleto.

**Solução.** `SocketsHttpHandler` com `PooledConnectionLifetime` de alguns minutos.

---

### 2.4 · Limpeza do arquivo de projeto — **P**

`<Nullable>enable</Nullable>` aparece duas vezes em
[`AiShot.csproj`](src/AiShot/AiShot.csproj). Remover a duplicata e avaliar a
adoção de `TreatWarningsAsErrors` junto de um `.editorconfig` com regras de
análise, alinhado à meta de zero alerta de lint.

---

## Fase 3 — Refatoração

### 3.1 · Quebrar `CaptureOverlay` — **G**

**Problema.** [`CaptureOverlay`](src/AiShot/Capture/CaptureOverlay.cs) tem 707
linhas e acumula responsabilidades: seleção, ferramentas de desenho, paleta, menu
de espessura, entrada de texto, dica de ferramenta, mensagem transitória, upload e
renderização final. Ultrapassa o limite de 300–400 linhas por arquivo.

**Proposta de divisão.**

| Novo tipo | Responsabilidade |
| --- | --- |
| `AnnotationController` | Lista de formas, desfazer/refazer, ciclo da entrada de texto |
| `OverlayChrome` | Dica de ferramenta, mensagem transitória, dimensões, moldura da seleção |
| `OverlayActions` | Copiar, salvar, abrir no Paint, enviar, compartilhar |
| `CaptureOverlay` | Ciclo de vida do formulário, eventos de mouse e teclado, orquestração |

Deve ser feita **depois** da Fase 2 — os testes servem de rede de segurança para
a refatoração.

---

## Fase 4 — Features de edição

### 4.1 · Ferramenta de borrão / pixelização — **M**

Lacuna funcional mais relevante para privacidade: não há como ocultar dados
sensíveis antes de compartilhar um print. Implementar como novo valor de `Tool`
com renderização em [`ShapeRenderer`](src/AiShot/Capture/ShapeRenderer.cs) —
amostragem em blocos sobre a região do retângulo.

### 4.2 · Refazer (`Ctrl+Y`) — **P**

Hoje só existe desfazer ([`CaptureOverlay.cs:97-101`](src/AiShot/Capture/CaptureOverlay.cs#L97-L101)).
Uma pilha de refazer, limpa a cada nova forma, resolve.

### 4.3 · Numeração de passos — **M**

Marcadores circulares numerados automaticamente (1, 2, 3…), padrão em capturas
para tutoriais e documentação.

### 4.4 · Atalhos de teclado das ferramentas — **P**

`P` caneta, `R` retângulo, `E` elipse, `L` linha, `A` seta, `T` texto, `Esc`
cancelar. Ganho imediato de usabilidade, custo próximo de zero.

### 4.5 · Histórico de capturas — **G**

Guardar as últimas capturas (por exemplo, dez) em `%LOCALAPPDATA%\AiShot\history`
e expor no menu da bandeja. Requer política de retenção e limite de espaço em
disco.

### 4.6 · Acessibilidade do overlay — **G**

O overlay é inteiramente desenhado com `Graphics.DrawString` sobre um formulário
sem controles: não há `AccessibleName`, navegação por teclado nem qualquer coisa
que um leitor de tela possa anunciar. As únicas teclas reconhecidas hoje são `Esc`
e `Ctrl+Z` — na prática, a captura é inutilizável para quem depende de tecnologia
assistiva.

Escopo: navegação por `Tab`, indicador visual de foco, acionamento por `Enter` e
`Espaço`, `AccessibleObject` expondo cada botão, e verificação de contraste do
tema. Apoia-se na estrutura de teclado de 4.4 — vale implementar em sequência.

---

## Fase 5 — Features de IA e produtividade

### 5.1 · Resposta da IA em streaming — **G**

[`ChatPanel.SendAsync`](src/AiShot/Capture/ChatPanel.cs#L98-L133) aguarda a
resposta completa antes de exibir qualquer coisa. Ambos os provedores suportam
SSE (Server-Sent Events); transmitir por incrementos elimina a espera percebida.
Exige mudança em [`IAiProvider`](src/AiShot/Ai/IAiProvider.cs) para expor um
`IAsyncEnumerable<string>`.

### 5.2 · Markdown no chat — **M**

A resposta é desenhada com `Graphics.DrawString` sem formatação
([`ChatPanel.cs:161-177`](src/AiShot/Capture/ChatPanel.cs#L161-L177)). Blocos de
código, listas e negrito saem ilegíveis. Renderizar ao menos blocos de código com
fonte monoespaçada e fundo próprio.

### 5.3 · OCR / copiar texto da imagem — **M**

Extrair o texto do print para a área de transferência. Com a IA de visão já
integrada, é um prompt dedicado; alternativamente, a API `Windows.Media.Ocr` roda
localmente e sem custo.

### 5.4 · Copiar resposta da IA — **P**

Não há como copiar o texto retornado pela IA — os balões são pixels desenhados.
Um botão de copiar por balão resolve.

### 5.5 · Internacionalização da interface — **G**

Todas as strings visíveis estão escritas no código, em português. Não há `.resx`
nem uso de `CultureInfo`. A documentação, porém, já é trilíngue: README em inglês,
português e espanhol, e `docs/` em português e espanhol. Quem chega pelo
`README.es.md` instala o aplicativo e encontra uma interface que pode não ler.

Duas camadas: `.resx` para o WinForms (overlay, bandeja, caixas de mensagem) e
dicionário de traduções no React, alimentado pela cultura enviada na mensagem de
configuração. As mensagens que hoje concatenam texto fixo com `ex.Message`
precisam virar strings com marcador de posição.

---

## Fase 6 — Distribuição

Integridade e reprodutibilidade do que chega à máquina do usuário. Independente
das fases anteriores, mas 6.1 e 6.2 se apoiam no workflow de release de 2.2.

### 6.1 · Fonte única de versão — **M**

**Problema.** O número de versão está escrito à mão em cinco lugares:
[`AiShot.csproj`](src/AiShot/AiShot.csproj#L14), [`AiShot.iss`](installer/AiShot.iss#L7),
[`aishot.nuspec`](chocolatey/aishot.nuspec) (duas vezes) e
[`chocolateyinstall.ps1`](chocolatey/tools/chocolateyinstall.ps1#L6). O histórico já
mostra o sintoma: dois commits para o mesmo release (`a00315b` e `210c246`), porque
a primeira tentativa não atualizou todos.

**Consequência.** Divergência entre o assembly e a tag do release pode colocar o
auto-update em laço de reinstalação — baixa, instala, continua se achando
desatualizado.

**Solução.** `Directory.Build.props` como fonte única, com o workflow de release
injetando o valor nos demais artefatos.

---

### 6.2 · Checksum do Chocolatey atualizado à mão — **M**

**Problema.** [`chocolateyinstall.ps1`](chocolatey/tools/chocolateyinstall.ps1#L7)
traz o SHA-256 fixo no código, atualizado manualmente conforme
[`RELEASING.md`](RELEASING.md). É a única verificação de integridade do binário
nesse canal — e um passo manual em posição crítica de segurança é questão de
tempo até falhar.

**Solução.** Gerar o script a partir de um modelo no workflow de release, com URL
e checksum calculados do mesmo artefato publicado.

---

### 6.3 · Avaliar assinatura Authenticode — **M**

[`RELEASING.md`](RELEASING.md) documenta que `PublishSingleFile` foi desativado por
falso-positivo de antivírus, e exige verificação manual no VirusTotal a cada
publicação. O próprio documento aponta a causa raiz: binário .NET não assinado.

Assinar elimina os falso-positivos e o aviso do SmartScreen, permite reativar
`PublishSingleFile`, remove a etapa manual do release e **dá o que verificar a
1.1**. Custo na faixa de US$ 200–400 por ano para um certificado OV.

Decisão de negócio, não técnica — registrada para que a escolha fique explícita.

---

### 6.4 · Limpeza na desinstalação — **P**

**Problema.** [`AiShot.iss`](installer/AiShot.iss) não tem seção `[UninstallDelete]`.
Desinstalar deixa `%APPDATA%\AiShot\appsettings.json` (com as chaves cifradas),
o perfil do WebView2 e os caches da interface.

As chaves estão sob DPAPI e são inúteis fora da máquina — não é vazamento. Mas
desinstalar deve deixar o sistema limpo, e a remoção da configuração precisa ser
uma escolha explícita do usuário.

---

## Ordem de execução sugerida

1. **Fase 0** inteira — bugs baratos com impacto imediato.
2. **2.1 + 2.2** — testes e CI antes de mexer na estrutura.
3. **6.1 + 6.2** — logo após o CI existir; hoje cada release depende de acerto manual.
4. **Fase 1** — segurança, já com o CI validando cada mudança.
5. **3.1** — refatoração protegida pelos testes.
6. **4.1, 4.2, 4.4** — features de edição de custo baixo e valor alto.
7. **5.1** — streaming, a melhoria de percepção mais notável do conjunto.

**6.3** (assinatura) é pré-requisito de fato para fechar **1.1** com garantia
forte, e depende de decisão sobre custo — vale resolver cedo.
