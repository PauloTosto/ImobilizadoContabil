using System;
using System.Collections.Generic;
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
    /// Estoque por produto (ENTSAI.DBF) — kardex físico de entradas/saídas, porte do
    /// Estok_Produto (FLT_ESTO) do Clipper: filtro por período e código, totais de
    /// entrada/saída, edição com o código validado no CADCUSTO.
    /// </summary>
    public sealed class FrmEntSai : Form
    {
        private TextBox txtPasta, txtCod;
        private DateTimePicker dtDe, dtAte;
        private Button btnPasta, btnCarregar, btnIncluir, btnAlterar, btnExcluir;
        private DataGridView dgv;
        private Label lblResumo;

        private List<EntSaiGravador.ItemEntSai> _itens;
        private Dictionary<string, string> _produtos;   // COD → DESC (CADCUSTO)

        public FrmEntSai()
        {
            Text = "Estoque por Produto (ENTSAI) — Operações";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(920, 480);
            Size = new Size(1040, 580);
            MontarUI();
            txtPasta.Text = ConfigApp.CarregarPastaDados();
        }

        private void MontarUI()
        {
            var topo = new Panel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(8) };
            var lblPasta = new Label { Text = "Pasta de dados:", AutoSize = true, Location = new Point(10, 12) };
            txtPasta = new TextBox { Location = new Point(120, 8), Width = 620 };
            btnPasta = new Button { Text = "...", Location = new Point(746, 7), Width = 34 };
            btnPasta.Click += (s, e) => EscolherPasta();

            var hoje = DateTime.Today;
            var lblP = new Label { Text = "Período:", AutoSize = true, Location = new Point(10, 44) };
            dtDe = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 110, Location = new Point(120, 40), Value = new DateTime(hoje.Year, 1, 1) };
            var lblA = new Label { Text = "a", AutoSize = true, Location = new Point(238, 44) };
            dtAte = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 110, Location = new Point(258, 40), Value = hoje };
            var lblC = new Label { Text = "Código (prefixo):", AutoSize = true, Location = new Point(390, 44) };
            txtCod = new TextBox { Location = new Point(495, 40), Width = 60, MaxLength = 4 };
            btnCarregar = new Button { Text = "Carregar", Location = new Point(575, 39), Width = 90 };
            btnCarregar.Click += (s, e) => Carregar();
            topo.Controls.AddRange(new Control[] { lblPasta, txtPasta, btnPasta, lblP, dtDe, lblA, dtAte, lblC, txtCod, btnCarregar });

            var barra = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8, 4, 8, 4) };
            btnIncluir = new Button { Text = "&Incluir", Location = new Point(8, 5), Width = 86, Enabled = false };
            btnIncluir.Click += (s, e) => Incluir();
            btnAlterar = new Button { Text = "&Alterar", Location = new Point(100, 5), Width = 86, Enabled = false };
            btnAlterar.Click += (s, e) => Alterar();
            btnExcluir = new Button { Text = "&Excluir", Location = new Point(192, 5), Width = 86, Enabled = false };
            btnExcluir.Click += (s, e) => Excluir();
            barra.Controls.AddRange(new Control[] { btnIncluir, btnAlterar, btnExcluir });

            dgv = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoGenerateColumns = true,
            };
            GridOrdena.Aplicar(dgv);
            dgv.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) Alterar(); };

            lblResumo = new Label { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), Font = new Font(Font, FontStyle.Bold) };

            Controls.Add(dgv);
            Controls.Add(barra);
            Controls.Add(topo);
            Controls.Add(lblResumo);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (File.Exists(Path.Combine(txtPasta.Text.Trim(), "ENTSAI.DBF"))) Carregar();
        }

        private void EscolherPasta()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pasta com ENTSAI.DBF (e CADCUSTO.DBF)" })
            {
                if (Directory.Exists(txtPasta.Text)) dlg.SelectedPath = txtPasta.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtPasta.Text = dlg.SelectedPath;
            }
        }

        private void Carregar()
        {
            var pasta = txtPasta.Text.Trim();
            if (!File.Exists(Path.Combine(pasta, "ENTSAI.DBF"))) { Aviso("Não encontrei o ENTSAI.DBF na pasta."); return; }
            try
            {
                UseWaitCursor = true;
                var cad = Path.Combine(pasta, "CADCUSTO.DBF");
                _produtos = File.Exists(cad)
                    ? ProdutoCusto.Carregar(cad).GroupBy(p => p.Cod).ToDictionary(g => g.Key, g => (g.First().Desc ?? "").Trim())
                    : new Dictionary<string, string>();
                _itens = new EntSaiGravador(pasta).Ler();
                Exibir();
                btnIncluir.Enabled = btnAlterar.Enabled = btnExcluir.Enabled = true;
            }
            catch (Exception ex) { Aviso("Erro ao carregar:\n" + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        private void Exibir()
        {
            string d1 = dtDe.Value.ToString("yyyyMMdd"), d2 = dtAte.Value.ToString("yyyyMMdd");
            var pref = txtCod.Text.Trim();
            var filtrados = _itens.Where(i =>
                string.Compare(i.Data, d1, StringComparison.Ordinal) >= 0 &&
                string.Compare(i.Data, d2, StringComparison.Ordinal) <= 0 &&
                (pref.Length == 0 || i.Cod.StartsWith(pref, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(i => i.Data, StringComparer.Ordinal).ThenBy(i => i.Cod, StringComparer.Ordinal).ToList();

            dgv.DataSource = filtrados.Select(i => new
            {
                i.Recno,
                Data = Fmt(i.Data),
                Cod = i.Cod,
                Produto = _produtos.TryGetValue(i.Cod, out var d) ? d : "?",
                Entrada = i.Ent,
                ObsEntrada = i.ObsEnt,
                Saida = i.Sai,
                ObsSaida = i.ObsSai,
            }).ToList();
            if (dgv.Columns.Contains("Recno")) dgv.Columns["Recno"].Visible = false;
            void Col(string n, string t, int w, bool dir = false, string fmt = null)
            {
                if (!dgv.Columns.Contains(n)) return;
                dgv.Columns[n].HeaderText = t; dgv.Columns[n].FillWeight = w;
                if (fmt != null) dgv.Columns[n].DefaultCellStyle.Format = fmt;
                if (dir) dgv.Columns[n].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            Col("Data", "Data", 12); Col("Cod", "Cód.", 8); Col("Produto", "Produto", 24);
            Col("Entrada", "Entrada", 14, true, "N2"); Col("ObsEntrada", "Obs. entrada", 18);
            Col("Saida", "Saída", 14, true, "N2"); Col("ObsSaida", "Obs. saída", 18);

            lblResumo.Text = $"{filtrados.Count} movimentos | Σ Entradas {filtrados.Sum(i => i.Ent):N2}   Σ Saídas {filtrados.Sum(i => i.Sai):N2}";
        }

        private static string Fmt(string yyyymmdd)
            => DateTime.TryParseExact(yyyymmdd, "yyyyMMdd", null, DateTimeStyles.None, out var d) ? d.ToString("dd/MM/yyyy") : yyyymmdd;

        private EntSaiGravador.ItemEntSai Selecionado()
        {
            var item = dgv.CurrentRow?.DataBoundItem;
            var rec = item?.GetType().GetProperty("Recno")?.GetValue(item);
            return rec == null ? null : _itens.Find(i => i.Recno == Convert.ToInt32(rec));
        }

        private void Incluir()
        {
            using (var dlg = new FrmEntSaiItem(null, _produtos))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { new EntSaiGravador(txtPasta.Text.Trim()).Incluir(dlg.Item); Carregar(); }
                catch (Exception ex) { Aviso("Erro ao incluir:\n" + ex.Message); }
            }
        }

        private void Alterar()
        {
            var sel = Selecionado();
            if (sel == null) { Aviso("Selecione um movimento."); return; }
            using (var dlg = new FrmEntSaiItem(sel, _produtos))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { new EntSaiGravador(txtPasta.Text.Trim()).Alterar(dlg.Item); Carregar(); }
                catch (Exception ex) { Aviso("Erro ao alterar:\n" + ex.Message); }
            }
        }

        private void Excluir()
        {
            var sel = Selecionado();
            if (sel == null) { Aviso("Selecione um movimento."); return; }
            if (MessageBox.Show(this, $"Excluir o movimento de {Fmt(sel.Data)} cód {sel.Cod} (E={sel.Ent:N2} S={sel.Sai:N2})?",
                    "Excluir", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { new EntSaiGravador(txtPasta.Text.Trim()).Excluir(sel.Recno); Carregar(); }
            catch (Exception ex) { Aviso("Erro ao excluir:\n" + ex.Message); }
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "ENTSAI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>Diálogo de inclusão/alteração de um movimento do ENTSAI (código validado no CADCUSTO).</summary>
    public sealed class FrmEntSaiItem : Form
    {
        private readonly IReadOnlyDictionary<string, string> _produtos;
        private DateTimePicker dtData;
        private TextBox txtCod, txtObsEnt, txtObsSai;
        private Label lblProd;
        private NumericUpDown numEnt, numSai;

        public EntSaiGravador.ItemEntSai Item { get; private set; }

        public FrmEntSaiItem(EntSaiGravador.ItemEntSai existente, IReadOnlyDictionary<string, string> produtos)
        {
            _produtos = produtos ?? new Dictionary<string, string>();
            Text = existente == null ? "Incluir movimento (ENTSAI)" : "Alterar movimento";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(480, 262);
            MontarUI();
            if (existente != null)
            {
                Item = new EntSaiGravador.ItemEntSai { Recno = existente.Recno };
                if (DateTime.TryParseExact(existente.Data, "yyyyMMdd", null, DateTimeStyles.None, out var d)) dtData.Value = d;
                txtCod.Text = existente.Cod;
                numEnt.Value = Math.Min(numEnt.Maximum, Math.Max(0, existente.Ent));
                numSai.Value = Math.Min(numSai.Maximum, Math.Max(0, existente.Sai));
                txtObsEnt.Text = existente.ObsEnt; txtObsSai.Text = existente.ObsSai;
            }
            else Item = new EntSaiGravador.ItemEntSai();
        }

        private void MontarUI()
        {
            int y = 14; const int lx = 14, cx = 120;
            Label L(string t) { var l = new Label { Text = t, Location = new Point(lx, y + 3), AutoSize = true }; Controls.Add(l); return l; }

            L("Data:"); dtData = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 120, Location = new Point(cx, y) };
            Controls.Add(dtData); y += 32;
            L("Código:"); txtCod = new TextBox { Location = new Point(cx, y), Width = 60, MaxLength = 4, CharacterCasing = CharacterCasing.Upper };
            lblProd = new Label { Location = new Point(cx + 70, y + 3), AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(260, 0) };
            txtCod.TextChanged += (s, e) =>
            {
                var v = txtCod.Text.Trim();
                if (v.Length == 0) { lblProd.Text = ""; return; }
                if (_produtos.TryGetValue(v, out var d)) { lblProd.Text = d; lblProd.ForeColor = Color.DimGray; }
                else { lblProd.Text = "não existe no CADCUSTO"; lblProd.ForeColor = Color.Firebrick; }
            };
            Controls.AddRange(new Control[] { txtCod, lblProd }); y += 32;
            L("Entrada:"); numEnt = new NumericUpDown { Location = new Point(cx, y), Width = 120, Maximum = 99_999_999, DecimalPlaces = 2, ThousandsSeparator = true };
            Controls.Add(numEnt); y += 32;
            L("Obs. entrada:"); txtObsEnt = new TextBox { Location = new Point(cx, y), Width = 200, MaxLength = 15 };
            Controls.Add(txtObsEnt); y += 32;
            L("Saída:"); numSai = new NumericUpDown { Location = new Point(cx, y), Width = 120, Maximum = 99_999_999, DecimalPlaces = 2, ThousandsSeparator = true };
            Controls.Add(numSai); y += 32;
            L("Obs. saída:"); txtObsSai = new TextBox { Location = new Point(cx, y), Width = 200, MaxLength = 15 };
            Controls.Add(txtObsSai); y += 38;

            var btnOk = new Button { Text = "Salvar", Location = new Point(cx, y), Width = 110 };
            btnOk.Click += (s, e) => Confirmar();
            var btnCancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(cx + 120, y), Width = 110 };
            Controls.AddRange(new Control[] { btnOk, btnCancel });
            AcceptButton = btnOk; CancelButton = btnCancel;
        }

        private void Confirmar()
        {
            var cod = txtCod.Text.Trim();
            if (cod.Length == 0) { MessageBox.Show(this, "Informe o código do produto.", "ENTSAI", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!_produtos.ContainsKey(cod) &&
                MessageBox.Show(this, $"O código {cod} não existe no CADCUSTO. Salvar mesmo assim?", "ENTSAI",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Item.Data = dtData.Value.ToString("yyyyMMdd");
            Item.Cod = cod;
            Item.Ent = numEnt.Value; Item.Sai = numSai.Value;
            Item.ObsEnt = txtObsEnt.Text.Trim(); Item.ObsSai = txtObsSai.Text.Trim();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
