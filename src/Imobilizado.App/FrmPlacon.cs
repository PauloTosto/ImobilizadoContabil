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
    /// Cadastro (CRUD) do PLACON master: lista as contas com filtro e permite incluir/alterar.
    /// É o cadastro mestre — onde contas novas entram (o PTPLA só ancora saldos).
    /// </summary>
    public sealed class FrmPlacon : Form
    {
        private TextBox txtPasta, txtFiltro;
        private Button btnPasta, btnRecarregar, btnIncluir, btnAlterar, btnExcluir;
        private DataGridView dgv;
        private Label lblResumo;
        private StatusStrip status;
        private ToolStripStatusLabel statusLabel;

        private List<ContaPlacao> _contas = new List<ContaPlacao>();

        public FrmPlacon()
        {
            Text = "Plano de contas (PLACON) — Contabilidade";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(820, 520);
            Size = new Size(940, 600);
            MontarUI();
            txtPasta.Text = ConfigApp.CarregarPastaDados();
        }

        private void MontarUI()
        {
            var topo = new Panel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(8) };
            var lblPasta = new Label { Text = "Pasta de dados:", AutoSize = true, Location = new Point(10, 12) };
            txtPasta = new TextBox { Location = new Point(120, 8), Width = 540, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            btnPasta = new Button { Text = "...", Location = new Point(666, 7), Width = 34, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnPasta.Click += (s, e) => EscolherPasta();
            btnRecarregar = new Button { Text = "Carregar", Location = new Point(706, 7), Width = 90, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnRecarregar.Click += (s, e) => Carregar();

            var lblFiltro = new Label { Text = "Filtro:", AutoSize = true, Location = new Point(10, 46) };
            txtFiltro = new TextBox { Location = new Point(120, 42), Width = 260 };
            txtFiltro.TextChanged += (s, e) => AplicarFiltro();
            btnIncluir = new Button { Text = "&Incluir conta", Location = new Point(400, 41), Width = 105, Enabled = false };
            btnIncluir.Click += (s, e) => Incluir();
            btnAlterar = new Button { Text = "&Alterar conta", Location = new Point(510, 41), Width = 105, Enabled = false };
            btnAlterar.Click += (s, e) => Alterar();
            btnExcluir = new Button { Text = "&Excluir conta", Location = new Point(620, 41), Width = 105, Enabled = false };
            btnExcluir.Click += (s, e) => Excluir();
            topo.Controls.AddRange(new Control[] { lblPasta, txtPasta, btnPasta, btnRecarregar, lblFiltro, txtFiltro, btnIncluir, btnAlterar, btnExcluir });

            dgv = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoGenerateColumns = true,
            };
            dgv.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) Alterar(); };

            lblResumo = new Label { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };
            status = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("Pronto.");
            status.Items.Add(statusLabel);

            Controls.Add(dgv);
            Controls.Add(lblResumo);
            Controls.Add(topo);
            Controls.Add(status);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (File.Exists(Path.Combine(txtPasta.Text.Trim(), "placon.DBF"))) Carregar();
        }

        private string PlaconPath => Path.Combine(txtPasta.Text.Trim(), "placon.DBF");

        private void EscolherPasta()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pasta com placon.DBF" })
            {
                if (Directory.Exists(txtPasta.Text)) dlg.SelectedPath = txtPasta.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtPasta.Text = dlg.SelectedPath;
            }
        }

        private void Carregar()
        {
            if (!File.Exists(PlaconPath)) { Aviso("placon.DBF não encontrado na pasta."); return; }
            try
            {
                UseWaitCursor = true;
                _contas = ContaPlacao.Carregar(PlaconPath);
                AplicarFiltro();
                btnIncluir.Enabled = btnAlterar.Enabled = btnExcluir.Enabled = true;
                statusLabel.Text = $"Carregado: {_contas.Count} contas.";
            }
            catch (Exception ex) { Aviso("Erro ao carregar:\n" + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        private void AplicarFiltro()
        {
            var f = txtFiltro.Text.Trim();
            IEnumerable<ContaPlacao> q = _contas;
            if (f.Length > 0)
                q = _contas.Where(c => (c.NumConta ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                                     || (c.Descricao ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                                     || (c.Apelido ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
            var lista = q.Select(c => new { c.NumConta, c.Grau, c.Descricao, Apelido = c.Apelido, c.Taxa, c.Sdo }).ToList();
            dgv.DataSource = lista;
            if (dgv.Columns.Contains("NumConta")) dgv.Columns["NumConta"].HeaderText = "Conta";
            if (dgv.Columns.Contains("Taxa")) { dgv.Columns["Taxa"].DefaultCellStyle.Format = "N2"; dgv.Columns["Taxa"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
            if (dgv.Columns.Contains("Sdo")) { dgv.Columns["Sdo"].DefaultCellStyle.Format = "N2"; dgv.Columns["Sdo"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
            lblResumo.Text = $"{lista.Count} de {_contas.Count} contas" + (f.Length > 0 ? $" (filtro: \"{f}\")" : "");
        }

        private bool Existe(string nc) => _contas.Exists(c => string.Equals(c.NumConta, nc, StringComparison.OrdinalIgnoreCase));

        private void Incluir()
        {
            using (var dlg = new FrmConta(null, Existe))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var g = new PlaconGravador(txtPasta.Text.Trim());
                    if (g.Existe(dlg.Conta.NumConta)) { Aviso($"Já existe a conta {dlg.Conta.NumConta}."); return; }
                    g.Incluir(dlg.Conta.NumConta, dlg.Conta.Grau, dlg.Conta.Descricao, dlg.Conta.Apelido, dlg.Conta.Taxa);
                    statusLabel.Text = $"Conta {dlg.Conta.NumConta} incluída.";
                    Carregar();
                }
                catch (Exception ex) { Aviso("Erro ao incluir:\n" + ex.Message); }
            }
        }

        private void Alterar()
        {
            var cod = (dgv.CurrentRow?.DataBoundItem as object);
            var num = cod?.GetType().GetProperty("NumConta")?.GetValue(cod) as string;
            if (num == null) { Aviso("Selecione uma conta."); return; }
            var atual = _contas.Find(c => c.NumConta == num);
            if (atual == null) return;

            var copia = new ContaPlacao { NumConta = atual.NumConta, Grau = atual.Grau, Descricao = atual.Descricao, Apelido = atual.Apelido, Taxa = atual.Taxa, Sdo = atual.Sdo };
            using (var dlg = new FrmConta(copia, Existe))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    new PlaconGravador(txtPasta.Text.Trim()).Alterar(dlg.Conta.NumConta, dlg.Conta.Grau, dlg.Conta.Descricao, dlg.Conta.Apelido, dlg.Conta.Taxa);
                    statusLabel.Text = $"Conta {dlg.Conta.NumConta} alterada.";
                    Carregar();
                }
                catch (Exception ex) { Aviso("Erro ao alterar:\n" + ex.Message); }
            }
        }

        private void Excluir()
        {
            var num = (dgv.CurrentRow?.DataBoundItem as object)?.GetType().GetProperty("NumConta")?.GetValue(dgv.CurrentRow.DataBoundItem) as string;
            if (num == null) { Aviso("Selecione uma conta."); return; }

            // não pode excluir conta que tenha filhas (qualquer conta cujo ancestral seja esta)
            bool temFilhas = _contas.Exists(c => !string.Equals(c.NumConta, num, StringComparison.OrdinalIgnoreCase)
                                              && AncestralDe(num, c.NumConta));
            if (temFilhas)
            {
                Aviso($"A conta {num} é sintética (tem contas filhas). Exclua/realoque as filhas antes.");
                return;
            }
            if (MessageBox.Show(this, $"Excluir a conta {num} do plano de contas?", "Confirmar exclusão",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                new PlaconGravador(txtPasta.Text.Trim()).Excluir(num);
                statusLabel.Text = $"Conta {num} excluída.";
                Carregar();
            }
            catch (Exception ex) { Aviso("Erro ao excluir:\n" + ex.Message); }
        }

        private static bool AncestralDe(string possivelAncestral, string conta)
        {
            foreach (var a in HierarquiaContas.Ancestrais(conta))
                if (string.Equals(a, possivelAncestral, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "Plano de contas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
