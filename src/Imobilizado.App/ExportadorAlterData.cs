using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Contabil.Core;
using Imobilizado.Dados;
using OfficeOpenXml;

namespace Imobilizado.App
{
    /// <summary>Modo de tradução das contas no lote AlterData.</summary>
    public enum ModoExportAlterData
    {
        Reduzido = 0,   // NUMCONTA → RELACIONA.REDUZIDO  (PADRÃO — igual ao rbDesc2 do Contabil2020)
        NovoCod = 1,    // NUMCONTA → RELACIONA.NOVOCOD   (alternativa/contabilidade)
        NumConta = 2    // mantém o NUMCONTA de 8 díg.    (debug)
    }

    /// <summary>Uma linha pronta para o lote AlterData (já traduzida).</summary>
    public sealed class LinhaAlterData
    {
        public string Debito;      // código traduzido (ou "-1" se sem mapeamento, ou "" se lado vazio)
        public string Credito;
        public DateTime Data;
        public decimal Valor;
        public string Historico;
        public string NrDocumento; // "DOC.n.:" + (DOC ou DOC_FISC)
        public int Recno;          // rastreabilidade (linha de origem no MOVFIN)

        public bool SemMapeamento => Debito == "-1" || Credito == "-1";
        public bool MeiaEntrada =>
            (string.IsNullOrEmpty(Debito) && !string.IsNullOrEmpty(Credito)) ||
            (!string.IsNullOrEmpty(Debito) && string.IsNullOrEmpty(Credito));
    }

    /// <summary>
    /// Gera o lote de importação do AlterData a partir do MOVFIN pareado.
    ///
    /// Reproduz o pipeline do Contabil2020 de forma enxuta:
    ///   apelido/cód-banco --(PlanoContas.ResolverContabil)--> NUMCONTA(8) --(RELACIONA)--> REDUZIDO
    ///   e grava no layout de 10 colunas (ver ExportaLoteExcel.cs do Contabil2020).
    ///
    /// Diferente do Contabil2020, NÃO há etapa FrmRazao: nosso MOVFIN já é pareado na origem
    /// (cada linha carrega débito+crédito; folha pareada desde 01/mai/2026).
    /// </summary>
    public sealed class ExportadorAlterData
    {
        private readonly PlanoContas _plano;
        private readonly Dictionary<string, ItemRelaciona> _relaciona;

        public ExportadorAlterData(PlanoContas plano, Dictionary<string, ItemRelaciona> relaciona)
        {
            _plano = plano ?? throw new ArgumentNullException(nameof(plano));
            _relaciona = relaciona ?? throw new ArgumentNullException(nameof(relaciona));
        }

        /// <summary>
        /// Traduz uma conta do MOVFIN (apelido/cód-banco) para o código do lote.
        /// Vazio → "" (lado em branco). Sem correspondência → "-1".
        /// </summary>
        public string Traduzir(string contaRaw, ModoExportAlterData modo)
        {
            var raw = (contaRaw ?? "").Trim();
            if (raw.Length == 0) return "";

            string numConta = _plano.ResolverContabil(raw);   // → NUMCONTA analítico, ou null
            if (numConta == null) return "-1";

            if (modo == ModoExportAlterData.NumConta) return numConta;

            if (!_relaciona.TryGetValue(numConta.Trim(), out var item)) return "-1";

            if (modo == ModoExportAlterData.NovoCod)
                return string.IsNullOrEmpty(item.NovoCod) ? "-1" : item.NovoCod;

            // Reduzido (padrão)
            return item.Reduzido != 0 ? item.Reduzido.ToString(CultureInfo.InvariantCulture) : "-1";
        }

