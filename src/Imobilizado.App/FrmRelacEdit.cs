using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;
using Contabil.Core;
using Imobilizado.Dados;

namespace Imobilizado.App
{
    /// <summary>
    /// Relações da absorção (RELAC.DBF) — editor do par débito (ativo/estoque) × crédito
    /// (custo apropriado) + FUNCAO ('A' = absorção) + % de rateio. Porte do FLT_REL do Clipper.
    /// As linhas do mesmo grupo de crédito devem somar 100% (ou uma linha única sem %).
    /// </summary>
    public sealed class FrmRelacEdit : Form
    {
        private TextBox txtPasta;
        private Button btnPasta, btnCarregar, btnIncluir, btnAlterar, btnExcluir;
        private DataGridView dgv;
        private Label lblResumo;

        private List<RelacGravador.ItemRelac> _itens;
        private PlanoContas _plano;

        public FrmRelacEdit()
        {
            Text = "Relações da Absorção (RELAC) — Operações";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 460);
            Size = new Size(1020, 560);
            MontarUI();
            txtPasta.Text = ConfigApp.CarregarPastaDados();
        }

        private void MontarUI()
        {
            var topo = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
            var lblPasta = new Label { Text = "Pasta de dados:", AutoSize = true, Location = new Point(10, 12) };
            txtPasta = new TextBox { Location = new Point(120, 8), Width = 600 };
            btnPasta = new Button { Text = "...", Location = new Point(726, 7), Width = 34 };
            btnPasta.Click += (s, e) => EscolherPasta();
            btnCarregar = new Button { Text = "Carregar", Location = new Point(780, 7), Width = 90 };
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
            if (File.Exists(Path.Combine(txtPasta.Text.Trim(), "RELAC.DBF"))) Carregar();
        }

