using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Contabil.Core;
using Imobilizado.Dados;

namespace Imobilizado.App
{
    /// <summary>
    /// Absorção de custo de produção (porte do Absorcao_Custo do Clipper / FLT_ABS.PRG):
    /// usa o RELAC.DBF (par débito-ativo × crédito-custo-apropriado + % QUANT1) e o custo
    /// líquido (débitos−créditos) de cada GRUPO de custo no período-base, e gera os
    /// lançamentos D=estoque / C=custo-apropriado que zeram os grupos (DOC=SIST_ABSOR,
    /// HIST="DEB/CRE ABSORCAO CUSTO PRODUCAO", MOV_ID=0 como no Clipper).
    /// </summary>
    public sealed class FrmAbsorcao : Form
    {
        private TextBox txtPasta, txtPrefDeb, txtPrefCred;
        private DateTimePicker dtLanc, dtDe;
        private Button btnPasta, btnCalcular, btnGravar;
        private CheckBox chkSubstituir;
        private DataGridView dgv;
        private Label lblResumo;
        private StatusStrip status;
        private ToolStripStatusLabel statusLabel;

        private List<MotorAbsorcao.ItemAbsorcao> _itens;
        private PlanoContas _plano;

        public FrmAbsorcao()
        {
            Text = "Absorção de Custo de Produção — SIST_ABSOR";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(960, 520);
            Size = new Size(1060, 600);
            MontarUI();
            txtPasta.Text = ConfigApp.CarregarPastaDados();
        }

        private void MontarUI()
        {
            var topo = new Panel { Dock = DockStyle.Top, Height = 104, Padding = new Padding(8) };
            var lblPasta = new Label { Text = "Pasta de dados:", AutoSize = true, Location = new Point(10, 12) };
            txtPasta = new TextBox { Location = new Point(130, 8), Width = 700 };
            btnPasta = new Button { Text = "...", Location = new Point(836, 7), Width = 34 };
            btnPasta.Click += (s, e) => EscolherPasta();

            // data do lançamento = fim do mês de absorção; período-base = 01/01 até a data (como o
            // balancete anual do Clipper que alimentava o VAL2/VAL3 do PLACON)
            var fimMesAnterior = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddDays(-1);
            var lblL = new Label { Text = "Data do lançamento:", AutoSize = true, Location = new Point(10, 46) };
            dtLanc = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 110, Location = new Point(130, 42), Value = fimMesAnterior };
            dtLanc.ValueChanged += (s, e) => dtDe.Value = new DateTime(dtLanc.Value.Year, 1, 1);
            var lblP = new Label { Text = "Período-base de:", AutoSize = true, Location = new Point(260, 46) };
            dtDe = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 110, Location = new Point(365, 42), Value = new DateTime(fimMesAnterior.Year, 1, 1) };
            var lblAte = new Label { Text = "até a data do lançamento", AutoSize = true, Location = new Point(482, 46), ForeColor = Color.DimGray };

            var lblD = new Label { Text = "Conta ativo (prefixo):", AutoSize = true, Location = new Point(10, 76) };
            txtPrefDeb = new TextBox { Location = new Point(130, 72), Width = 80, Text = "1" };
            var lblC = new Label { Text = "Conta resultado (prefixo):", AutoSize = true, Location = new Point(230, 76) };
            txtPrefCred = new TextBox { Location = new Point(382, 72), Width = 80, Text = "3" };
            btnCalcular = new Button { Text = "Calcular", Location = new Point(490, 70), Width = 110 };
            btnCalcular.Click += (s, e) => Calcular();
            chkSubstituir = new CheckBox { Text = "Substituir SIST_ABSOR já existente na data", AutoSize = true, Location = new Point(620, 74) };

            topo.Controls.AddRange(new Control[] { lblPasta, txtPasta, btnPasta, lblL, dtLanc, lblP, dtDe, lblAte, lblD, txtPrefDeb, lblC, txtPrefCred, btnCalcular, chkSubstituir });

