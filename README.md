# ImobilizadoContabil

Migração para C# do módulo de **imobilizado / depreciação** do sistema legado
Clipper `Ju2.exe` (DOS). Substitui o passo manual no DOS por código testável,
mantendo compatibilidade total com as DBFs que o resto do fluxo (apoioparceiro)
continua lendo.

Fonte Clipper original: `C:\Clipper_Migração\JU2` (família `FLT_*`, núcleo em `FLT_DEP.PRG`).
Dados reais de referência: `C:\Clipper_Migração\DADOS Clipper`.

## Regra de depreciação (validada contra dados reais)

Depreciação **linear sobre custo**, em Real:

```
quota_mensal     = base * taxa_anual_grupo / 1200
acumulada(mês)   = min(base, dep_inicial + quota * meses_desde_partida)
quota_do_mês     = acumulada(mês) - acumulada(mês anterior)   // > 0 ⇒ gera lançamento
```

- **base** = campo `VAL_UFIR` do IMOBIL. O nome é legado (UFIR extinta em 2000,
  hoje 1:1 com o Real); o campo guarda a base depreciável em Reais. Não se usa
  `VAL_AQUIS` (tem moeda antiga — Cruzeiro/Cr$ — misturada).
- **taxa** vem da conta-**grupo** (grau 4) do plano de contas, não da conta
  analítica. Ex.: bem com `CONTAB=12265001` → grupo `12265000` (Tratores, 10% a.a.).
- **partida** = `DATA_CORR` (fallback `DATA_AQUIS`); depreciação inicial = `DEP_UFIR`.
- Bem deprecia **já no mês de aquisição**. Para ao atingir 100%.
- Lançamentos são **agregados por par** (conta de resultado, conta de dep. acumulada):
  uma linha por par, débito no resultado/despesa, crédito na dep. acumulada,
  `DOC = "SIST_IMOB"`.

## Estrutura

| Projeto | Framework | Papel |
|---|---|---|
| `src/Imobilizado.Core` | netstandard2.0 | Motor + domínio, puro (sem UI, sem VFPOLEDB). Reutilizável no futuro WinForms net48. |
| `src/Imobilizado.Dados` | net48 (x86) | Gravação no MOVFIN (depreciação) e no IMOBIL (cadastro) via VFPOLEDB. x86 porque o driver é 32-bit. |
| `tools/Imobilizado.Reconciliador` | net8.0 | Console: roda o motor e compara com os lançamentos reais do MOVFIN. |
| `src/Contabil.Core` | netstandard2.0 | Núcleo de contabilidade: resolução de apelidos + cálculo de saldo (fatia do balancete) + motor de apropriação de custos (`FLT_APRO`). |
| `src/Imobilizado.App` | net48 (x86) | **Tela WinForms** (`ImobilizadoContabil.exe`): bens + depreciação + **apropriação** + **CRUD do plano de contas** + **editor de lançamentos do MOVFIN** (partida simples), com preview e gravação. |
| `tools/Imobilizado.Lancador` | net48 (x86) | Console: calcula a depreciação do mês e grava no MOVFIN. Dry-run por padrão. |

## Rodar a reconciliação

```
dotnet run --project tools/Imobilizado.Reconciliador -- "C:\Clipper_Migração\DADOS Clipper" 202401
```

Compara, linha a linha, a depreciação calculada pelo motor com os lançamentos
`SIST_IMOB` realmente gravados no MOVFIN da competência. Em jan/2024 (mês do
snapshot do IMOBIL) casa 26/27; o único outlier corresponde a um ajuste manual
feito no cadastro em 2024-01-29.

## Lançar a depreciação (gravar no MOVFIN)

```
# dry-run (não escreve nada, só mostra o lote):
dotnet run --project tools/Imobilizado.Lancador -- "<pastaDados>" 202707

# gravar de fato:
dotnet run --project tools/Imobilizado.Lancador -- "<pastaDados>" 202707 --gravar

# reprocessar um mês já lançado (exclui os SIST_IMOB do mês e regrava):
dotnet run --project tools/Imobilizado.Lancador -- "<pastaDados>" 202707 --gravar --substituir
```

Sem `--gravar` é sempre dry-run. Com `--gravar`, aborta se o mês já tiver
lançamentos (a menos de `--substituir`). **Teste sempre numa cópia antes de
apontar para o diretório de produção (CONTAB).**

## Tela WinForms (`ImobilizadoContabil.exe`)

```
dotnet run --project src/Imobilizado.App
```

Lembra a última pasta de dados e carrega sozinha na abertura. Aba **Bens** lista o
cadastro com taxa/base/quota/situação na competência e permite **Incluir** e
**Alterar / Baixar** bens (duplo-clique numa linha abre a edição); aba
**Depreciação do mês** mostra o lote calculado (preview), e o botão **Gravar no
MOVFIN** grava (com confirmação; trava se o mês já existe, a menos de marcar
"Substituir").

Datas vazias (ex.: bem sem baixa) são gravadas no IMOBIL como **data em branco do
VFP** (via `CTOD('')`), idêntica aos registros legados — compatível com o Ju2.

## Próximos passos (ainda não feitos)

1. ~~Camada de gravação no MOVFIN.~~ ✅ Feita (`Imobilizado.Dados` + `Imobilizado.Lancador`).
2. ~~Tela WinForms: bens + rodar depreciação do mês.~~ ✅ Feita (`Imobilizado.App`).
3. ~~Cadastro editável de bens (incluir/alterar/baixar).~~ ✅ Feito (`FrmBem` + `ImobilGravador`).
4. Módulo de **apropriações** (`FLT_APRO`) — mais complexo, 2ª rodada.
5. **Lançamentos/ajustes contábeis** manuais.
