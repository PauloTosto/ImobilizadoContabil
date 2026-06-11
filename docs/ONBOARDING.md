# 📋 Onboarding — Projeto ImobilizadoContabil (para o Roberto)

Documento autossuficiente: dá para ler antes mesmo de ter acesso ao repositório.
Marque cada item. Detalhes técnicos: [`../CLAUDE.md`](../CLAUDE.md) e [`DECISOES.md`](DECISOES.md).

## 0. O que é o projeto (contexto rápido)

Migração para **C# / WinForms (.NET Framework 4.8, x86)** de módulos de um sistema
legado **Clipper `Ju2.exe` (DOS)**: imobilizado/depreciação, apropriações, plano de
contas e **lançamentos contábeis** (incluindo partida dobrada/composto). O objetivo é
substituir passos manuais do DOS por código testável, **mantendo compatibilidade total
com as DBFs** que o resto do fluxo ainda lê.

Trabalhamos **em conjunto via Git** — cada um na sua máquina, com sua própria conta
Claude. Não há sessão compartilhada ao vivo; o que sincroniza é o **código**
(branches + Pull Requests).

## ⚠️ 1. Regras de segurança (LER PRIMEIRO)

- **DBFs são dados contábeis de terceiros, em produção.** Nunca versionar
  `.dbf`/`.cdx`/`.fpt`/planilhas (já bloqueados no `.gitignore`).
- **Por padrão, tudo é leitura.** Nenhuma gravação no `MOVFIN`/`IMOBIL` real sem
  combinar. Para testar gravação, **sempre apontar para uma CÓPIA**, nunca para a
  pasta de produção (CONTAB).
- Sem senhas/credenciais no código — manter assim.

## 2. Pré-requisitos (instalar)

- [ ] **Visual Studio 2026** (Community) com a carga **".NET desktop development"**.
- [ ] **.NET Framework 4.8** (Developer Pack).
- [ ] **Driver VFPOLEDB 32-bit** (Visual FoxPro OLE DB Provider) — **sem ele o app não
      lê os DBF**. Build é **x86** por causa disso.
- [ ] **Git** (com Git Credential Manager, que vem no Git for Windows).
- [ ] **Claude Max** ativo + **Claude Code** instalado (login com a conta dele).
- [ ] *(Opcional, só se mexer com leitura de planilha)* **AccessDatabaseEngine_X86**.

## 3. Acesso e clone

- [ ] Pedir ao **Paulo** para te adicionar como **Collaborator** no repositório privado
      (GitHub → Settings → Collaborators).
- [ ] Aceitar o convite (chega por e-mail).
- [ ] Clonar e configurar **sua** identidade:
      ```bash
      git clone https://github.com/PAULO_USUARIO/ImobilizadoContabil.git
      cd ImobilizadoContabil
      git config user.name "Roberto ..."
      git config user.email "roberto@exemplo.com"
      ```

## 4. Compilar

- [ ] **NuGet restore** (a pasta `packages/` não vem no git): no VS, botão direito na
      solução → *Restore NuGet Packages*.
- [ ] Abrir `ImobilizadoContabil.slnx`.
- [ ] **Build em x86** (Debug). Pela linha de comando:
      ```
      MSBuild src\Imobilizado.App\Imobilizado.App.csproj /p:Configuration=Debug /p:Platform=x86
      ```
- [ ] Se o app estiver aberto, mate o processo antes de rebuildar (senão a DLL trava):
      ```powershell
      Get-Process ImobilizadoContabil | Stop-Process -Force
      ```

## 5. Verificar que está tudo de pé

- [ ] Rodar o **smoke test** (instancia todas as telas, sem abrir janela):
      ```
      src\Imobilizado.App\bin\x86\Debug\net48\ImobilizadoContabil.exe --selftest
      ```
      Tem que imprimir **`SELFTEST OK`**.

## 6. Dados de teste (NUNCA vêm pelo git)

