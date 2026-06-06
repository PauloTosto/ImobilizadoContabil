using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Contabil.Core;
using Imobilizado.Dados;
using OfficeOpenXml;

namespace Imobilizado.App
{
    /// <summary>
    /// Balancete (nos moldes do FrmBalancete do Contabil2020): saldo anterior vindo do
    /// PTPLA&lt;ano-1&gt; (âncora), movimento do período somado do MOVFIN, e saldo atual =
    /// anterior + débito − crédito. Permite analítico (todas as contas com movimento) ou
    /// sintético (só os grupos, com rollup pela máscara 1.1.1.2.3). Usa o EngineSaldo validado.
    /// </summary>
    public sealed class FrmBalancete : Form
    {
        private TextBox txtPasta, txtConta;
        private DateTimePicker dtDe, dtAte;
        private CheckBox chkSintetico, chkFechamento;
        private Button btnPasta, btnCalcular, btnExcel, btnNovas;
        private Label lblAncora, lblResumo;
        private DataGridView dgv;
        private StatusStrip status;
        private ToolStripStatusLabel statusLabel;

        public FrmBalancete()
        {
            Text = "Balancete — Contabilidade";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(940, 560);
            Size = new Size(1060, 660);
            MontarUI();
            txtPasta.Text = ConfigApp.CarregarPastaDados();
        }

        private void MontarUI()
        {
            var topo = new Panel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(8) };
            var lblPasta = new Label { Text = "Pasta de dados:", AutoSize = true, Location = new Point(10, 12) };
            txtPasta = new TextBox { Location = new Point(120, 8), Width = 700 };
            btnPasta = new Button { Text = "...", Location = new Point(826, 7), Width = 34 };
            btnPasta.Click += (s, e) => EscolherPasta();

