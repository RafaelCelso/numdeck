# NumDeck

Transforma um teclado numérico externo (USB ou Bluetooth) num **Stream Deck** para Windows: cada tecla abre aplicativos ou pastas, abre o navegador já com abas em URLs específicas, toca sons, executa atalhos/macros, controla mídia e volume ou troca de perfil.

As teclas apertadas no deck são **bloqueadas** (não digitam no programa em foco). O teclado principal, incluindo o numpad dele, continua funcionando normalmente.

## Instalação

Baixe a versão mais recente em **[Releases](https://github.com/RafaelCelso/numdeck/releases)**:

- **`NumDeck-Setup-x.y.z.exe`**: instalador. Não precisa de administrador nem do .NET instalado. Tem opção de iniciar com o Windows e atualiza versões anteriores mantendo as configurações.
- **`NumDeck-x.y.z-portable.zip`**: versão portátil. Extraia e rode `NumDeck.exe`, mantendo a pasta `native` ao lado.

Requer Windows 10 (1809+) ou 11, 64 bits. Como o instalador não é assinado digitalmente, o SmartScreen pode mostrar um aviso: clique em **Mais informações → Executar assim mesmo**.

## Como usar

1. Abra o NumDeck. O app fica na bandeja do sistema: fechar a janela só a esconde, e para sair use o menu do ícone.
2. Em **Teclado do deck**, escolha o teclado na lista. Se o nome não estiver claro, clique em **Identificar** e aperte uma tecla no teclado numérico.
3. Clique numa tecla da grade, escolha a **Ação** e configure. Use **▶ Testar** para conferir.
4. **Perfis** funcionam como páginas: cada um tem seu mapa de teclas. Em **★ Teclas globais** ficam as teclas que valem em todos os perfis (por exemplo "Próximo perfil" no Enter). Clique duas vezes num perfil para renomeá-lo e arraste para mudar a ordem.
   Arraste uma tecla sobre outra para trocar as ações delas.
5. Para teclas extras do seu teclado (Backspace, Tab, "000", etc.) use **Aprender tecla do deck** e aperte a tecla.

A configuração é salva automaticamente em `%APPDATA%\NumDeck\config.json`.

### Ações disponíveis

| Ação | Detalhes |
|---|---|
| Abrir aplicativo | Um ou mais aplicativos escolhidos na lista dos instalados, ou qualquer arquivo (.exe, .lnk, .bat/.ps1…). Opcionalmente traz para a frente a janela já aberta. |
| Abrir pasta | Abre o Explorador de Arquivos numa pasta específica (aceita `%VARIÁVEIS%` e caminhos de rede) ou em Downloads, Documentos, Área de Trabalho, Este Computador etc. |
| Abrir navegador com abas | Chrome, Edge, Comet, Brave, Opera, Vivaldi, Firefox (detectados automaticamente) ou qualquer outro .exe. Uma URL por linha, com opção de nova janela e de perfil do navegador. |
| Tocar som | MP3/WAV/etc., com volume e saída de áudio por tecla. Apertar de novo para o som. |
| Macro / atalho | Um passo por linha: `Ctrl+Shift+M`, `texto: Olá`, `espera: 300`. Há um campo que grava a combinação pressionada. |
| Mídia / volume | Play/pause, próxima, anterior, parar, volume ±, mudo, mudo do microfone. |
| Trocar perfil | Próximo, anterior ou um perfil específico (com aviso na bandeja). |

## Limitações

- **Programas rodando como administrador**: o Windows não deixa um app comum bloquear teclas neles. Use **Reiniciar como admin** se precisar.
- **Jogos que leem o teclado em baixo nível** (Raw Input/DirectInput) podem continuar vendo a tecla do deck, mesmo bloqueada.
- A tecla **Num Lock** do deck ainda liga e desliga o Num Lock do sistema (o LED é global). As outras teclas funcionam com o Num Lock ligado ou desligado.

## Como funciona

- **Raw Input** (`RegisterRawInputDevices` com `RIDEV_INPUTSINK`) diz de qual teclado veio cada tecla.
- Um hook **`WH_KEYBOARD`** (DLL nativa `native\NumDeckHook64.dll`) é consultado antes de a tecla chegar ao programa em foco. Ele pergunta ao NumDeck se a tecla veio do deck e, se veio, a descarta. O registro do Raw Input sempre chega antes, e as consultas são casadas com ele em ordem (FIFO) pelo scan code.
- Um hook de baixo nível (`WH_KEYBOARD_LL`) **não** serve: bloquear a tecla nele também cancela o Raw Input, e aí não dá para saber de qual teclado ela veio. Isso foi verificado no desenvolvimento.

## Compilar

Requisitos: .NET 10 SDK e, só para recompilar a DLL do hook, [Zig](https://ziglang.org) (`winget install zig.zig`).

```powershell
powershell -ExecutionPolicy Bypass -File native\build.ps1   # DLL do hook (já incluída em native\bin)
dotnet test
dotnet run --project src\NumDeck.App
dotnet publish src\NumDeck.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

Para gerar o instalador e os arquivos de uma release, veja [RELEASING.md](RELEASING.md).

Estrutura:

- `src/NumDeck.Core`: modelos, configuração JSON, ações (navegadores, sons, macros, mídia) e o motor de perfis.
- `src/NumDeck.Input`: enumeração de teclados, Raw Input, ponte com o hook.
- `src/NumDeck.App`: interface WPF (tema Fluent claro/escuro) e ícone na bandeja.
- `native/`: código C do hook.
- `installer/`: script do instalador (Inno Setup).
- `scripts/build-release.ps1`: gera instalador, zip portátil e checksums em `dist/`.
- `tests/NumDeck.Core.Tests`: testes xUnit.
