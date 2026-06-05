# ImobilizadoContabil

Migração para **C# / WinForms (.NET Framework 4.8, x86)** de módulos do sistema
legado **Clipper `Ju2.exe` (DOS)**: imobilizado/depreciação, apropriações, plano de
contas e **lançamentos contábeis do MOVFIN** (incluindo partida dobrada/composto).
Substitui passos manuais do DOS por código testável, **mantendo compatibilidade
total com as DBFs** que o resto do fluxo (sistema "apoioparceiro") continua lendo.

- Fonte Clipper original: `C:\Clipper_Migração\JU2` (família `FLT_*`, núcleo `FLT_DEP.PRG`).
- Dados de referência: ficam **fora do repositório** (ver "Dados sensíveis").

## ⚠️ Dados sensíveis e produção (LER ANTES DE QUALQUER COISA)

1. **DBFs são dados contábeis de terceiros, em produção.** NUNCA versionar `.dbf`,
   `.cdx`, `.fpt`, planilhas — já bloqueados no `.gitignore`.
2. **Por padrão, tudo é leitura.** Nenhuma gravação em `MOVFIN`/`IMOBIL` real sem
   pedido explícito do usuário. Ao testar gravação, **sempre apontar para uma CÓPIA**,
   nunca para o diretório de produção (CONTAB).
3. **Não acessar dados financeiros pessoais** do usuário (ex.: pastas de gastos/IRPF).
4. Sem senhas/credenciais hardcoded — manter assim.

## Stack

- **.NET Framework 4.8** + Windows Forms para o app/gravação; **netstandard2.0** para
  os núcleos puros (motores/domínio).
- **VFPOLEDB.1** (driver Visual FoxPro, **32-bit only**) para ler/gravar DBF →
  build **x86** obrigatório nos projetos que tocam DBF.
- `packages.config`/NuGet; a pasta `packages/` é gitignored (precisa restore na 1ª build).
- C# com recursos compatíveis com **LangVersion 7.3** em alguns pontos (sem `"`
  dentro de interpolação `$"..."`, etc.).

## Estrutura

| Projeto | Framework | Papel |
|---|---|---|
| `src/Imobilizado.Core` | netstandard2.0 | Motor de depreciação + domínio, puro (sem UI/DBF). |
| `src/Contabil.Core` | netstandard2.0 | Plano de contas (resolução de apelidos DESC2→NUMCONTA), motor de saldo (balancete), motor de apropriação. |
| `src/Imobilizado.Dados` | net48 (x86) | Acesso a DBF via VFPOLEDB: `MovfinGravador` (ledger), `ImobilGravador` (cadastro). |
| `src/Imobilizado.App` | net48 (x86) | **App WinForms** (`ImobilizadoContabil.exe`): bens, depreciação, apropriação, CRUD do plano, **editor de lançamentos** (simples e composto). |
| `tools/*` | console | Reconciliadores/validadores que rodam os motores contra dados reais. |

Solução: `ImobilizadoContabil.slnx` (formato novo do VS 2026).

## Build & teste

- IDE: Visual Studio 2026 Community. MSBuild em
  `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`.
- Build do app (CLI):
  `MSBuild src\Imobilizado.App\Imobilizado.App.csproj /p:Configuration=Debug /p:Platform=x86`
- **Smoke test headless** (instancia todos os forms, sem abrir janela):
  `ImobilizadoContabil.exe --selftest`  → imprime `SELFTEST OK`.
- Antes de buildar, **matar instâncias** do exe que travam as DLLs:
  `Get-Process ImobilizadoContabil | Stop-Process -Force`.
- Modos de diagnóstico (apontar para CÓPIA dos dados):
  `--testcomposto <pasta> [movid]` · `--capturacomposto <pasta> <movid> <png>` ·
  `--testmask` · `--lancamentos` · `--placon` · `--apropriacao`.

## Modelo de dados MOVFIN (essencial)

- Colunas-chave: `MOV_ID, OUTRO_ID, DATA, DEBITO, CREDITO, VALOR, HIST, DOC, FORN,
  TIPO, TP_FIN, VENC, DOC_FISC, EMISSOR, DATA_EMI`.
- **`MOV_ID` NÃO é único** — é 0 em lançamentos gerados pelo sistema e **reinicia a
  cada exercício** (o mesmo número reaparece em anos diferentes). A identidade única
  de uma linha é o **RECNO** (número físico do registro). Edição/exclusão sempre por
  `WHERE RECNO()=?`.
- **Composto (partida dobrada)** = 1 mestre (`OUTRO_ID=0`) + N detalhes
  (`OUTRO_ID = MOV_ID do mestre`). **É sempre de um único dia** (multi-dia não existe;
  aparência multi-dia = lixo de colisão de MOV_ID entre anos). As linhas costumam
  compartilhar o mesmo **`DOC_FISC`** (referência fiscal).
- Por isso, agrupar/ler/excluir um composto é **ancorado na DATA**:
  `WHERE (MOV_ID=? OR OUTRO_ID=?) AND DATA=?`.

O histórico completo das decisões está em [`docs/DECISOES.md`](docs/DECISOES.md).

## Convenções de código

- **Português** em nomes, métodos e comentários — manter.
- Prefixos: `Frm` (forms), `T` (classes estilo Delphi), `Gravador`/`Motor`/`Engine`.
- Para layout WinForms, **não confiar só no código**: capturar a janela
  (`--capturacomposto`/PrintWindow) e olhar.
- Mudou regra/modelo? Validar **empiricamente contra os dados reais** (numa cópia)
  antes de afirmar que funciona.

## Colaboração (multi-dev)

- Cada dev usa sua própria conta Claude; a colaboração é **via Git** (branches + PR).
- A "memória" do Claude é **local de cada máquina** — o conhecimento durável vive
  neste `CLAUDE.md` e em `docs/DECISOES.md` (versionados).
- Cada dev precisa: VS 2026, .NET 4.8, **driver VFPOLEDB 32-bit**, NuGet restore, e
  uma **cópia local** dos DBF de teste (passada por fora do git).