            var hoje = DateTime.Today;
            var ini = new DateTime(hoje.Year, 1, 1);
            var lblP = new Label { Text = "Período:", AutoSize = true, Location = new Point(10, 46) };
            dtDe = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 110, Location = new Point(120, 42), Value = ini };
            var lblAte = new Label { Text = "a", AutoSize = true, Location = new Point(238, 46) };
            dtAte = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 110, Location = new Point(258, 42), Value = hoje };
            dtAte.ValueChanged += (s, e) => AtualizaAncora();
            chkSintetico = new CheckBox { Text = "Sintético (só grupos)", AutoSize = true, Location = new Point(390, 45) };
            chkFechamento = new CheckBox { Text = "Excluir fechamento (SIST_BAL)", AutoSize = true, Location = new Point(540, 45) };
            btnCalcular = new Button { Text = "Calcular", Location = new Point(826, 41), Width = 100 };
            btnCalcular.Click += (s, e) => Calcular();

            var lblCt = new Label { Text = "Conta (prefixo):", AutoSize = true, Location = new Point(10, 72) };
            txtConta = new TextBox { Location = new Point(120, 68), Width = 110 };
            txtConta.TextChanged += (s, e) => { if (_apuracao != null) Exibir(); };
            lblAncora = new Label { AutoSize = true, Location = new Point(258, 72), ForeColor = Color.DimGray };

            // posição fixa à esquerda (sem Anchor=Right, que com Dock joga o botão pra fora da área visível)
            btnNovas = new Button { Text = "Contas novas no PTPLA…", Location = new Point(470, 66), Width = 180, Enabled = false };
            btnNovas.Click += (s, e) => ConferirContasNovas();
            btnExcel = new Button { Text = "Exportar Excel…", Location = new Point(656, 66), Width = 130, Enabled = false };
            btnExcel.Click += (s, e) => ExportarExcel();

            topo.Controls.AddRange(new Control[] { lblPasta, txtPasta, btnPasta, lblP, dtDe, lblAte, dtAte, chkSintetico, chkFechamento, btnCalcular, lblCt, txtConta, lblAncora, btnNovas, btnExcel });
            chkSintetico.CheckedChanged += (s, e) => { if (_apuracao != null) Exibir(); };

            dgv = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoGenerateColumns = true,
            };

            lblResumo = new Label { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), Font = new Font(Font, FontStyle.Bold) };
            status = new StatusStrip(); statusLabel = new ToolStripStatusLabel("Informe a pasta e clique Calcular."); status.Items.Add(statusLabel);

            Controls.Add(dgv);
            Controls.Add(lblResumo);
            Controls.Add(topo);
            Controls.Add(status);
            AtualizaAncora();
        }

        private string AnoAnterior() => (dtAte.Value.Year - 1).ToString();
        private void AtualizaAncora() => lblAncora.Text = $"Saldos iniciais de: PTPLA{AnoAnterior()}.DBF";

        private void EscolherPasta()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pasta com MOVFIN.DBF, placon.DBF e PTPLA<ano>.DBF" })
            {
                if (Directory.Exists(txtPasta.Text)) dlg.SelectedPath = txtPasta.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtPasta.Text = dlg.SelectedPath;
            }
        }

        // estado calculado (chave = NUMCONTA)
        private Dictionary<string, EngineSaldo.Apuracao> _apuracao;
        private PlanoContas _plano;

        private void Calcular()
        {
            var pasta = txtPasta.Text.Trim();
            var placon = Path.Combine(pasta, "placon.DBF");
            var movfin = Path.Combine(pasta, "MOVFIN.DBF");
            var ptpla = Path.Combine(pasta, $"PTPLA{AnoAnterior()}.DBF");
            if (!File.Exists(placon) || !File.Exists(movfin)) { Aviso("A pasta precisa conter placon.DBF e MOVFIN.DBF."); return; }
            if (!File.Exists(ptpla)) { Aviso($"Não encontrei o PTPLA{AnoAnterior()}.DBF (saldos iniciais do exercício anterior) na pasta."); return; }

            try
            {
                UseWaitCursor = true;
                statusLabel.Text = "Calculando…";
                Application.DoEvents();
                _plano = PlanoContas.Carregar(placon, ptpla);   // estrutura+apelidos do placon; saldos do PTPLA<ano-1>
                var engine = new EngineSaldo(_plano);
                Func<string, string, bool> excluir = chkFechamento.Checked ? (doc, data) => doc == "SIST_BAL" : (Func<string, string, bool>)null;
                _apuracao = engine.ApurarPeriodoComRollup(movfin, dtDe.Value.ToString("yyyyMMdd"), dtAte.Value.ToString("yyyyMMdd"), excluir);
                Exibir();
                btnExcel.Enabled = btnNovas.Enabled = true;
                int novasMov = NovasComMovimento().Count;
                statusLabel.Text = $"Balancete de {dtDe.Value:dd/MM/yyyy} a {dtAte.Value:dd/MM/yyyy} (saldos iniciais de PTPLA{AnoAnterior()})."
                    + (novasMov > 0 ? $"  ⚠ {novasMov} conta(s) nova(s) com movimento fora do PTPLA — use \"Contas novas no PTPLA…\"." : "");
            }
            catch (Exception ex) { Aviso("Erro ao calcular:\n" + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        private sealed class LinhaBal
        {
            public string Conta { get; set; }
            public string Descricao { get; set; }
            public decimal SaldoAnterior { get; set; }
            public decimal Debito { get; set; }
            public decimal Credito { get; set; }
            public decimal SaldoAtual { get; set; }
            public int Nivel { get; set; }
        }

        /// <summary>Monta as linhas do balancete aplicando o filtro (sintético/analítico + prefixo).</summary>
        private List<LinhaBal> MontarLinhas()
        {
            bool sint = chkSintetico.Checked;
            var pref = txtConta.Text.Trim();
            bool Mostra(string nc, EngineSaldo.Apuracao a)
            {
                if (pref.Length > 0 && !nc.StartsWith(pref, StringComparison.Ordinal)) return false;
                bool temMov = a.Val1 != 0 || a.Val2 != 0 || a.Val3 != 0 || a.SaldoFinal != 0;
                if (!temMov) return false;
                return sint ? !HierarquiaContas.EhAnalitica(nc) : true;   // sintético = só grupos; analítico = tudo
            }
            return _plano.Contas.Keys
                .Where(nc => _apuracao.ContainsKey(nc) && Mostra(nc, _apuracao[nc]))
                .OrderBy(nc => nc, StringComparer.Ordinal)
                .Select(nc =>
                {
                    var a = _apuracao[nc];
                    int nivel = HierarquiaContas.Nivel(nc);
                    _plano.Contas.TryGetValue(nc, out var c);
                    return new LinhaBal
                    {
                        Conta = nc,
                        Descricao = new string(' ', Math.Max(0, nivel - 1) * 2) + (c?.Descricao ?? ""),
                        SaldoAnterior = decimal.Round(a.Val1, 2),
                        Debito = decimal.Round(a.Val2, 2),
                        Credito = decimal.Round(a.Val3, 2),
                        SaldoAtual = decimal.Round(a.SaldoFinal, 2),
                        Nivel = nivel,
                    };
                }).ToList();
        }

        private void Exibir()
        {
            var linhas = MontarLinhas();
            dgv.DataSource = linhas;
            if (dgv.Columns.Contains("Nivel")) dgv.Columns["Nivel"].Visible = false;
            void Col(string n, string t, int w, bool dir)
            {
                if (!dgv.Columns.Contains(n)) return;
                dgv.Columns[n].HeaderText = t; dgv.Columns[n].FillWeight = w;
                if (dir) { dgv.Columns[n].DefaultCellStyle.Format = "N2"; dgv.Columns[n].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
            }
            Col("Conta", "Conta", 14, false);
            Col("Descricao", "Descrição", 44, false);
            Col("SaldoAnterior", "Saldo Anterior", 16, true);
            Col("Debito", "Débito", 14, true);
            Col("Credito", "Crédito", 14, true);
            Col("SaldoAtual", "Saldo Atual", 16, true);

            // realça as contas sintéticas (grupos) em negrito
            foreach (DataGridViewRow row in dgv.Rows)
            {
                var item = row.DataBoundItem;
                var nv = item?.GetType().GetProperty("Nivel")?.GetValue(item);
                if (nv != null && Convert.ToInt32(nv) < 5)
                    row.DefaultCellStyle.Font = new Font(dgv.Font, FontStyle.Bold);
            }

            // totais a partir das ANALÍTICAS (evita dupla contagem das sintéticas do rollup)
            decimal totDeb = 0, totCred = 0;
            foreach (var kv in _apuracao)
                if (HierarquiaContas.EhAnalitica(kv.Key)) { totDeb += kv.Value.Val2; totCred += kv.Value.Val3; }
            var dif = decimal.Round(totDeb - totCred, 2);
            lblResumo.Text = $"{linhas.Count} contas exibidas | Débitos R$ {totDeb:N2}   Créditos R$ {totCred:N2}   "
                + (dif == 0 ? "(confere ✓)" : $"(diferença R$ {dif:N2})");
            lblResumo.ForeColor = dif == 0 ? Color.Green : Color.Firebrick;
        }

        /// <summary>Contas NOVAS (no placon, fora do PTPLA âncora) que têm movimento no período.</summary>
        private List<string> NovasComMovimento()
        {
            var res = new List<string>();
            if (_plano == null || _apuracao == null) return res;
            foreach (var nc in _plano.ContasForaDaAncora)
                if (_apuracao.TryGetValue(nc, out var a) && (a.Val2 != 0 || a.Val3 != 0))
                    res.Add(nc);
            res.Sort(StringComparer.Ordinal);
            return res;
        }

        /// <summary>Oferece acrescentar no PTPLA&lt;ano-1&gt; as contas novas com movimento (como o Contabil2020).</summary>
        private void ConferirContasNovas()
        {
            if (_plano == null) return;
            var novas = NovasComMovimento();
            if (novas.Count == 0) { Aviso($"Não há contas novas com movimento no período fora do PTPLA{AnoAnterior()}."); return; }

            var amostra = string.Join("\n", novas.Take(15).Select(nc =>
            { _plano.Contas.TryGetValue(nc, out var c); return $"   {nc}  {c?.Descricao}"; }));
            if (novas.Count > 15) amostra += $"\n   …e mais {novas.Count - 15}.";
            if (MessageBox.Show(this,
                    $"{novas.Count} conta(s) nova(s) com movimento NÃO estão no PTPLA{AnoAnterior()}.DBF:\n\n{amostra}\n\n" +
                    $"Acrescentar essas contas no PTPLA{AnoAnterior()} (saldo inicial = 0)?",
                    "Contas novas no PTPLA", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                UseWaitCursor = true;
                var grav = new PtplaGravador(txtPasta.Text.Trim(), dtAte.Value.Year - 1);
                int n = 0;
                foreach (var nc in novas)
                {
                    _plano.Contas.TryGetValue(nc, out var c);
                    if (grav.InserirConta(nc, HierarquiaContas.GrauDerivado(nc), c?.Descricao, c?.Desc2, c?.DataAncora)) n++;
                }
                MessageBox.Show(this, $"{n} conta(s) acrescentada(s) no PTPLA{AnoAnterior()}.DBF (SDO=0).",
                    "Concluído", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Calcular();   // recalcula: agora as contas estão na âncora
            }
            catch (Exception ex) { Aviso("Erro ao gravar no PTPLA:\n" + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        /// <summary>Exporta o balancete exibido para .xlsx (EPPlus), igual ao Contabil2020.</summary>
        private void ExportarExcel()
        {
            if (_apuracao == null) return;
            var linhas = MontarLinhas();
            if (linhas.Count == 0) { Aviso("Nada para exportar."); return; }
            using (var dlg = new SaveFileDialog
            {
                DefaultExt = "xlsx", Filter = "Excel (*.xlsx)|*.xlsx", AddExtension = true,
                FileName = $"Balancete_{dtDe.Value:yyyyMMdd}_{dtAte.Value:yyyyMMdd}.xlsx"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    UseWaitCursor = true;
                    GravarExcel(dlg.FileName, linhas);
                    statusLabel.Text = $"Exportado: {dlg.FileName}";
                    if (MessageBox.Show(this, "Planilha gerada. Abrir agora?", "Excel",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                        System.Diagnostics.Process.Start(dlg.FileName);
                }
                catch (Exception ex) { Aviso("Erro ao exportar:\n" + ex.Message); }
                finally { UseWaitCursor = false; }
            }
        }

        /// <summary>Escreve o balancete num .xlsx via EPPlus (separado p/ poder testar headless).</summary>
        private void GravarExcel(string caminho, List<LinhaBal> linhas)
        {
            var fi = new FileInfo(caminho);
            if (fi.Exists) fi.Delete();
            using (var pkg = new ExcelPackage(fi))
            {
                var ws = pkg.Workbook.Worksheets.Add("Balancete");
                ws.Cells[1, 1].Value = $"Balancete — {dtDe.Value:dd/MM/yyyy} a {dtAte.Value:dd/MM/yyyy} (saldos iniciais PTPLA{AnoAnterior()})";
                ws.Cells[1, 1].Style.Font.Bold = true;
                string[] hd = { "Conta", "Descrição", "Saldo Anterior", "Débito", "Crédito", "Saldo Atual" };
                for (int i = 0; i < hd.Length; i++) { ws.Cells[3, i + 1].Value = hd[i]; ws.Cells[3, i + 1].Style.Font.Bold = true; }

                int row = 4;
                foreach (var l in linhas)
                {
                    ws.Cells[row, 1].Value = l.Conta;
                    ws.Cells[row, 2].Value = l.Descricao;
                    Num(ws, row, 3, l.SaldoAnterior); Num(ws, row, 4, l.Debito); Num(ws, row, 5, l.Credito); Num(ws, row, 6, l.SaldoAtual);
                    if (l.Nivel < 5) for (int c = 1; c <= 6; c++) ws.Cells[row, c].Style.Font.Bold = true;
                    row++;
                }
                decimal td = 0, tc = 0;
                foreach (var kv in _apuracao) if (HierarquiaContas.EhAnalitica(kv.Key)) { td += kv.Value.Val2; tc += kv.Value.Val3; }
                ws.Cells[row + 1, 2].Value = "TOTAIS"; ws.Cells[row + 1, 2].Style.Font.Bold = true;
                Num(ws, row + 1, 4, decimal.Round(td, 2)); Num(ws, row + 1, 5, decimal.Round(tc, 2));
                ws.Cells[row + 1, 4].Style.Font.Bold = ws.Cells[row + 1, 5].Style.Font.Bold = true;

                ws.Column(1).Width = 12; ws.Column(2).Width = 46; ws.Column(3).Width = 16;
                ws.Column(4).Width = 15; ws.Column(5).Width = 15; ws.Column(6).Width = 16;
                pkg.Save();
            }
        }

        private static void Num(ExcelWorksheet ws, int r, int c, decimal v)
        {
            ws.Cells[r, c].Value = v;
            ws.Cells[r, c].Style.Numberformat.Format = "#,##0.00";
            ws.Cells[r, c].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "Balancete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