        /// <summary>
        /// Pareia os COMPOSTOS (partida dobrada master/detalhe) — versão determinística do
        /// AltereDataTableParaRazao do Contabil2020. No MOVFIN do Clipper o composto fica como
        /// um MESTRE (OUTRO_ID=0) com só um lado preenchido (a contrapartida, ex.: o banco) e
        /// N DETALHES (OUTRO_ID = MOV_ID do mestre) com o outro lado. O pareamento copia o lado
        /// do mestre para cada detalhe (virando partida dobrada completa) e descarta o mestre —
        /// mas só quando a SOMA dos detalhes bate com o valor do mestre (trava do Contabil2020).
        ///
        /// Casa por (OUTRO_ID==MOV_ID do mestre) E MESMA DATA — o composto é sempre do mesmo dia,
        /// o que protege contra a não-unicidade do MOV_ID entre exercícios. Lado preenchido que
        /// começa com '*' (marcador especial) é ignorado, igual ao original. Muta os objetos
        /// detalhe no lugar e retorna a lista sem os mestres pareados.
        /// </summary>
        public List<LancamentoMovfin> ParearCompostos(List<LancamentoMovfin> orig)
        {
            var removidos = new HashSet<int>();   // RECNOs dos mestres pareados (a descartar)

            var mestres = orig.Where(m => m.MovId != 0m && m.OutroId == 0m &&
                (string.IsNullOrWhiteSpace(m.Debito) ^ string.IsNullOrWhiteSpace(m.Credito))).ToList();

            foreach (var m in mestres)
            {
                bool debVazio = string.IsNullOrWhiteSpace(m.Debito);
                string ladoCheio = (debVazio ? m.Credito : m.Debito) ?? "";
                if (ladoCheio.TrimStart().StartsWith("*")) continue;   // marcador especial: não pareia

                var filhos = orig.Where(f => !ReferenceEquals(f, m) &&
                    f.OutroId == m.MovId && f.Data == m.Data &&
                    (debVazio ? string.IsNullOrWhiteSpace(f.Credito) : string.IsNullOrWhiteSpace(f.Debito)))
                    .ToList();

                if (filhos.Count == 0) continue;
                if (filhos.Sum(f => f.Valor) != m.Valor) continue;     // trava: soma tem que bater

                foreach (var f in filhos)
                {
                    if (debVazio) f.Credito = m.Credito;
                    else f.Debito = m.Debito;
                }
                removidos.Add(m.Recno);
            }

            return orig.Where(l => !removidos.Contains(l.Recno)).ToList();
        }

        /// <summary>
        /// Filtro "PREPARADO PARA A CONTABILIDADE" (FLT_1) — aplicado ANTES de parear/traduzir,
        /// igual ao que o Paulo passa antes de submeter ao FrmRazão. EXCLUI os registros que NÃO
        /// estão preparados: lado que não casa EXATO com um DESC2 analítico do PLACON (nem é banco
        /// 2-díg com CONTAB analítico), o que inclui os marcados com '*' no início (marcador legado
        /// de "não-preparado", ex.: *31340009) e bancos sem CONTAB. Meia-entrada (composto) com o
        /// lado preenchido válido é MANTIDA (será pareada depois). Os excluídos saem em
        /// <paramref name="excluidos"/> para transparência (nada some calado).
        /// </summary>
        public List<LancamentoMovfin> FiltrarPreparados(List<LancamentoMovfin> orig, out List<LancamentoMovfin> excluidos)
        {
            var manter = new List<LancamentoMovfin>();
            var fora = new List<LancamentoMovfin>();
            foreach (var l in orig)
            {
                if (_plano.ValidoParaContabilidade(l.Debito, l.Credito)) manter.Add(l);
                else fora.Add(l);
            }
            excluidos = fora;
            return manter;
        }

        private static bool Len2(string s) => s != null && s.Trim().Length == 2;
        private static bool Estrela(string s) => s != null && s.TrimStart().StartsWith("*");
        private static bool Vazio(string s) => string.IsNullOrWhiteSpace(s);

        /// <summary>
        /// Pareia a FOLHA solta do SIST_RURAL (porte do FOLHA_TentaAdvinhar). Delega ao
        /// <see cref="PareadorFolha"/> (reutilizado pelo balancete). Sem corte de data: pareia
        /// todos os dias (pós-maio não tem folha solta, então é no-op).
        /// </summary>
        public List<LancamentoMovfin> ParearFolha(List<LancamentoMovfin> orig)
            => new PareadorFolha(_plano).Parear(orig);

