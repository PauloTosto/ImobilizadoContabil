using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Contabil.Core;
using Contabil.Core.Apropriacao;
using Imobilizado.Dados;

namespace Imobilizado.App
{
    /// <summary>
    /// Apropriação de custos: escolhe pasta + ano + âncora (PTPLA&lt;ano-1&gt;), pré-visualiza
    /// o lote (dry-run na grade) e grava no MOVFIN (DOC = SIST_APROP).
    ///
    /// Procedimento oficial: estrutura+apelidos do placon master; saldo ancorado no
    /// PTPLA&lt;ano-1&gt; (contas novas entram com saldo 0).
    /// </summary>
    public sealed class FrmApropriacao : Form
    {
        private TextBox txtPasta, txtAncora;
        private DateTimePicker dtData;
        private Button btnPasta, btnCalcular, btnGravar;
        private DataGridView dgv;
        private Label lblResumo;
        private CheckBox chkSubstituir;
        private StatusStrip status;
        private ToolStripStatusLabel statusLabel;

        private IReadOnlyList<LancamentoApropriacao> _lancamentos;

        public FrmApropriacao()
        {
            Text = "Apropriação de custos — Contabilidade";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 540);
            Size = new Size(1040, 620);
            MontarUI();
            txtPasta.Text = ConfigApp.CarregarPastaDados();
            AtualizarAncoraPadrao();
        }

        private void MontarUI()
        {
            var topo = new Panel { Dock = DockStyle.Top, Height = 82, Padding = new Padding(8) };
            var lblPasta = new Label { Text = "Pasta de dados:", AutoSize = true, Location = new Point(10, 12) };
            txtPasta = new TextBox { Location = new Point(120, 8), Width = 560, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            btnPasta = new Button { Text = "...", Location = new Point(686, 7), Width = 34, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnPasta.Click += (s, e) => EscolherPasta();

            var lblData = new Label { Text = "Apurar até a data:", AutoSize = true, Location = new Point(10, 46) };
            dtData = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Location = new Point(125, 42), Width = 110, Value = new DateTime(DateTime.Today.Year, 12, 31) };
            dtData.ValueChanged += (s, e) => AtualizarAncoraPadrao();
            var lblAnc = new Label { Text = "Âncora (PTPLA):", AutoSize = true, Location = new Point(250, 46) };
            txtAncora = new TextBox { Location = new Point(360, 42), Width = 160 };
            btnCalcular = new Button { Text = "Calcular (preview)", Location = new Point(530, 41), Width = 150 };
            btnCalcular.Click += (s, e) => Calcular();
            topo.Controls.AddRange(new Control[] { lblPasta, txtPasta, btnPasta, lblData, dtData, lblAnc, txtAncora, btnCalcular });

            dgv = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoGenerateColumns = true,
            };

            var rodape = new Panel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(6) };
            lblResumo = new Label { Location = new Point(6, 6), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            chkSubstituir = new CheckBox { Text = "Substituir apropriação já existente do ano", Location = new Point(6, 32), AutoSize = true };
            btnGravar = new Button { Text = "Gravar no MOVFIN", Location = new Point(400, 24), Width = 170, Height = 30, Enabled = false };
            btnGravar.Click += (s, e) => Gravar();
            rodape.Controls.AddRange(new Control[] { lblResumo, chkSubstituir, btnGravar });

            status = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("Pronto.");
            status.Items.Add(statusLabel);

            Controls.Add(dgv);
            Controls.Add(rodape);
            Controls.Add(topo);
            Controls.Add(status);
        }

        private int Ano => dtData.Value.Year;
        private void AtualizarAncoraPadrao() => txtAncora.Text = $"PTPLA{Ano - 1}.DBF";

