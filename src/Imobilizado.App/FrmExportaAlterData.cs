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
    /// Prepara e exporta o LOTE de importação do AlterData a partir do MOVFIN pareado
    /// (rebuild enxuto do FrmRazao+FrmRelaciona do Contabil2020). Lê o período, pareia
    /// compostos (OUTRO_ID) e transferências bancárias, traduz cada conta para o código
    /// REDUZIDO via RELACIONA e gera o .xlsx no layout de 10 colunas do AlterData.
    ///
    /// Contas sem correspondência em RELACIONA viram "-1" e meias-entradas que sobraram
    /// (não pareadas) são marcadas "a conferir"; o export BLOQUEIA quando há pendências
    /// (com opção de exportar mesmo assim), igual ao btnUnicas + bloqueio do Contabil2020.
    /// </summary>
    public sealed class FrmExportaAlterData : Form
    {
        private TextBox txtPasta;
        private DateTimePicker dtDe, dtAte;
        private RadioButton rbReduzido, rbNovoCod, rbNumConta;
        private CheckBox chkFechamento, chkPreparados;
        private Button btnPasta, btnPreparar, btnConferir, btnSolteiras, btnExportar;
        private List<ExportadorAlterData.ContaSolteira> _solteiras;
        private Label lblResumo;
        private DataGridView dgv;
        private StatusStrip status;
        private ToolStripStatusLabel statusLabel;

        private List<LinhaAlterData> _linhas;
        private int _excluidosFlt;

        public FrmExportaAlterData()
        {
            Text = "Exportar Lote — AlterData";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(960, 560);
            Size = new Size(1080, 660);
            MontarUI();
            txtPasta.Text = ConfigApp.CarregarPastaDados();
        }

        private void MontarUI()
        {
            var topo = new Panel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(8) };
            var lblPasta = new Label { Text = "Pasta de dados:", AutoSize = true, Location = new Point(10, 12) };
            txtPasta = new TextBox { Location = new Point(120, 8), Width = 720 };
            btnPasta = new Button { Text = "...", Location = new Point(846, 7), Width = 34 };
            btnPasta.Click += (s, e) => EscolherPasta();

            var hoje = DateTime.Today;
            var ini = new DateTime(hoje.Year, hoje.Month, 1);
            var lblP = new Label { Text = "Período:", AutoSize = true, Location = new Point(10, 46) };
            dtDe = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 110, Location = new Point(120, 42), Value = ini };
            var lblAte = new Label { Text = "a", AutoSize = true, Location = new Point(238, 46) };
            dtAte = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 110, Location = new Point(258, 42), Value = hoje };
            chkFechamento = new CheckBox { Text = "Excluir fechamento (SIST_BAL)", AutoSize = true, Location = new Point(390, 45) };
            chkPreparados = new CheckBox { Text = "Só preparados p/ contabilidade (FLT_1)", AutoSize = true, Location = new Point(580, 45), Checked = true };

            btnPreparar = new Button { Text = "Preparar", Location = new Point(846, 41), Width = 100 };
            btnPreparar.Click += (s, e) => Preparar();

            var lblModo = new Label { Text = "Código:", AutoSize = true, Location = new Point(10, 72) };
            rbReduzido = new RadioButton { Text = "REDUZIDO (padrão)", AutoSize = true, Location = new Point(120, 70), Checked = true };
            rbNovoCod = new RadioButton { Text = "NOVOCOD", AutoSize = true, Location = new Point(270, 70) };
            rbNumConta = new RadioButton { Text = "NUMCONTA (debug)", AutoSize = true, Location = new Point(370, 70) };

            btnConferir = new Button { Text = "Linhas a conferir…", Location = new Point(516, 66), Width = 130, Enabled = false };
            btnConferir.Click += (s, e) => Conferir();
            // habilita SÓ quando há contas sem correspondência no RELACIONA (= btnUnicas do Contabil2020)
            btnSolteiras = new Button { Text = "Contas solteiras…", Location = new Point(652, 66), Width = 130, Enabled = false };
            btnSolteiras.Click += (s, e) => MostrarSolteiras();
            btnExportar = new Button { Text = "Exportar lote…", Location = new Point(788, 66), Width = 130, Enabled = false };
            btnExportar.Click += (s, e) => Exportar();

            topo.Controls.AddRange(new Control[] { lblPasta, txtPasta, btnPasta, lblP, dtDe, lblAte, dtAte,
                chkFechamento, chkPreparados, btnPreparar, lblModo, rbReduzido, rbNovoCod, rbNumConta, btnConferir, btnSolteiras, btnExportar });

            dgv = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoGenerateColumns = true,
            };
            GridOrdena.Aplicar(dgv);   // ordenar clicando no cabeçalho

            lblResumo = new Label { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), Font = new Font(Font, FontStyle.Bold) };
            status = new StatusStrip(); statusLabel = new ToolStripStatusLabel("Informe a pasta e clique Preparar."); status.Items.Add(statusLabel);

            Controls.Add(dgv);
            Controls.Add(lblResumo);
            Controls.Add(topo);
            Controls.Add(status);
        }

        private ModoExportAlterData Modo()
        {
            if (rbNovoCod.Checked) return ModoExportAlterData.NovoCod;
            if (rbNumConta.Checked) return ModoExportAlterData.NumConta;
            return ModoExportAlterData.Reduzido;
        }

        private void EscolherPasta()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pasta com MOVFIN.DBF, placon.DBF, BANCOS.DBF e RELACIONA.DBF" })
            {
                if (Directory.Exists(txtPasta.Text)) dlg.SelectedPath = txtPasta.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtPasta.Text = dlg.SelectedPath;
            }
        }

        private void Preparar()
        {
            var pasta = txtPasta.Text.Trim();
            var placon = Path.Combine(pasta, "placon.DBF");
            var movfin = Path.Combine(pasta, "MOVFIN.DBF");
            var relac = Path.Combine(pasta, "RELACIONA.DBF");
            if (!File.Exists(placon) || !File.Exists(movfin)) { Aviso("A pasta precisa conter placon.DBF e MOVFIN.DBF."); return; }
            if (!File.Exists(relac)) { Aviso("Não encontrei o RELACIONA.DBF (de-para NUMCONTA → REDUZIDO) na pasta."); return; }

            try
            {
                UseWaitCursor = true;
                statusLabel.Text = "Preparando o lote…";
                Application.DoEvents();

                var plano = PlanoContas.Carregar(placon);
                var relMap = new RelacionaReader(pasta).Carregar(out var dups);
                if (dups.Count > 0)
                    Aviso($"RELACIONA.DBF tem {dups.Count} NUMCONTA duplicado(s) — só a 1ª ocorrência é usada. Ex.: {string.Join(", ", dups.Take(6))}");

                var lanc = new MovfinGravador(pasta).LerPeriodo(dtDe.Value.ToString("yyyyMMdd"), dtAte.Value.ToString("yyyyMMdd"), null);
                if (chkFechamento.Checked)
                    lanc = lanc.Where(l => (l.Doc ?? "").Trim() != "SIST_BAL").ToList();

                var exp = new ExportadorAlterData(plano, relMap);

                // FLT_1: descarta os registros NÃO preparados para a contabilidade ('*', sem casar DESC2, banco sem CONTAB)
                _excluidosFlt = 0;
                if (chkPreparados.Checked)
                {
                    lanc = exp.FiltrarPreparados(lanc, out var excluidos);
                    _excluidosFlt = excluidos.Count;
                }

                // pareamento: composto (OUTRO_ID) → transferências banco → folha SIST_RURAL
                // (pós-corte a folha já nasce pareada na origem, então ParearFolha vira no-op)
                // pareamento: composto (OUTRO_ID) → transferências banco → folha SIST_RURAL → nota fiscal (DOC_FISC)
                var pareado = exp.ParearNotasFiscais(exp.ParearFolha(exp.ParearTransferencias(exp.ParearCompostos(lanc))));
                _linhas = exp.MontarLinhas(pareado, Modo());
                _solteiras = exp.ContasSolteiras(pareado);

                Exibir();
                btnConferir.Enabled = true;
                btnSolteiras.Enabled = _solteiras.Count > 0;
                btnSolteiras.Text = _solteiras.Count > 0 ? $"Contas solteiras ({_solteiras.Count})…" : "Contas solteiras…";
                btnExportar.Enabled = _linhas.Count > 0;
            }
            catch (Exception ex) { Aviso("Erro ao preparar:\n" + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        private sealed class LinhaView
        {
            public string Conferir { get; set; }
            public string Debito { get; set; }
            public string Credito { get; set; }
            public DateTime Data { get; set; }
            public decimal Valor { get; set; }
            public string Historico { get; set; }
            public string NrDocumento { get; set; }
            public bool Pendente { get; set; }
        }

        private void Exibir()
        {
            var view = _linhas.Select(l => new LinhaView
            {
                Conferir = (l.SemMapeamento || l.MeiaEntrada) ? "⚠" : "",
                Debito = l.Debito,
                Credito = l.Credito,
                Data = l.Data,
                Valor = l.Valor,
                Historico = l.Historico,
                NrDocumento = l.NrDocumento,
                Pendente = l.SemMapeamento || l.MeiaEntrada,
            }).ToList();

            dgv.DataSource = view;
            if (dgv.Columns.Contains("Pendente")) dgv.Columns["Pendente"].Visible = false;
            void Col(string n, string t, int w, bool dir)
            {
                if (!dgv.Columns.Contains(n)) return;
                dgv.Columns[n].HeaderText = t; dgv.Columns[n].FillWeight = w;
                if (dir) dgv.Columns[n].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            Col("Conferir", "", 4, false);
            Col("Debito", "Débito", 10, true);
            Col("Credito", "Crédito", 10, true);
            Col("Data", "Data", 12, false);
            Col("Valor", "Valor", 14, true);
            Col("Historico", "Complemento histórico", 40, false);
            Col("NrDocumento", "NrDocumento", 16, false);
            if (dgv.Columns.Contains("Data")) dgv.Columns["Data"].DefaultCellStyle.Format = "dd/MM/yyyy";
            if (dgv.Columns.Contains("Valor")) dgv.Columns["Valor"].DefaultCellStyle.Format = "N2";

            foreach (DataGridViewRow row in dgv.Rows)
            {
                var item = row.DataBoundItem as LinhaView;
                if (item != null && item.Pendente) row.DefaultCellStyle.BackColor = Color.MistyRose;
            }

            int pend = _linhas.Count(l => l.SemMapeamento || l.MeiaEntrada);
            int semMap = _linhas.Count(l => l.SemMapeamento);
            int meia = _linhas.Count(l => l.MeiaEntrada && !l.SemMapeamento);
            decimal soma = _linhas.Sum(l => l.Valor);
            string fltTxt = _excluidosFlt > 0 ? $"{_excluidosFlt} não-preparados excluídos (FLT_1) | " : "";
            lblResumo.Text = $"{_linhas.Count} linhas | {fltTxt}Soma R$ {soma:N2} | " +
                (pend == 0 ? "tudo OK ✓" : $"⚠ {pend} a conferir (sem mapeamento: {semMap}, meia-entrada: {meia})");
            lblResumo.ForeColor = pend == 0 ? Color.Green : Color.Firebrick;
            statusLabel.Text = $"Lote de {dtDe.Value:dd/MM/yyyy} a {dtAte.Value:dd/MM/yyyy} preparado.";
        }

        /// <summary>Lista as linhas pendentes (sem mapeamento ou meia-entrada) para o usuário conferir.</summary>
        private void Conferir()
        {
            if (_linhas == null) return;
            var pend = _linhas.Where(l => l.SemMapeamento || l.MeiaEntrada).ToList();
            if (pend.Count == 0) { Aviso("Nenhuma pendência: todas as linhas têm débito e crédito mapeados."); return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{pend.Count} linha(s) a conferir antes de exportar:\n");
            foreach (var l in pend.Take(40))
            {
                string motivo = l.SemMapeamento ? "sem REDUZIDO em RELACIONA" : "meia-entrada (lado vazio — não pareada)";
                sb.AppendLine($"  rec {l.Recno}  {l.Data:dd/MM/yyyy}  D=[{l.Debito}] C=[{l.Credito}]  R$ {l.Valor:N2}  {l.NrDocumento}  → {motivo}");
            }
            if (pend.Count > 40) sb.AppendLine($"  …e mais {pend.Count - 40}.");
            MessageBox.Show(this, sb.ToString(), "Contas a conferir", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// Mostra as CONTAS SOLTEIRAS (sem correspondência no RELACIONA) numa janela com grid —
        /// equivale ao btnUnicas do FrmRelaciona do Contabil2020. Corrija via planilha PESQUISA
        /// (AlterData → Importar RELACIONA) e clique Preparar de novo.
        /// </summary>
        private void MostrarSolteiras()
        {
            if (_solteiras == null || _solteiras.Count == 0) return;
            var f = new Form
            {
                Text = $"Contas solteiras — sem correspondência no RELACIONA ({_solteiras.Count})",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(760, 440), MinimumSize = new Size(560, 300),
            };
            var g = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoGenerateColumns = true,
            };
            GridOrdena.Aplicar(g);
            g.DataSource = _solteiras.Select(c => new
            { NUMCONTA = c.NumConta, DESC2 = c.Desc2, Descricao = c.Descricao, Ocorrencias = c.Ocorrencias, Soma = c.Soma }).ToList();
            if (g.Columns.Contains("Descricao")) { g.Columns["Descricao"].HeaderText = "Descrição"; g.Columns["Descricao"].FillWeight = 40; }
            if (g.Columns.Contains("Ocorrencias")) { g.Columns["Ocorrencias"].HeaderText = "Ocorr."; g.Columns["Ocorrencias"].FillWeight = 12; g.Columns["Ocorrencias"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
            if (g.Columns.Contains("Soma")) { g.Columns["Soma"].DefaultCellStyle.Format = "N2"; g.Columns["Soma"].FillWeight = 18; g.Columns["Soma"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
            var rodape = new Label
            {
                Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0),
                Text = "Acrescente essas contas na planilha PESQUISA e importe (AlterData → Importar RELACIONA); depois clique Preparar de novo.",
                ForeColor = Color.DimGray,
            };
            f.Controls.Add(g);
            f.Controls.Add(rodape);
            f.Show(this);   // não-modal: dá pra comparar com o lote ao lado
        }

        private void Exportar()
        {
            if (_linhas == null || _linhas.Count == 0) { Aviso("Nada para exportar — clique Preparar."); return; }

            int pend = _linhas.Count(l => l.SemMapeamento || l.MeiaEntrada);
            if (pend > 0)
            {
                var r = MessageBox.Show(this,
                    $"Há {pend} linha(s) a conferir (sem mapeamento ou meia-entrada). O AlterData pode rejeitar ou lançar errado.\n\n" +
                    "Exportar mesmo assim?", "Pendências no lote",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (r != DialogResult.OK) return;
            }

            using (var dlg = new SaveFileDialog
            {
                DefaultExt = "xlsx", Filter = "Excel (*.xlsx)|*.xlsx", AddExtension = true,
                FileName = $"LoteAlterData_{dtDe.Value:yyyyMMdd}_{dtAte.Value:yyyyMMdd}.xlsx"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    UseWaitCursor = true;
                    ExportadorAlterData.GravarXlsx(_linhas, dlg.FileName);
                    statusLabel.Text = $"Lote exportado: {dlg.FileName}";
                    if (MessageBox.Show(this, "Lote gerado. Abrir agora?", "AlterData",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                        System.Diagnostics.Process.Start(dlg.FileName);
                }
                catch (Exception ex) { Aviso("Erro ao exportar:\n" + ex.Message); }
                finally { UseWaitCursor = false; }
            }
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "Exportar AlterData", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
