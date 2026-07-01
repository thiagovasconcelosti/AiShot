# Guia de uso

## Instalação

### Chocolatey (recomendado, após aprovação)
```sh
choco install aishot
```

### Instalador
Baixe `AiShot-Setup-x.y.z.exe` no [último release](https://github.com/thiagovasconcelosti/AiShot/releases/latest) e execute. Instala por usuário (sem admin), cria atalhos no Menu Iniciar / área de trabalho e pode ativar **iniciar com o Windows**.

### Portátil
Baixe `AiShot.exe` e execute direto — arquivo único, sem precisar instalar o .NET.

## Primeira execução

O AiShot inicia na **bandeja do sistema** (ícone **A** azul). Não tem janela principal.

- **Tirar um print:** aperte o atalho (**PrintScreen** por padrão) ou dê duplo-clique no ícone da bandeja.
- **Botão direito no ícone** abre o menu: *Capturar*, *Configurações*, *Iniciar com o Windows*, *Sair*.

## Configurar a IA

Abra **Configurações** pelo menu da bandeja:

1. **Atalho** — clique no campo e aperte a combinação desejada (ex.: `Ctrl+Alt+S`). *Limpar* zera.
2. **IA principal** — provedor (`anthropic` ou `openai`), chave de API, modelo e base URL opcional.
   Endpoints compatíveis com OpenAI também funcionam: use provedor `openai` e a base URL, ex.: DeepSeek (`https://api.deepseek.com`) ou OpenRouter (`https://openrouter.ai/api`).
3. **IA de fallback** — usada automaticamente se a principal falhar.
4. **IA de visão (opcional)** — modelo com visão que descreve a imagem **antes** da IA principal responder. Necessária quando o modelo principal não lê imagens (ex.: modelos de texto do DeepSeek). Opção grátis no OpenRouter: `google/gemma-4-26b-a4b-it:free`.
5. **Upload de imagem** — `freeimage` (freeimage.host) ou `imgbb`, com chave opcional.

As chaves são guardadas **cifradas** (DPAPI do Windows) em `%APPDATA%\AiShot`.

## Capturar e anotar

1. Dispare a captura — a tela escurece e você arrasta para selecionar uma região.
2. A seleção permanece aberta com o overlay de edição:
   - **Toolbar lateral (desenho):** lápis, seta, linha, retângulo, elipse, texto, cor, desfazer.
   - **Barra inferior (ações):** Copiar, Salvar, Abrir no Paint, Upload, Compartilhar, Perguntar à IA, Fechar.
3. **Mover / redimensionar** a seleção pelas 8 alças brancas (ou arrastar dentro dela quando nenhuma ferramenta está ativa).
4. **Desfazer** a última anotação com `Ctrl+Z`. **Esc** cancela/fecha.

## Ações

- **Copiar** — coloca a imagem final (com anotações) na área de transferência.
- **Salvar** — salva como PNG ou JPG.
- **Abrir no Paint** — envia a imagem para o mspaint.
- **Upload** — envia para o serviço de imagem configurado e copia a URL.
- **Compartilhar** — envia e abre a URL no navegador.

> ⚠️ Upload/Compartilhar enviam a imagem para um host **público**. É pedida confirmação na primeira vez.

## Perguntar à IA

Clique em **Perguntar à IA** para abrir um balão de chat sobre o print (não fecha a captura):

- Digite a pergunta e aperte **Enter**. Suas mensagens aparecem à direita, as da IA à esquerda.
- A conversa é **contínua** — perguntas seguintes mantêm o contexto.
- Se a **IA de visão** estiver ativa, ela descreve a imagem uma vez e a IA principal responde usando a descrição.
- **Esc** fecha o chat (não a captura); role com a roda do mouse.

## Iniciar com o Windows

Ative pelo menu da bandeja (**Iniciar com o Windows**) ou durante a instalação. Registra o AiShot na inicialização do usuário atual — sem admin.