        /// <summary>
        /// Pareia as TRANSFERÊNCIAS entre contas financeiras — porte do JuntaRegistrosTransfBancos
        /// do Contabil2020. Uma transferência banco↔banco fica gravada DUAS vezes (espelho): uma
        /// linha referencia um banco pelo CÓDIGO de 2 díg e o outro lado pelo apelido DESC2; a
        /// linha-espelho inverte. Aqui casamos o par (mesma DATA, soma batendo, e os números/DESC2
        /// se cruzando) e DESCARTAMOS a linha cujo CRÉDITO é o código de 2 díg, convertendo o
        /// DÉBITO-código da linha sobrevivente para o DESC2 — exatamente como o original. Isso
        /// elimina a dupla contagem (o mesmo "espelho financeiro" já tratado no balancete).
        ///
        /// 2 passes, como o original: (1) casando por mesma data; (2) os "pais perdidos" casando
        /// ignorando a data. Trava: só pareia quando soma(filhos) == valor(pai).
        /// </summary>
        public List<LancamentoMovfin> ParearTransferencias(List<LancamentoMovfin> orig)
        {
            // candidatos: um lado é código de banco (2 díg), o outro é DESC2 de um banco contábil
            var altere = orig.Where(r =>
                (Len2(r.Debito) && !Estrela(r.Credito) && !Vazio(r.Credito) && _plano.EhBancoContabilDesc2((r.Credito ?? "").Trim()))
                ||
                (Len2(r.Credito) && !Estrela(r.Debito) && _plano.EhBancoContabilDesc2((r.Debito ?? "").Trim()))
            ).ToList();

            var removidos = new HashSet<int>();   // NOVO_ID == -1 (descartar)
            var usados = new HashSet<int>();       // NOVO_ID != 0 (já vinculado como filho)
            var paisPerdidos = new List<LancamentoMovfin>();

            List<LancamentoMovfin> AcharFilhos(LancamentoMovfin pai, bool exigeMesmaData)
            {
                return altere.Where(f => !ReferenceEquals(f, pai)
                    && (!exigeMesmaData || f.Data == pai.Data)
                    && Len2(f.Debito)
                    && !usados.Contains(f.Recno)
                    && _plano.NBancoDesc2((f.Debito ?? "").Trim()).Trim() == (pai.Debito ?? "").Trim()
                    && (f.Credito ?? "").Trim() == _plano.NBancoDesc2((pai.Credito ?? "").Trim()).Trim()
                ).ToList();
            }

            bool Casar(LancamentoMovfin pai, List<LancamentoMovfin> filhos)
            {
                decimal soma = filhos.Sum(f => f.Valor);
                if (filhos.Count > 1 && soma != pai.Valor)
                {
                    var salvo = filhos.FirstOrDefault(f => f.Valor == pai.Valor);
                    if (salvo != null) { filhos = new List<LancamentoMovfin> { salvo }; soma = salvo.Valor; }
                }
                if (soma != 0 && soma == pai.Valor)
                {
                    foreach (var f in filhos)
                    {
                        f.Debito = _plano.NBancoDesc2((f.Debito ?? "").Trim());  // cód banco → DESC2
                        usados.Add(f.Recno);
                    }
                    removidos.Add(pai.Recno);
                    return true;
                }
                return false;
            }

            // pass 1: pai com CRÉDITO = código de banco contábil, casando por mesma data
            foreach (var pai in altere.Where(r => Len2(r.Credito) && _plano.EhBancoContabil((r.Credito ?? "").Trim())))
            {
                var filhos = AcharFilhos(pai, true);
                if (filhos.Count == 0) { paisPerdidos.Add(pai); continue; }
                Casar(pai, filhos);
            }

            // pass 2: pais perdidos — casa ignorando a data
            foreach (var pai in paisPerdidos)
            {
                if (removidos.Contains(pai.Recno)) continue;
                var filhos = AcharFilhos(pai, false);
                if (filhos.Count > 0) Casar(pai, filhos);
            }

            return orig.Where(l => !removidos.Contains(l.Recno)).ToList();
        }

