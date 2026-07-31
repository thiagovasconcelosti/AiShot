# Avisos de terceiros

O AiShot é distribuído sob a [licença MIT](LICENSE) e inclui componentes de
terceiros. Este arquivo reúne os avisos de copyright e as licenças que esses
componentes exigem que acompanhem a redistribuição.

## Redistribuídos no executável

Estes componentes viajam dentro do binário instalado — a atribuição é
obrigatória para quem recebe o aplicativo, não apenas para quem compila o
código.

### Phosphor Icons

Fonte de ícones embutida em `src/AiShot/Assets/Phosphor.ttf`, usada em toda a
interface do overlay de captura.

- Site: <https://phosphoricons.com>
- Repositório: <https://github.com/phosphor-icons/web>
- Licença: MIT

```
MIT License

Copyright (c) 2020-2021 Phosphor Icons

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Microsoft Edge WebView2

Hospeda a interface de Configurações. O SDK é distribuído com o aplicativo; o
runtime do navegador vem do Windows.

- Pacote: `Microsoft.Web.WebView2`
- Site: <https://aka.ms/webview>
- Licença: BSD 3-Clause (Microsoft)

A licença exige que a redistribuição **em forma binária** reproduza o aviso de
copyright, esta lista de condições e a isenção de garantia na documentação:

```
Copyright (C) Microsoft Corporation. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

   * Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
   * Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
   * The name of Microsoft Corporation, or the names of its contributors
may not be used to endorse or promote products derived from this
software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

O texto completo acompanha o pacote NuGet (`LICENSE.txt`).

### Interface de Configurações (bundle web)

A tela de Configurações é uma aplicação React compilada e embutida como recurso
no executável. Os pacotes abaixo entram nesse bundle:

| Componente | Licença |
| --- | --- |
| [React](https://react.dev) e React DOM | MIT |
| [Radix UI](https://www.radix-ui.com) | MIT |
| [Tailwind CSS](https://tailwindcss.com) | MIT |
| [Lucide](https://lucide.dev) (`lucide-react`) | ISC |
| [clsx](https://github.com/lukeed/clsx) | MIT |
| [tailwind-merge](https://github.com/dcastil/tailwind-merge) | MIT |
| [tw-animate-css](https://github.com/Wombosvideo/tw-animate-css) | MIT |
| [class-variance-authority](https://cva.style) | Apache-2.0 |

Os componentes de interface em `web/src/components/ui/` seguem o
[shadcn/ui](https://ui.shadcn.com) (MIT), que é copiado para o projeto em vez de
instalado como dependência.

### .NET

O aplicativo é publicado como *self-contained*, então o runtime do .NET
acompanha o executável. É distribuído pela Microsoft sob a licença MIT
(<https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>).

## Usados apenas na compilação e nos testes

Não fazem parte do binário distribuído.

| Componente | Licença |
| --- | --- |
| [xUnit.net](https://xunit.net) (`xunit`, `xunit.runner.visualstudio`) | Apache-2.0 |
| `Microsoft.NET.Test.Sdk` | MIT |
| [coverlet](https://github.com/coverlet-coverage/coverlet) | MIT |
| [Xunit.SkippableFact](https://github.com/AArnott/Xunit.SkippableFact) | MS-PL |
| [Vite](https://vite.dev), [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react) | MIT |
| [oxlint](https://oxc.rs) | MIT |
| [TypeScript](https://www.typescriptlang.org) | Apache-2.0 |
| `@types/*` ([DefinitelyTyped](https://github.com/DefinitelyTyped/DefinitelyTyped)) | MIT |

## APIs do sistema operacional

Usadas por P/Invoke ou projeção WinRT, fornecidas pelo próprio Windows e não
redistribuídas: `user32`/`kernel32` (atalho global, janela de mensagens),
`Windows.Media.Ocr` (reconhecimento de texto local), DPAPI (`ProtectedData`,
cifragem das chaves de API).

## Serviços de terceiros

Acionados apenas quando o usuário os configura, com a chave dele. Não há código
de terceiros embutido para nenhum deles — a comunicação é REST sobre HTTPS.

| Serviço | Uso |
| --- | --- |
| [Anthropic](https://www.anthropic.com) | provedor de IA (chat e visão) |
| [OpenAI](https://openai.com) | provedor de IA (chat e visão) |
| [freeimage.host](https://freeimage.host), [ImgBB](https://imgbb.com) | hospedagem das imagens enviadas |

O envio de uma captura para qualquer um deles parte de uma ação explícita do
usuário. O reconhecimento de texto (OCR) roda localmente e não envia a imagem
para fora.
