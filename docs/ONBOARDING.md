# Onboarding — checklist do Roberto

Passo a passo para entrar no projeto **ImobilizadoContabil**. Marque cada item.
Em caso de dúvida sobre o porquê de algo, leia o [`CLAUDE.md`](../CLAUDE.md) e
[`DECISOES.md`](DECISOES.md) na raiz/docs.

## 1. Pré-requisitos (instalar na máquina)

- [ ] **Visual Studio 2026** (Community serve) com a carga **".NET desktop development"**.
- [ ] **.NET Framework 4.8** (Developer Pack) — o app e a camada de dados são net48.
- [ ] **Driver VFPOLEDB 32-bit** (Visual FoxPro OLE DB Provider). **Sem ele o app
      não lê os DBF.** Procure por "VFPOLEDB.msi" / "Visual FoxPro OLEDB". Build é x86
      por causa disso.
- [ ] **Git** (e, se for usar o GitHub por HTTPS, o **Git Credential Manager** que já
      vem com o Git for Windows).
- [ ] (Opcional, só se mexer com leitura de planilha) **AccessDatabaseEngine_X86**.
- [ ] **Claude Max** ativo + **Claude Code** instalado (login com a conta dele).

## 2. Acesso e clone

- [ ] Pedir ao Paulo para te adicionar como **Collaborator** no repositório privado
      do GitHub (Settings → Collaborators).
- [ ] Aceitar o convite (chega por e-mail / aparece no GitHub).
- [ ] Clonar:
      ```
      git clone https://github.com/PAULO_USUARIO/ImobilizadoContabil.git
      cd ImobilizadoContabil
      ```
- [ ] Configurar **sua** identidade git (importante para a autoria dos commits):
      ```
      git config user.name "Roberto ..."
      git config user.email "roberto@exemplo.com"
      ```

## 3. Restaurar dependências e compilar

- [ ] **NuGet restore** (a pasta `packages/` não vem no repositório):
      no VS, clique direito na solução → *Restore NuGet Packages*; ou via linha de comando.
- [ ] Abrir `ImobilizadoContabil.slnx` no VS.
- [ ] **Build em x86** (Debug). Pela linha de comando:
      ```
      MSBuild src\Imobilizado.App\Imobilizado.App.csproj /p:Configuration=Debug /p:Platform=x86
      ```
- [ ] Antes de rebuildar, se o app estiver aberto, **matar o processo** (senão a DLL
      fica travada):
      ```
      Get-Process ImobilizadoContabil | Stop-Process -Force
      ```

## 4. Verificar que está tudo de pé

- [ ] Rodar o **smoke test headless** (instancia todas as telas, sem abrir janela):
      ```
      src\Imobilizado.App\bin\x86\Debug\net48\ImobilizadoContabil.exe --selftest
      ```
      Tem que imprimir **`SELFTEST OK`**.

## 5. Dados de teste (NUNCA vêm pelo git)

- [ ] Pedir ao Paulo uma **cópia dos DBF de teste** (`MOVFIN.DBF`, `placon.DBF`,
      `BANCOS.DBF`…) — entregue **por fora do git** (pendrive / pasta compartilhada).
- [ ] Guardar numa pasta **local** sua (ex.: `C:\dados_teste\`). O app lembra a última
      pasta usada.
- [ ] ⚠️ **Esses dados são de terceiros e de produção.** Nunca commitar, nunca apontar
      gravação para a pasta de produção (CONTAB) sem combinar com o Paulo. Para testar
      gravação, **sempre usar uma cópia**.

## 6. Trabalhando com o Claude

- [ ] Abrir o Claude Code **na pasta do projeto** — ele lê o `CLAUDE.md` e já entra no
      contexto (RECNO, regra do composto, máscara de centavos, etc.).
- [ ] A "memória" do Claude é **local de cada máquina**: decisões importantes devem ir
      para o `CLAUDE.md`/`docs/DECISOES.md` (versionados), não ficar só na memória.
- [ ] Há modos de diagnóstico no app (apontar para CÓPIA dos dados):
      `--selftest`, `--testcomposto <pasta> [movid]`,
      `--capturacomposto <pasta> <movid> <png>`, `--testmask`,
      `--lancamentos`, `--placon`, `--apropriacao`.

## 7. Fluxo de trabalho (git)

- [ ] `main` é a branch estável (sempre compilando). **Não commitar direto nela.**
- [ ] Para cada tarefa, criar uma branch:
      ```
      git checkout -b feature/descricao-curta
      ```
- [ ] Commitar com mensagens claras; subir a branch:
      ```
      git push -u origin feature/descricao-curta
      ```
- [ ] Abrir um **Pull Request** no GitHub e pedir revisão (Paulo, ou usar o Claude para
      revisar) antes de juntar no `main`.
- [ ] Antes de começar algo novo, atualizar:
      ```
      git checkout main && git pull
      ```
- [ ] Combinar com o Paulo **quem mexe em quê** (ex.: um na parte de lançamentos, outro
      no imobilizado) para evitar conflito.

## 8. Convenções a respeitar

- [ ] **Português** em nomes, métodos e comentários.
- [ ] Mudou regra/modelo de dados? **Validar empiricamente contra os dados reais**
      (numa cópia) antes de afirmar que funciona.
- [ ] Mexeu em layout WinForms? **Conferir visualmente** (`--capturacomposto`/PrintWindow),
      não confiar só no código.
- [ ] Edição/exclusão no MOVFIN é **por RECNO** (MOV_ID não é único).

## Problemas comuns

- **"Não acha o provider VFPOLEDB"** → driver 32-bit não instalado, ou build não está x86.
- **Erro de DLL travada ao buildar** → `ImobilizadoContabil.exe` ainda aberto; matar o processo.
- **Falta de pacotes / erros de referência** → faltou o NuGet restore.
- **Roberto vê "arquivo inteiro alterado"** sem ter mexido → quebra de linha; o
  `.gitattributes` resolve (já está no repo). Rodar `git add --renormalize .` se persistir.