        /// <summary>
        /// Monta as linhas do lote a partir dos lançamentos do MOVFIN.
        /// Replica os filtros do PesquiseRazao: ignora linha com ambos os lados vazios e valor 0;
        /// HIST cai para FORN quando vazio.
        /// </summary>
        public List<LinhaAlterData> MontarLinhas(
            IEnumerable<LancamentoMovfin> lancamentos, ModoExportAlterData modo,
            bool excluirDebIgualCred = true)
        {
            var saida = new List<LinhaAlterData>();
            foreach (var l in lancamentos)
            {
                bool debVazio = string.IsNullOrWhiteSpace(l.Debito);
                bool credVazio = string.IsNullOrWhiteSpace(l.Credito);
                if (debVazio && credVazio) continue;
                if (l.Valor == 0m) continue;

                string deb = Traduzir(l.Debito, modo);
                string cred = Traduzir(l.Credito, modo);

                // pula lançamentos auto-anulados (débito == crédito), como o chkExcluiDebCre do
                // Contabil2020 — é o caso do salário-família que vira D=IMP#INSS / C=IMP#INSS.
                if (excluirDebIgualCred && !debVazio && !credVazio && deb == cred) continue;

                string hist = (l.Historico ?? "").Trim();
                if (hist.Length == 0 && !string.IsNullOrWhiteSpace(l.Forn))
                    hist = l.Forn.Trim();

                string doc = (l.Doc ?? "").Trim();
                if (doc.Length == 0) doc = (l.DocFisc ?? "").Trim();
                string nrDoc = doc.Length > 0 ? "DOC.n.:" + doc : "";

                saida.Add(new LinhaAlterData
                {
                    Debito = deb,
                    Credito = cred,
                    Data = DateTime.ParseExact(l.Data, "yyyyMMdd", CultureInfo.InvariantCulture),
                    Valor = Math.Round(l.Valor, 2),
                    Historico = hist,
                    NrDocumento = nrDoc,
                    Recno = l.Recno
                });
            }
            return saida;
        }

        /// <summary>
        /// Grava o .xlsx no layout AlterData (10 colunas A-J), idêntico ao ExportaLoteExcel
        /// do Contabil2020: cabeçalho na linha 1, dados a partir da linha 2, hist. fixo 98,
        /// data dd/MM/yyyy, valor ###,###,##0.00, código numérico vira número (paridade Interop).
        /// </summary>
        public static void GravarXlsx(IEnumerable<LinhaAlterData> linhas, string caminho)
        {
            if (!caminho.ToLower().EndsWith(".xlsx")) caminho += ".xlsx";
            var fi = new FileInfo(caminho);
            if (fi.Exists) fi.Delete();

            using (var pkg = new ExcelPackage(fi))
            {
                var ws = pkg.Workbook.Worksheets.Add("Planilha1");

                ws.Cells[1, 1].Value = "lancto auto";
                ws.Cells[1, 2].Value = "debito";
                ws.Cells[1, 3].Value = "credito";
                ws.Cells[1, 4].Value = "data";
                ws.Cells[1, 5].Value = "valor";
                ws.Cells[1, 6].Value = "cód histórico";
                ws.Cells[1, 7].Value = "complemento historico";
                ws.Cells[1, 8].Value = "Ccusto debito";
                ws.Cells[1, 9].Value = "Ccusto credito";
                ws.Cells[1, 10].Value = "NrDocumento";

                ws.Column(1).Width = 11;
                ws.Column(2).Width = 14;
                ws.Column(3).Width = 14;
                ws.Column(4).Width = 12;
                ws.Column(5).Width = 14;
                ws.Column(6).Width = 8;
                ws.Column(7).Width = 45;
                ws.Column(8).Width = 8;
                ws.Column(9).Width = 8;
                ws.Column(10).Width = 20;

                int row = 2;
                foreach (var l in linhas)
                {
                    bool debVazio = string.IsNullOrWhiteSpace(l.Debito);
                    bool credVazio = string.IsNullOrWhiteSpace(l.Credito);
                    if (debVazio && credVazio) continue;

                    if (!string.IsNullOrEmpty(l.Debito) && long.TryParse(l.Debito, out long dNum))
                        ws.Cells[row, 2].Value = dNum;
                    else
                        ws.Cells[row, 2].Value = l.Debito;

                    if (!string.IsNullOrEmpty(l.Credito) && long.TryParse(l.Credito, out long cNum))
                        ws.Cells[row, 3].Value = cNum;
                    else
                        ws.Cells[row, 3].Value = l.Credito;

                    ws.Cells[row, 4].Value = l.Data;
                    ws.Cells[row, 4].Style.Numberformat.Format = "dd/MM/yyyy";

                    ws.Cells[row, 5].Value = l.Valor;
                    ws.Cells[row, 5].Style.Numberformat.Format = "###,###,##0.00";

                    ws.Cells[row, 6].Value = 98;
                    ws.Cells[row, 6].Style.Numberformat.Format = "##0";

                    ws.Cells[row, 7].Value = l.Historico;
                    ws.Cells[row, 10].Value = l.NrDocumento;
                    row++;
                }

                pkg.Save();
            }
        }
    }
}