        private void EscolherPasta()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pasta com placon.DBF, CADCUSTO.DBF, ENTSAI.DBF, MOVFIN.DBF e o PTPLA" })
            {
                if (Directory.Exists(txtPasta.Text)) dlg.SelectedPath = txtPasta.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtPasta.Text = dlg.SelectedPath;
            }
        }

        private void Calcular()
        {
            var pasta = txtPasta.Text.Trim();
            if (!Directory.Exists(pasta)) { Aviso("Pasta inválida."); return; }
            string Master = Path.Combine(pasta, "placon.DBF");
            string Ancora = Path.Combine(pasta, txtAncora.Text.Trim());
            string Cad = Path.Combine(pasta, "CADCUSTO.DBF");
            string Ent = Path.Combine(pasta, "ENTSAI.DBF");
            string Mov = Path.Combine(pasta, "MOVFIN.DBF");
            foreach (var f in new[] { Master, Ancora, Cad, Ent, Mov })
                if (!File.Exists(f)) { Aviso("Não encontrei: " + Path.GetFileName(f)); return; }

            try
            {
                UseWaitCursor = true;
                var plano = PlanoContas.Carregar(Master, Ancora);
                var produtos = ProdutoCusto.Carregar(Cad);
                var movs = MovimentoEstoque.CarregarPorProduto(Ent);
                var engine = new EngineSaldo(plano);
                var d1 = Ano + "0101"; var d2 = dtData.Value.ToString("yyyyMMdd");
                var anoStr = Ano.ToString();
                // re-rodar é idempotente: ignora a própria apropriação e o fechamento de balanço do ano
                var apur = engine.ApurarPeriodo(Mov, d1, d2,
                    excluir: (doc, data) => (doc == "SIST_APROP" || doc == "SIST_BAL") && data.StartsWith(anoStr, StringComparison.Ordinal));
                _lancamentos = new MotorApropriacao().Gerar(produtos, movs, apur, d1, d2);

                dgv.DataSource = _lancamentos.Select(l => new
                {
                    Data = System.DateTime.TryParseExact(l.Data, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var dd) ? dd.ToString("dd/MM/yyyy") : l.Data,
                    l.Debito, l.Credito, l.Valor, l.Historico
                }).ToList();
                if (dgv.Columns.Contains("Valor")) dgv.Columns["Valor"].DefaultCellStyle.Format = "N2";
                decimal total = _lancamentos.Sum(l => l.Valor);
                lblResumo.Text = $"Ano {Ano}: {_lancamentos.Count} lançamentos — total R$ {total:N2}  (pré-visualização, nada gravado)";
                btnGravar.Enabled = _lancamentos.Count > 0;
                statusLabel.Text = $"Calculado de {pasta} (âncora {txtAncora.Text.Trim()}).";
            }
            catch (Exception ex) { Aviso("Erro ao calcular:\n" + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        private void Gravar()
        {
            if (_lancamentos == null || _lancamentos.Count == 0) return;
            var pasta = txtPasta.Text.Trim();
            decimal total = _lancamentos.Sum(l => l.Valor);
            var msg = $"Gravar {_lancamentos.Count} lançamentos de apropriação do ano {Ano} " +
                      $"(total R$ {total:N2}) no MOVFIN?\n\nPasta: {pasta}" +
                      (chkSubstituir.Checked ? "\n\nA apropriação SIST_APROP já existente do ano será EXCLUÍDA antes." : "");
            if (MessageBox.Show(this, msg, "Confirmar gravação", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            try
            {
                UseWaitCursor = true;
                var grav = new MovfinGravador(pasta);
                int ja = grav.ContarPorDocAno(LancamentoApropriacao.Doc, Ano);
                if (ja > 0 && !chkSubstituir.Checked)
                {
                    Aviso($"Já existem {ja} lançamentos SIST_APROP em {Ano}.\nMarque \"Substituir\" para reprocessar o ano.");
                    return;
                }
                var linhas = _lancamentos.Select(l => new LinhaMovfin
                {
                    Data = l.Data, Debito = l.Debito, Credito = l.Credito,
                    Valor = l.Valor, Historico = l.Historico, Doc = LancamentoApropriacao.Doc,
                }).ToList();
                int n = grav.GravarLote(linhas, true, chkSubstituir.Checked ? (LancamentoApropriacao.Doc, Ano) : ((string, int)?)null);
                statusLabel.Text = $"Gravados {n} lançamentos de apropriação em {Ano}.";
                MessageBox.Show(this, $"Gravados {n} lançamentos no MOVFIN" + (chkSubstituir.Checked ? $" (após excluir {ja})." : "."),
                                "Concluído", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { Aviso("Erro ao gravar:\n" + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "Apropriação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