            dgv = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoGenerateColumns = true,
            };
            GridOrdena.Aplicar(dgv);

            var rodape = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Padding = new Padding(8, 9, 8, 9),
            };
            btnGravar = new Button { Text = "Gravar no MOVFIN", Size = new Size(160, 30), Enabled = false };
            btnGravar.Click += (s, e) => Gravar();
            lblResumo = new Label { AutoSize = false, Size = new Size(640, 30), TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) };
            rodape.Controls.Add(btnGravar);
            rodape.Controls.Add(lblResumo);

            status = new StatusStrip(); statusLabel = new ToolStripStatusLabel("Informe a pasta (precisa de RELAC.DBF) e clique Calcular."); status.Items.Add(statusLabel);

            Controls.Add(dgv);
            Controls.Add(rodape);
            Controls.Add(topo);
            Controls.Add(status);
        }

        private void EscolherPasta()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pasta com RELAC.DBF, placon.DBF, PTPLA<ano-1> e MOVFIN.DBF" })
            {
                if (Directory.Exists(txtPasta.Text)) dlg.SelectedPath = txtPasta.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtPasta.Text = dlg.SelectedPath;
            }
        }

        private void Calcular()
        {
            var pasta = txtPasta.Text.Trim();
            var relacPath = Path.Combine(pasta, "RELAC.DBF");
            var placon = Path.Combine(pasta, "placon.DBF");
            var movfin = Path.Combine(pasta, "MOVFIN.DBF");
            int ano = dtLanc.Value.Year;
            var ptpla = Path.Combine(pasta, $"PTPLA{ano - 1}.DBF");
            if (!File.Exists(relacPath)) { Aviso("Não encontrei o RELAC.DBF na pasta (tabela débito×crédito da absorção)."); return; }
            if (!File.Exists(placon) || !File.Exists(movfin)) { Aviso("A pasta precisa conter placon.DBF e MOVFIN.DBF."); return; }
            if (!File.Exists(ptpla)) { Aviso($"Não encontrei o PTPLA{ano - 1}.DBF (âncora dos saldos)."); return; }

            try
            {
                UseWaitCursor = true;
                statusLabel.Text = "Calculando…";
                Application.DoEvents();

                _plano = PlanoContas.Carregar(placon, ptpla);
                var eng = new EngineSaldo(_plano);
                string d1 = dtDe.Value.ToString("yyyyMMdd"), d2 = dtLanc.Value.ToString("yyyyMMdd");
                // exclui o SIST_ABSOR da PRÓPRIA data (recálculo); absorções de meses anteriores ficam
                Func<string, string, bool> excl = (doc, data) => doc == "SIST_ABSOR" && data == d2;
                var ap = eng.ApurarPeriodoComRollup(movfin, d1, d2, excl);

                var relac = LinhaRelac.Carregar(relacPath, "A");   // só FUNCAO='A' (absorção)
                _itens = MotorAbsorcao.Gerar(relac,
                    g => ap.TryGetValue(g, out var a) ? a.Val2 - a.Val3 : 0m,
                    txtPrefDeb.Text, txtPrefCred.Text);

                string DescD2(string nc) => _plano.Contas.TryGetValue(nc, out var c) ? c.Descricao.Trim() : "?";
                dgv.DataSource = _itens.Select(i => new
                {
                    Debito = i.Debito, DescDebito = DescD2(i.Debito),
                    Credito = i.Credito, DescCredito = DescD2(i.Credito),
                    Perc = i.Quant1, Valor = i.Valor,
                }).ToList();
                void Col(string n, string t, int w, bool dir = false, string fmt = null)
                {
                    if (!dgv.Columns.Contains(n)) return;
                    dgv.Columns[n].HeaderText = t; dgv.Columns[n].FillWeight = w;
                    if (fmt != null) dgv.Columns[n].DefaultCellStyle.Format = fmt;
                    if (dir) dgv.Columns[n].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
                Col("Debito", "Débito", 12); Col("DescDebito", "Descrição débito", 26);
                Col("Credito", "Crédito", 12); Col("DescCredito", "Descrição crédito", 26);
                Col("Perc", "%", 8, true, "N2"); Col("Valor", "Valor", 16, true, "N2");

                int jaExistem = new MovfinGravador(pasta).ContarPorDocData("SIST_ABSOR", d2);
                decimal total = _itens.Sum(i => i.Valor);
                lblResumo.Text = $"{_itens.Count} lançamentos — total R$ {total:N2}"
                    + (jaExistem > 0 ? $"   ⚠ já existem {jaExistem} SIST_ABSOR em {dtLanc.Value:dd/MM/yyyy}" : "");
                lblResumo.ForeColor = jaExistem > 0 ? Color.Firebrick : Color.Green;
                btnGravar.Enabled = _itens.Count > 0;
                statusLabel.Text = $"Pré-visualização (nada gravado). Período-base {dtDe.Value:dd/MM/yyyy} a {dtLanc.Value:dd/MM/yyyy}.";
            }
            catch (Exception ex) { Aviso("Erro ao calcular:\n" + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        private void Gravar()
        {
            if (_itens == null || _itens.Count == 0) return;
            var pasta = txtPasta.Text.Trim();
            var d2 = dtLanc.Value.ToString("yyyyMMdd");
            var g = new MovfinGravador(pasta);
            int jaExistem = g.ContarPorDocData("SIST_ABSOR", d2);
            if (jaExistem > 0 && !chkSubstituir.Checked)
            {
                Aviso($"Já existem {jaExistem} lançamentos SIST_ABSOR em {dtLanc.Value:dd/MM/yyyy}.\nMarque \"Substituir\" para reprocessar.");
                return;
            }
            decimal total = _itens.Sum(i => i.Valor);
            var msg = $"Gravar {_itens.Count} lançamentos de ABSORÇÃO em {dtLanc.Value:dd/MM/yyyy} (total R$ {total:N2}) no MOVFIN?\n\nPasta: {pasta}"
                + (jaExistem > 0 ? $"\n\nOs {jaExistem} SIST_ABSOR existentes da data serão EXCLUÍDOS antes." : "");
            if (MessageBox.Show(this, msg, "Confirmar gravação", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                UseWaitCursor = true;
                if (jaExistem > 0) g.ExcluirPorDocData("SIST_ABSOR", d2);
                var lancs = _itens.Select(i => new LancamentoMovfin
                {
                    Data = d2, Debito = i.Debito, Credito = i.Credito, Valor = i.Valor,
                    Historico = "DEB/CRE ABSORCAO CUSTO PRODUCAO", Doc = "SIST_ABSOR",
                }).ToList();
                int n = g.InserirComMovIdZero(lancs);
                statusLabel.Text = $"Gravados {n} lançamentos SIST_ABSOR em {dtLanc.Value:dd/MM/yyyy}.";
                MessageBox.Show(this, $"Gravados {n} lançamentos no MOVFIN" + (jaExistem > 0 ? $" (após excluir {jaExistem})." : "."),
                    "Concluído", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { Aviso("Erro ao gravar:\n" + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "Absorção", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
