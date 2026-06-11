using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Contabil.Core.Apropriacao;
using Imobilizado.Dados;

namespace Imobilizado.App
{
    /// <summary>
    /// Balanço de Produtos — porte do Bal_Prod (FLT_PROD) do Clipper: posição FÍSICA por produto
    /// do CADCUSTO no período. Sdo Anterior = ESTOQUE base do cadastro + Σ(ENT−SAI) do ENTSAI
    /// desde a data-base até antes do período; Entregas = Σ ENT e Vendas = Σ SAI no período;
    /// Sdo Atual = anterior + entregas − vendas. Produtos sem nenhum valor ficam fora.
    /// </summary>
    public sealed class FrmBalancoProdutos : Form
    {
        private TextBox txtPasta;
        private DateTimePicker dtDe, dtAte;
        private Button btnPasta, btnCalcular;
        private DataGridView dgv;
        private Label lblResumo;

        public FrmBalancoProdutos()
        {
            Text = "Balanço de Produtos (físico) — Operações";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(880, 460);
            Size = new Size(980, 560);
            MontarUI();
            txtPasta.Text = ConfigApp.CarregarPastaDados();
        }

        private void MontarUI()
        {
            var topo = new Panel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(8) };
            var lblPasta = new Label { Text = "Pasta de dados:", AutoSize = true, Location = new Point(10, 12) };
            txtPasta = new TextBox { Location = new Point(120, 8), Width = 600 };
            btnPasta = new Button { Text = "...", Location = new Point(726, 7), Width = 34 };
            btnPasta.Click += (s, e) => EscolherPasta();

            var hoje = DateTime.Today;
            var lblP = new Label { Text = "Período:", AutoSize = true, Location = new Point(10, 44) };
            dtDe = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 110, Location = new Point(120, 40), Value = new DateTime(hoje.Year, 1, 1) };
            var lblA = new Label { Text = "a", AutoSize = true, Location = new Point(238, 44) };
            dtAte = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 110, Location = new Point(258, 40), Value = hoje };
            btnCalcular = new Button { Text = "Calcular", Location = new Point(390, 39), Width = 100 };
            btnCalcular.Click += (s, e) => Calcular();
            topo.Controls.AddRange(new Control[] { lblPasta, txtPasta, btnPasta, lblP, dtDe, lblA, dtAte, btnCalcular });

            dgv = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoGenerateColumns = true,
            };
            GridOrdena.Aplicar(dgv);

            lblResumo = new Label { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), Font = new Font(Font, FontStyle.Bold) };

            Controls.Add(dgv);
            Controls.Add(topo);
            Controls.Add(lblResumo);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            var p = txtPasta.Text.Trim();
            if (File.Exists(Path.Combine(p, "CADCUSTO.DBF")) && File.Exists(Path.Combine(p, "ENTSAI.DBF"))) Calcular();
        }

        private void EscolherPasta()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pasta com CADCUSTO.DBF e ENTSAI.DBF" })
            {
                if (Directory.Exists(txtPasta.Text)) dlg.SelectedPath = txtPasta.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtPasta.Text = dlg.SelectedPath;
            }
        }

        private void Calcular()
        {
            var pasta = txtPasta.Text.Trim();
            var cad = Path.Combine(pasta, "CADCUSTO.DBF");
            var entsai = Path.Combine(pasta, "ENTSAI.DBF");
            if (!File.Exists(cad) || !File.Exists(entsai)) { Aviso("A pasta precisa conter CADCUSTO.DBF e ENTSAI.DBF."); return; }
            try
            {
                UseWaitCursor = true;
                string d1 = dtDe.Value.ToString("yyyyMMdd"), d2 = dtAte.Value.ToString("yyyyMMdd");
                var produtos = ProdutoCusto.Carregar(cad);
                var movs = new EntSaiGravador(pasta).Ler().ToLookup(m => m.Cod);

                var linhas = produtos.Select(p =>
                {
                    // Sdo Anterior = ESTOQUE base (na DATA do cadastro) + Σ(ENT−SAI) de depois da
                    // data-base até ANTES do período (mesma varredura do Sele_Balanco do Clipper).
                    var dataBase = (p.Data ?? "").Trim();
                    decimal ant = p.Estoque, ent = 0m, sai = 0m;
                    foreach (var m in movs[p.Cod])
                    {
                        if (dataBase.Length == 8 && string.Compare(m.Data, dataBase, StringComparison.Ordinal) <= 0) continue;
                        if (string.Compare(m.Data, d1, StringComparison.Ordinal) < 0) { ant += m.Ent - m.Sai; }
                        else if (string.Compare(m.Data, d2, StringComparison.Ordinal) <= 0) { ent += m.Ent; sai += m.Sai; }
                    }
                    return new
                    {
                        Cod = p.Cod,
                        Produto = (p.Desc ?? "").Trim(),
                        Unid = (p.Unid ?? "").Trim(),
                        SdoAnterior = ant,
                        Entregas = ent,
                        Vendas = sai,
                        SdoAtual = ant + ent - sai,
                    };
                })
                .Where(l => l.SdoAnterior != 0 || l.Entregas != 0 || l.Vendas != 0)
                .OrderBy(l => l.Cod, StringComparer.Ordinal)
                .ToList();

                dgv.DataSource = linhas;
                void Col(string n, string t, int w, bool dir = false, string fmt = null)
                {
                    if (!dgv.Columns.Contains(n)) return;
                    dgv.Columns[n].HeaderText = t; dgv.Columns[n].FillWeight = w;
                    if (fmt != null) dgv.Columns[n].DefaultCellStyle.Format = fmt;
                    if (dir) dgv.Columns[n].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
                Col("Cod", "Cód.", 8); Col("Produto", "Produto", 30); Col("Unid", "Unid.", 10);
                Col("SdoAnterior", "Sdo anterior", 14, true, "N2"); Col("Entregas", "Entregas", 14, true, "N2");
                Col("Vendas", "Vendas/Saídas", 14, true, "N2"); Col("SdoAtual", "Sdo atual", 14, true, "N2");

                lblResumo.Text = $"{linhas.Count} produtos | Σ Entregas {linhas.Sum(l => l.Entregas):N2}   Σ Vendas {linhas.Sum(l => l.Vendas):N2}";
            }
            catch (Exception ex) { Aviso("Erro ao calcular:\n" + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "Balanço de Produtos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
