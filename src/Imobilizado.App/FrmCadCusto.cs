using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Contabil.Core;
using Contabil.Core.Apropriacao;
using Imobilizado.Dados;

namespace Imobilizado.App
{
    /// <summary>
    /// Cadastro de Custos (CADCUSTO.DBF) — tabela auxiliar da Apropriação: produto → contas de
    /// produção/em-curso/receita/custo-venda + percentuais. Visualiza e edita (incluir/alterar/
    /// excluir), com a descrição das contas resolvida pelo placon.
    /// </summary>
    public sealed class FrmCadCusto : Form
    {
        private TextBox txtPasta;
        private Button btnPasta, btnCarregar, btnIncluir, btnAlterar, btnExcluir;
        private DataGridView dgv;
        private Label lblResumo;

        private List<ProdutoCusto> _itens;
        private PlanoContas _plano;

        public FrmCadCusto()
        {
            Text = "Cadastro de Custos (CADCUSTO) — Apropriação";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 480);
            Size = new Size(1100, 560);
            MontarUI();
            txtPasta.Text = ConfigApp.CarregarPastaDados();
        }

        private void MontarUI()
        {
            var topo = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
            var lblPasta = new Label { Text = "Pasta de dados:", AutoSize = true, Location = new Point(10, 12) };
            txtPasta = new TextBox { Location = new Point(120, 8), Width = 620 };
            btnPasta = new Button { Text = "...", Location = new Point(746, 7), Width = 34 };
            btnPasta.Click += (s, e) => EscolherPasta();
            btnCarregar = new Button { Text = "Carregar", Location = new Point(800, 7), Width = 90 };
            btnCarregar.Click += (s, e) => Carregar();
            topo.Controls.AddRange(new Control[] { lblPasta, txtPasta, btnPasta, btnCarregar });

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

            lblResumo = new Label { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };

            Controls.Add(dgv);
            Controls.Add(barra);
            Controls.Add(topo);
            Controls.Add(lblResumo);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (File.Exists(Path.Combine(txtPasta.Text.Trim(), "CADCUSTO.DBF"))) Carregar();
        }