- [ ] Pedir ao Paulo uma **cópia dos DBF de teste** (`MOVFIN.DBF`, `placon.DBF`,
      `BANCOS.DBF`…), entregue **por fora do git** (pendrive / pasta compartilhada).
- [ ] Guardar numa pasta **local** sua (ex.: `C:\dados_teste\`). O app lembra a última
      pasta usada.
- [ ] ⚠️ Dados de terceiros/produção — não commitar, não apontar gravação pra produção
      sem combinar.

## 7. Trabalhando com o Claude

- [ ] Abrir o Claude Code **na pasta do projeto** — ele lê o `CLAUDE.md` e já entra no
      contexto (RECNO, regra do composto, máscara de centavos, regras de depreciação…).
- [ ] A **memória do Claude é local** de cada máquina. Decisões importantes vão para
      `CLAUDE.md` / `docs/DECISOES.md` (versionados), não ficam só na memória.
- [ ] Modos de diagnóstico (apontar pra CÓPIA): `--selftest`,
      `--testcomposto <pasta> [movid]`, `--capturacomposto <pasta> <movid> <png>`,
      `--testmask`, `--lancamentos`, `--placon`, `--apropriacao`.

## 8. Fluxo de trabalho (git)

- [ ] `main` é estável — **não commitar direto nela.**
- [ ] Uma branch por tarefa:
      ```
      git checkout -b feature/descricao-curta
      git push -u origin feature/descricao-curta
      ```
- [ ] Abrir **Pull Request** no GitHub e pedir revisão antes de juntar no `main`.
- [ ] Antes de começar algo novo: `git checkout main && git pull`.
- [ ] Combinar com o Paulo **quem mexe em quê** (ex.: um nos lançamentos, outro no
      imobilizado).

## 9. Convenções a respeitar

- [ ] **Português** em nomes, métodos e comentários.
- [ ] Mudou regra/modelo de dados? **Validar contra os dados reais** (numa cópia) antes
      de afirmar que funciona.
- [ ] Mexeu em layout WinForms? **Conferir visualmente** (`--capturacomposto`/PrintWindow).
- [ ] Edição/exclusão no MOVFIN é **por RECNO** (MOV_ID não é único).

## 🔧 Problemas comuns

- **"Não acha o provider VFPOLEDB"** → driver 32-bit não instalado, ou build não está em x86.
- **DLL travada ao buildar** → `ImobilizadoContabil.exe` ainda aberto; matar o processo.
- **Erros de referência / falta de pacotes** → faltou o NuGet restore.
- **"Arquivo inteiro alterado" sem ter mexido** → quebra de linha (CRLF/LF); o
  `.gitattributes` resolve. Se persistir: `git add --renormalize .`

## 📖 Glossário do domínio

| Termo | Significado |
|---|---|
| **MOVFIN** | A tabela DBF do *ledger* (lançamentos contábeis/financeiros "ao vivo"). |
| **RECNO** | Número físico do registro no DBF — a **identidade única** de uma linha (MOV_ID não serve). |
| **MOV_ID / OUTRO_ID** | IDs lógicos do lançamento. `OUTRO_ID=0` = mestre; `OUTRO_ID=MOV_ID do mestre` = detalhe. MOV_ID **não é único** (reinicia por ano). |
| **Composto** | Partida dobrada: 1 mestre + N detalhes, sempre **do mesmo dia**, normalmente com o mesmo `DOC_FISC`. |
| **PLACON** | Plano de contas (arquivo `placon.DBF`). |
| **DESC2** | Apelido da conta no plano; o MOVFIN referencia contas por esse apelido (ex.: `GER # DIVS`). |
| **IMOBIL** | Cadastro de bens do imobilizado (base da depreciação). |
| **SIST_IMOB / SIST_*** | Tag no campo `DOC` marcando lançamentos gerados pelo sistema (depreciação, etc.). |
| **VFPOLEDB** | Driver OLE DB 32-bit que lê/grava os DBF do Visual FoxPro. |
| **PTPLA&lt;ano&gt;** | Snapshot dos saldos do plano no fim do exercício (referência de validação). |
