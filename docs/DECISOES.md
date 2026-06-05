# Decisões técnicas — ImobilizadoContabil

Histórico das decisões e descobertas validadas contra os dados reais. Serve para
qualquer dev (e qualquer Claude) entrar com o mesmo contexto. **Sempre validar
empiricamente numa CÓPIA dos dados antes de afirmar que algo funciona.**

## Depreciação (motor)

- Depreciação **linear sobre custo, em Real**:
  `quota_mensal = base * taxa_anual_grupo / 1200`;
  `acumulada(mês) = min(base, dep_inicial + quota * meses_desde_partida)`.
- **base** = `VAL_UFIR` do IMOBIL (nome legado; hoje é Real, 1:1). Não usar
  `VAL_AQUIS` (tem moeda antiga misturada — Cruzeiro/Cr$).
- **taxa** vem da **conta-grupo (grau 4)** do plano, não da analítica
  (ex.: `CONTAB=12265001` → grupo `12265000`).
- **partida** = `DATA_CORR` (fallback `DATA_AQUIS`); dep. inicial = `DEP_UFIR`.
  Deprecia já no mês de aquisição; para em 100%.
- Lançamentos **agregados por par** (resultado, dep. acumulada), `DOC = "SIST_IMOB"`.
- Validação: jan/2024 casa 26/27 vs MOVFIN real (o outlier é um ajuste manual de 2024-01-29).
- Datas vazias gravadas como **data em branco do VFP** via `CTOD('')` (igual ao legado).

## Plano de contas e saldo

- `PlanoContas` resolve **DESC2 → NUMCONTA** (apelidos). Conta **analítica** =
  posições 6-8 do número ≠ "000". Contábil tem DESC2=número; financeira tem apelido
  com "#".
- Contas financeiras: o MOVFIN referencia banco por código `NBANCO` de 2 dígitos
  (ex.: "04") → mapeado para o `CONTAB` do banco via `BANCOS.DBF`.
- Motor de saldo (`EngineSaldo`) e apropriação (`FLT_APRO`) validados ~99,7% vs `PTPLA<ano>`.
- Hierarquia derivada do número (máscara 1.1.1.2.3); `GRAU` é pouco confiável (muitos em branco).

## MOVFIN: identidade e edição

- **`MOV_ID` não é único.** ~13% = 0 (lançamentos gerados: SIST_*), e os não-zero se
  repetem porque **o contador reinicia a cada exercício**. A identidade única de uma
  linha é o **RECNO** (número físico).
- VFPOLEDB aceita `SELECT RECNO() AS NRECNO`, e `WHERE RECNO()=?` em UPDATE/DELETE —
  atinge só a linha certa mesmo com MOV_ID repetido. **Edição/exclusão sempre por RECNO.**
- **RECNO é estável**: `DELETE` no VFP só marca o registro (flag `*`), NÃO renumera.
  Só `PACK`/`ZAP` renumeram — e **nenhum PACK do Ju2 toca o MOVFIN** (verificado nos
  fontes). Logo, identificar por RECNO é seguro no fluxo normal.

## Composto (partida dobrada)

- Estrutura: 1 **mestre** (`OUTRO_ID=0`, um lado da partida com o valor) + N
  **detalhes/meias-entradas** (`OUTRO_ID = MOV_ID do mestre`, o outro lado).
- **Regra de domínio (confirmada pelo usuário): um composto é SEMPRE de um único dia.**
  Grupo multi-dia não existe; aparência multi-dia vinha da colisão de MOV_ID entre anos.
- As linhas costumam compartilhar o mesmo **`DOC_FISC`** (referência fiscal) —
  preservado na edição (não entra no filtro de agrupamento, pois nem sempre preenchido).
- **Agrupar/ler/excluir composto é ancorado na DATA**:
  `WHERE (MOV_ID=? OR OUTRO_ID=?) AND DATA=?`. (Tentamos data-exata só como filtro e
  depois ano; o correto é a data do grupo, derivada da linha selecionada.)
- Roteamento (FrmLancamentos): um detalhe só abre o editor de composto se existir um
  **mestre no mesmo dia** (`MestreExiste`); senão é tratado como avulso (sem beco-sem-saída).

### Editor de composto (FrmGrupoComposto)

- **Edição DIRETA**: grid com TODAS as linhas do grupo (mestre + detalhes), cada
  célula editável — Débito, Crédito, **Valor** (inclusive o do principal, livre, sem
  agregação automática), Histórico. Header compartilhado: Data, Tipo, Doc. fiscal.
- **Salvar = por RECNO**: `AlterarLancamento` (UPDATE WHERE RECNO()=?) para as
  existentes, `ExcluirLancamento` para removidas, `InserirLancamento` para novas
  (já com `OUTRO_ID` do mestre). **Preserva a estrutura** (não usa delete+reinsert).
- Mostra balanço Débitos/Créditos/Diferença (verde quando zera) — não bloqueia.
- **Máscara de centavos** na coluna Valor (estilo calculadora): cada dígito entra pela
  direita (1→0,01, 12→0,12, 123→1,23, 1234→12,34); backspace remove o último; a 1ª
  tecla recomeça do zero; vírgula/letra ignoradas. `CellEndEdit` reformata N2.
- O editor "principal + contrapartidas" (`FrmLancamentoComposto`) ficou só para
  **incluir** composto novo.

## Armadilhas de WinForms já resolvidas

- **Botão sumindo**: `Anchor=Top|Right` + posição absoluta num painel `Dock=Bottom`
  calcula a âncora contra a largura errada e joga o botão pra fora. Usar
  `FlowLayoutPanel` (posiciona sozinho). Confirmar **visualmente** com PrintWindow.
- **Edição de célula não salva**: `DataGridView` não comita a célula em edição ao
  clicar no botão. Chamar `EndEdit()`/`CommitEdit()` no início do Salvar.
- Sem aspas `"` dentro de interpolação `$"..."` (LangVersion 7.3) — extrair p/ variável.

## Tarefas futuras (opcionais)

- Reconciliação bancária (~1% final do balancete).
- `FLT_RECI` (recibos + INSS/IR), `FLT_CHEQ` (cheques), se necessário.
- Aplicar a combobox de busca de conta também na contrapartida do composto.