        private void EscolherPasta()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pasta com CADCUSTO.DBF (e placon.DBF)" })
            {
                if (Directory.Exists(txtPasta.Text)) dlg.SelectedPath = txtPasta.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtPasta.Text = dlg.SelectedPath;
            }
        }

        private void Carregar()
        {
            var pasta = txtPasta.Text.Trim();
            var cad = Path.Combine(pasta, "CADCUSTO.DBF");
            if (!File.Exists(cad)) { Aviso("Não encontrei o CADCUSTO.DBF na pasta."); return; }
            try
            {
                UseWaitCursor = true;
                var placon = Path.Combine(pasta, "placon.DBF");
                _plano = File.Exists(placon) ? PlanoContas.Carregar(placon) : null;
                _itens = ProdutoCusto.Carregar(cad);
                Exibir();
                btnIncluir.Enabled = btnAlterar.Enabled = btnExcluir.Enabled = true;
            }
            catch (Exception ex) { Aviso("Erro ao carregar:\n" + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        private string DescConta(string nc)
        {
            var v = (nc ?? "").Trim();
            if (v.Length == 0 || _plano == null) return "";
            return _plano.Contas.TryGetValue(v, out var c) ? c.Descricao.Trim() : "(não existe no placon)";
        }

        private void Exibir()
        {
            var view = _itens.OrderBy(i => i.Cod, StringComparer.Ordinal).Select(i => new
            {
                Cod = i.Cod,
                Descricao = (i.Desc ?? "").Trim(),
                Producao = i.Producao,
                EmCurso = i.EmCurso,
                Receita = i.Receita,
                CustoVenda = i.CustoVenda,
                Unid = (i.Unid ?? "").Trim(),
                Estoque = i.Estoque,
                Data = i.Data,
                P1 = i.Perc1, P2 = i.Perc2, P3 = i.Perc3, P4 = i.Perc4,
            }).ToList();
            dgv.DataSource = view;
            void Col(string n, string t, int w, bool dir = false, string fmt = null)
            {
                if (!dgv.Columns.Contains(n)) return;
                dgv.Columns[n].HeaderText = t; dgv.Columns[n].FillWeight = w;
                if (fmt != null) dgv.Columns[n].DefaultCellStyle.Format = fmt;
                if (dir) dgv.Columns[n].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            Col("Cod", "Cód.", 8); Col("Descricao", "Descrição", 28);
            Col("Producao", "Produção", 12); Col("EmCurso", "Em curso", 12);
            Col("Receita", "Receita", 12); Col("CustoVenda", "Custo venda", 12);
            Col("Unid", "Unid.", 10); Col("Estoque", "Estoque", 13, true, "N2"); Col("Data", "Data", 11);
            Col("P1", "%1", 8, true, "N2"); Col("P2", "%2", 8, true, "N2");
            Col("P3", "%3", 8, true, "N2"); Col("P4", "%4", 8, true, "N2");
            lblResumo.Text = $"{_itens.Count} produtos no CADCUSTO.";
        }

        private ProdutoCusto Selecionado()
        {
            var item = dgv.CurrentRow?.DataBoundItem;
            var cod = item?.GetType().GetProperty("Cod")?.GetValue(item) as string;
            return cod == null ? null : _itens.Find(i => i.Cod == cod);
        }

        private void Incluir()
        {
            using (var dlg = new FrmCadCustoItem(null, _plano))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var g = new CadCustoGravador(txtPasta.Text.Trim());
                    if (g.Existe(dlg.Item.Cod)) { Aviso($"Já existe o código {dlg.Item.Cod}."); return; }
                    g.Incluir(dlg.Item);
                    Carregar();
                }
                catch (Exception ex) { Aviso("Erro ao incluir:\n" + ex.Message); }
            }
        }

        private void Alterar()
        {
            var sel = Selecionado();
            if (sel == null) { Aviso("Selecione um produto."); return; }
            using (var dlg = new FrmCadCustoItem(sel, _plano))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { new CadCustoGravador(txtPasta.Text.Trim()).Alterar(dlg.Item); Carregar(); }
                catch (Exception ex) { Aviso("Erro ao alterar:\n" + ex.Message); }
            }
        }

        private void Excluir()
        {
            var sel = Selecionado();
            if (sel == null) { Aviso("Selecione um produto."); return; }
            if (MessageBox.Show(this, $"Excluir o produto {sel.Cod} — {(sel.Desc ?? "").Trim()}?",
                    "Excluir", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { new CadCustoGravador(txtPasta.Text.Trim()).Excluir(sel.Cod); Carregar(); }
            catch (Exception ex) { Aviso("Erro ao excluir:\n" + ex.Message); }
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "CADCUSTO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>Diálogo de inclusão/alteração de um produto do CADCUSTO, com descrição das contas via placon.</summary>
    public sealed class FrmCadCustoItem : Form
    {
        private readonly bool _incluindo;
        private readonly PlanoContas _plano;
        private TextBox txtCod, txtDesc, txtProducao, txtEmCurso, txtReceita, txtCustoVenda, txtUnid;
        private Label lblProd, lblCurso, lblRec, lblCv;
        private NumericUpDown numEstoque, numP1, numP2, numP3, numP4;
        private DateTimePicker dtData;

        public CadCustoGravador.ItemCadCusto Item { get; private set; }

        public FrmCadCustoItem(ProdutoCusto existente, PlanoContas plano)
        {
            _incluindo = existente == null;
            _plano = plano;
            Text = _incluindo ? "Incluir produto (CADCUSTO)" : $"Alterar produto {existente.Cod}";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(540, 478);
            MontarUI();
            if (existente != null) Preencher(existente);
        }

        private void MontarUI()
        {
            int y = 12; const int lx = 14, cx = 130;
            Label L(string t) { var l = new Label { Text = t, Location = new Point(lx, y + 3), AutoSize = true }; Controls.Add(l); return l; }
            TextBox T(int w, int max) { var t = new TextBox { Location = new Point(cx, y), Width = w, MaxLength = max }; Controls.Add(t); return t; }
            Label DescL() { var l = new Label { Location = new Point(cx + 96, y + 3), AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(300, 0) }; Controls.Add(l); return l; }

            L("Código:"); txtCod = T(60, 4); txtCod.Enabled = _incluindo; y += 30;
            L("Descrição:"); txtDesc = T(300, 25); y += 30;
            L("Cta. produção:"); txtProducao = T(90, 8); lblProd = DescL(); txtProducao.TextChanged += (s, e) => AtualizaDesc(txtProducao, lblProd); y += 30;
            L("Cta. em curso:"); txtEmCurso = T(90, 8); lblCurso = DescL(); txtEmCurso.TextChanged += (s, e) => AtualizaDesc(txtEmCurso, lblCurso); y += 30;
            L("Cta. receita:"); txtReceita = T(90, 8); lblRec = DescL(); txtReceita.TextChanged += (s, e) => AtualizaDesc(txtReceita, lblRec); y += 30;
            L("Cta. custo venda:"); txtCustoVenda = T(90, 8); lblCv = DescL(); txtCustoVenda.TextChanged += (s, e) => AtualizaDesc(txtCustoVenda, lblCv); y += 30;
            L("Unidade:"); txtUnid = T(120, 12); y += 30;
            L("Estoque (base):"); numEstoque = Num(120, 2); y += 30;
            L("Data-base:"); dtData = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", ShowCheckBox = true, Checked = false, Width = 130, Location = new Point(cx, y) }; Controls.Add(dtData); y += 30;
            L("Perc. 1 (%):"); numP1 = Num(80, 2); y += 30;
            L("Perc. 2 (%):"); numP2 = Num(80, 2); y += 30;
            L("Perc. 3 (%):"); numP3 = Num(80, 2); y += 30;
            L("Perc. 4 (%):"); numP4 = Num(80, 2); y += 36;

            var btnOk = new Button { Text = _incluindo ? "Incluir" : "Salvar", Location = new Point(cx, y), Width = 110 };
            btnOk.Click += (s, e) => Confirmar();
            var btnCancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(cx + 120, y), Width = 110 };
            Controls.AddRange(new Control[] { btnOk, btnCancel });
            AcceptButton = btnOk; CancelButton = btnCancel;
        }

        private NumericUpDown Num(int w, int dec)
        {
            var n = new NumericUpDown { Location = new Point(130, ControlYAtual()), Width = w, Maximum = 99_999_999, Minimum = 0, DecimalPlaces = dec, ThousandsSeparator = true };
            Controls.Add(n);
            return n;
        }
        // o Num precisa do y corrente — truque: lê o último label adicionado
        private int ControlYAtual() => Controls[Controls.Count - 1].Top - 3;

        private void AtualizaDesc(TextBox t, Label lbl)
        {
            var v = t.Text.Trim();
            if (v.Length == 0) { lbl.Text = ""; return; }
            if (_plano != null && _plano.Contas.TryGetValue(v, out var c)) { lbl.Text = c.Descricao.Trim(); lbl.ForeColor = Color.DimGray; }
            else { lbl.Text = "não existe no placon"; lbl.ForeColor = Color.Firebrick; }
        }

        private void Preencher(ProdutoCusto p)
        {
            txtCod.Text = p.Cod;
            txtDesc.Text = (p.Desc ?? "").Trim();
            txtProducao.Text = (p.Producao ?? "").Trim();
            txtEmCurso.Text = (p.EmCurso ?? "").Trim();
            txtReceita.Text = (p.Receita ?? "").Trim();
            txtCustoVenda.Text = (p.CustoVenda ?? "").Trim();
            txtUnid.Text = (p.Unid ?? "").Trim();
            numEstoque.Value = Math.Min(numEstoque.Maximum, Math.Max(0, p.Estoque));
            if (DateTime.TryParseExact(p.Data, "yyyyMMdd", null, DateTimeStyles.None, out var d)) { dtData.Value = d; dtData.Checked = true; }
            numP1.Value = Clamp(p.Perc1); numP2.Value = Clamp(p.Perc2); numP3.Value = Clamp(p.Perc3); numP4.Value = Clamp(p.Perc4);
        }

        private decimal Clamp(decimal v) => Math.Min(numP1.Maximum, Math.Max(0, v));

        private void Confirmar()
        {
            var cod = txtCod.Text.Trim();
            if (cod.Length == 0) { Aviso("Informe o código."); return; }
            Item = new CadCustoGravador.ItemCadCusto
            {
                Cod = cod,
                Desc = txtDesc.Text.Trim(),
                Producao = txtProducao.Text.Trim(),
                EmCurso = txtEmCurso.Text.Trim(),
                Receita = txtReceita.Text.Trim(),
                CustoVenda = txtCustoVenda.Text.Trim(),
                Unid = txtUnid.Text.Trim(),
                Estoque = numEstoque.Value,
                Data = dtData.Checked ? dtData.Value.Date : (DateTime?)null,
                Perc1 = numP1.Value, Perc2 = numP2.Value, Perc3 = numP3.Value, Perc4 = numP4.Value,
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "CADCUSTO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