        private void EscolherPasta()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pasta com RELAC.DBF (e placon.DBF)" })
            {
                if (Directory.Exists(txtPasta.Text)) dlg.SelectedPath = txtPasta.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtPasta.Text = dlg.SelectedPath;
            }
        }

        private void Carregar()
        {
            var pasta = txtPasta.Text.Trim();
            if (!File.Exists(Path.Combine(pasta, "RELAC.DBF"))) { Aviso("Não encontrei o RELAC.DBF na pasta."); return; }
            try
            {
                UseWaitCursor = true;
                var placon = Path.Combine(pasta, "placon.DBF");
                _plano = File.Exists(placon) ? PlanoContas.Carregar(placon) : null;
                _itens = new RelacGravador(pasta).Ler();
                Exibir();
                btnIncluir.Enabled = btnAlterar.Enabled = btnExcluir.Enabled = true;
            }
            catch (Exception ex) { Aviso("Erro ao carregar:\n" + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        private string Desc(string nc)
        {
            var v = (nc ?? "").Trim();
            if (v.Length == 0 || _plano == null) return "";
            return _plano.Contas.TryGetValue(v, out var c) ? c.Descricao.Trim() : "(não existe no placon)";
        }

        private void Exibir()
        {
            var view = _itens.Select(i => new
            {
                i.Recno,
                Debito = i.Debito, DescDebito = Desc(i.Debito),
                Credito = i.Credito, DescCredito = Desc(i.Credito),
                Funcao = i.Funcao, Perc = i.Quant1,
            }).ToList();
            dgv.DataSource = view;
            if (dgv.Columns.Contains("Recno")) dgv.Columns["Recno"].Visible = false;
            void Col(string n, string t, int w, bool dir = false, string fmt = null)
            {
                if (!dgv.Columns.Contains(n)) return;
                dgv.Columns[n].HeaderText = t; dgv.Columns[n].FillWeight = w;
                if (fmt != null) dgv.Columns[n].DefaultCellStyle.Format = fmt;
                if (dir) dgv.Columns[n].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            Col("Debito", "Débito (ativo)", 12); Col("DescDebito", "Descrição débito", 28);
            Col("Credito", "Crédito (custo aprop.)", 14); Col("DescCredito", "Descrição crédito", 28);
            Col("Funcao", "Função", 8); Col("Perc", "% rateio", 10, true, "N2");

            // grupos de crédito cujos %s não somam 100 (e não são linha única sem %) → alerta
            var problemas = _itens.Where(i => i.Funcao == "A" && i.Credito.Length >= 5)
                .GroupBy(i => i.Credito.Substring(0, 5))
                .Where(g => g.Any(x => x.Quant1 > 0) && decimal.Round(g.Sum(x => x.Quant1), 2) != 100m)
                .Select(g => g.Key).ToList();
            lblResumo.Text = $"{_itens.Count} relações ({_itens.Count(i => i.Funcao == "A")} com FUNCAO=A)"
                + (problemas.Count > 0 ? $"   ⚠ grupos com %s que NÃO somam 100: {string.Join(", ", problemas.Take(8))}" : "   (rateios OK)");
            lblResumo.ForeColor = problemas.Count > 0 ? Color.Firebrick : Color.Green;
        }

        private RelacGravador.ItemRelac Selecionado()
        {
            var item = dgv.CurrentRow?.DataBoundItem;
            var rec = item?.GetType().GetProperty("Recno")?.GetValue(item);
            return rec == null ? null : _itens.Find(i => i.Recno == Convert.ToInt32(rec));
        }

        private void Incluir()
        {
            using (var dlg = new FrmRelacItem(null, _plano))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { new RelacGravador(txtPasta.Text.Trim()).Incluir(dlg.Item); Carregar(); }
                catch (Exception ex) { Aviso("Erro ao incluir:\n" + ex.Message); }
            }
        }

        private void Alterar()
        {
            var sel = Selecionado();
            if (sel == null) { Aviso("Selecione uma relação."); return; }
            using (var dlg = new FrmRelacItem(sel, _plano))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { new RelacGravador(txtPasta.Text.Trim()).Alterar(dlg.Item); Carregar(); }
                catch (Exception ex) { Aviso("Erro ao alterar:\n" + ex.Message); }
            }
        }

        private void Excluir()
        {
            var sel = Selecionado();
            if (sel == null) { Aviso("Selecione uma relação."); return; }
            if (MessageBox.Show(this, $"Excluir a relação D={sel.Debito} C={sel.Credito} ({sel.Quant1:N2}%)?",
                    "Excluir", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { new RelacGravador(txtPasta.Text.Trim()).Excluir(sel.Recno); Carregar(); }
            catch (Exception ex) { Aviso("Erro ao excluir:\n" + ex.Message); }
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "RELAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>Diálogo de inclusão/alteração de uma relação do RELAC, com descrições do placon.</summary>
    public sealed class FrmRelacItem : Form
    {
        private readonly PlanoContas _plano;
        private TextBox txtDeb, txtCred, txtFuncao;
        private Label lblDebDesc, lblCredDesc;
        private NumericUpDown numPerc;

        public RelacGravador.ItemRelac Item { get; private set; }

        public FrmRelacItem(RelacGravador.ItemRelac existente, PlanoContas plano)
        {
            _plano = plano;
            Text = existente == null ? "Incluir relação (RELAC)" : "Alterar relação";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(520, 226);
            MontarUI();
            if (existente != null)
            {
                Item = new RelacGravador.ItemRelac { Recno = existente.Recno };
                txtDeb.Text = existente.Debito; txtCred.Text = existente.Credito;
                txtFuncao.Text = existente.Funcao; numPerc.Value = Math.Min(numPerc.Maximum, Math.Max(0, existente.Quant1));
            }
            else
            {
                Item = new RelacGravador.ItemRelac();
                txtFuncao.Text = "A";
            }
        }

        private void MontarUI()
        {
            int y = 14; const int lx = 14, cx = 150;
            Label L(string t) { var l = new Label { Text = t, Location = new Point(lx, y + 3), AutoSize = true }; Controls.Add(l); return l; }

            L("Débito (ativo):"); txtDeb = new TextBox { Location = new Point(cx, y), Width = 90, MaxLength = 8 };
            lblDebDesc = new Label { Location = new Point(cx + 100, y + 3), AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(250, 0) };
            txtDeb.TextChanged += (s, e) => AtualizaDesc(txtDeb, lblDebDesc);
            Controls.AddRange(new Control[] { txtDeb, lblDebDesc }); y += 34;

            L("Crédito (custo aprop.):"); txtCred = new TextBox { Location = new Point(cx, y), Width = 90, MaxLength = 8 };
            lblCredDesc = new Label { Location = new Point(cx + 100, y + 3), AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(250, 0) };
            txtCred.TextChanged += (s, e) => AtualizaDesc(txtCred, lblCredDesc);
            Controls.AddRange(new Control[] { txtCred, lblCredDesc }); y += 34;

            L("Função:"); txtFuncao = new TextBox { Location = new Point(cx, y), Width = 40, MaxLength = 1, CharacterCasing = CharacterCasing.Upper };
            var lblF = new Label { Text = "('A' = absorção)", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(cx + 50, y + 3) };
            Controls.AddRange(new Control[] { txtFuncao, lblF }); y += 34;

            L("% rateio:"); numPerc = new NumericUpDown { Location = new Point(cx, y), Width = 90, Maximum = 99.99m, Minimum = 0, DecimalPlaces = 2 };
            var lblP = new Label { Text = "(0 = leva o valor cheio do grupo)", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(cx + 100, y + 3) };
            Controls.AddRange(new Control[] { numPerc, lblP }); y += 42;

            var btnOk = new Button { Text = "Salvar", Location = new Point(cx, y), Width = 110 };
            btnOk.Click += (s, e) => Confirmar();
            var btnCancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(cx + 120, y), Width = 110 };
            Controls.AddRange(new Control[] { btnOk, btnCancel });
            AcceptButton = btnOk; CancelButton = btnCancel;
        }

        private void AtualizaDesc(TextBox t, Label lbl)
        {
            var v = t.Text.Trim();
            if (v.Length == 0) { lbl.Text = ""; return; }
            if (_plano != null && _plano.Contas.TryGetValue(v, out var c)) { lbl.Text = c.Descricao.Trim(); lbl.ForeColor = Color.DimGray; }
            else { lbl.Text = "não existe no placon"; lbl.ForeColor = Color.Firebrick; }
        }

        private void Confirmar()
        {
            if (txtDeb.Text.Trim().Length == 0 || txtCred.Text.Trim().Length == 0)
            { MessageBox.Show(this, "Informe o débito e o crédito.", "RELAC", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Item.Debito = txtDeb.Text.Trim();
            Item.Credito = txtCred.Text.Trim();
            Item.Funcao = txtFuncao.Text.Trim();
            Item.Quant1 = numPerc.Value;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
