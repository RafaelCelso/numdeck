# Como publicar uma release do NumDeck

Repositório: https://github.com/RafaelCelso/numdeck

## 1. Gerar os arquivos

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1 -Version 1.0.0
```

O script roda os testes, publica o app e cria em `dist\`:

| Arquivo | Para quê |
|---|---|
| `NumDeck-Setup-1.0.0.exe` | Instalador (anexar à release) |
| `NumDeck-1.0.0-portable.zip` | Versão portátil (anexar à release) |
| `SHA256SUMS.txt` | Checksums (anexar à release) |
| `release-notes-1.0.0.md` | Texto pronto para a descrição da release |

Requisitos: .NET 10 SDK e Inno Setup 6 (`winget install JRSoftware.InnoSetup`). Feche o NumDeck que estiver rodando da pasta `publish` antes.

Para uma nova versão, use um número maior (`-Version 1.1.0`). O instalador atualiza a versão instalada no lugar e mantém as configurações do usuário.

## 2. Enviar o código (primeira vez)

```powershell
git init -b main
git add .
git commit -m "NumDeck 1.0.0"
git remote add origin https://github.com/RafaelCelso/numdeck.git
git push -u origin main
```

Se o repositório no GitHub já tiver commits (por exemplo, um README criado pelo site), rode antes do push:

```powershell
git pull origin main --allow-unrelated-histories
```

`publish\`, `dist\`, `bin\` e `obj\` não vão para o git (estão no `.gitignore`). A DLL `native\bin\NumDeckHook64.dll` vai, para que o projeto compile sem precisar do Zig.

## 3. Criar a release no GitHub

**Pelo site:** em https://github.com/RafaelCelso/numdeck/releases/new:

1. Em *Choose a tag*, digite `v1.0.0` e escolha *Create new tag on publish*.
2. Título: `NumDeck 1.0.0`.
3. Cole o conteúdo de `dist\release-notes-1.0.0.md` na descrição.
4. Arraste para *Attach binaries* os três arquivos: o instalador, o `.zip` e o `SHA256SUMS.txt`.
5. Clique em *Publish release*.

**Pelo terminal (opcional):** com o GitHub CLI (`winget install GitHub.cli`, depois `gh auth login`):

```powershell
gh release create v1.0.0 dist\NumDeck-Setup-1.0.0.exe dist\NumDeck-1.0.0-portable.zip dist\SHA256SUMS.txt `
  --repo RafaelCelso/numdeck --title "NumDeck 1.0.0" --notes-file dist\release-notes-1.0.0.md
```

## Observações

- O instalador não é assinado digitalmente, então o SmartScreen mostra um aviso na primeira execução ("Mais informações → Executar assim mesmo"). Para tirar o aviso é preciso um certificado de assinatura de código.
- Nunca mude o `AppId` em `installer\NumDeck.iss`: é ele que faz as novas versões atualizarem a instalada.
