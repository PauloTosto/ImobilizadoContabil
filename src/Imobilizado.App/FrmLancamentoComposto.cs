using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Contabil.Core;
using Imobilizado.Dados;

namespace Imobilizado.App
{
    /// <summary>
    /// Lançamento composto (partida dobrada): uma conta PRINCIPAL (ex.: fornecedor) com o
    /// valor total, e N CONTRAPARTIDAS (ex.: contas de custo) cuja soma = o total. No MOVFIN
    /// vira 1 mestre (OUTRO_ID=0) + N detalhes (OUTRO_ID = MOV_ID do mestre).
    /// </summary>
    public sealed class FrmLancamentoComposto : Form
    {
        private readonly PlanoContas _plano;
        private DateTimePicker dtData;
        private ComboBox cboTipo;
        private TextBox txtPrincipal, txtDoc, txtForn, txtDocFisc;
        private Label lblPrincDesc, lblTotal;
        private RadioButton rbDebito, rbCredito;
        private DataGridView dgvLinhas;
        private AutoCompleteStringCollection _fonte;

        public LancamentoMovfin Mestre { get; private set; }
        public List<LancamentoMovfin> Detalhes { get; private set; }

        public FrmLancamentoComposto(LancamentoMovfin mestreExist, List<LancamentoMovfin> detalhesExist, PlanoContas plano)
        {
            _plano = plano;
            bool incluindo = mestreExist == null;
            Text = incluindo ? "Incluir lançamento composto (partida dobrada)" : $"Alterar composto (MOV_ID {mestreExist.MovId:0})";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ClientSize = new Size(640, 480);
            MinimumSize = new Size(560, 420);
            MontarUI();
            _fonte = Autocomplete.DeContas(_plano);
            Autocomplete.Aplicar(txtPrincipal, _fonte);
            if (!incluindo) Preencher(mestreExist, detalhesExist);
            else { dtData.Value = DateTime.Today; rbCredito.Checked = true; cboTipo.SelectedIndex = 0; }
        }

        private void MontarUI()
        {
            var topo = new Panel { Dock = DockStyle.Top, Height = 150, Padding = new Padding(8) };
            int y = 8; const int lx = 8, cx = 120;
            Label L(string t, int yy) => Add(topo, new Label { Text = t, Location = new Point(lx, yy + 3), AutoSize = true });

            L("Data:", y); dtData = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Location = new Point(cx, y), Width = 110 };
            L("Tipo:", y); cboTipo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(300, y), Width = 160 };
            cboTipo.Items.AddRange(new object[] { "Contábil", "Recebimento (R)", "Pagamento (P)" });
            topo.Controls.AddRange(new Control[] { dtData, cboTipo }); y += 32;

            L("Conta principal:", y); txtPrincipal = new TextBox { Location = new Point(cx, y), Width = 300 };
            txtPrincipal.TextChanged += (s, e) => AtualizaPrincDesc();
            topo.Controls.Add(txtPrincipal); y += 26;
            lblPrincDesc = Add(topo, new Label { Location = new Point(cx, y), AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(480, 0) }); y += 24;

            L("Principal é:", y); rbDebito = new RadioButton { Text = "Débito", Location = new Point(cx, y), AutoSize = true };
            rbCredito = new RadioButton { Text = "Crédito", Location = new Point(cx + 90, y), AutoSize = true };
            rbDebito.CheckedChanged += (s, e) => RotuloLinhas();
            var ldf = Add(topo, new Label { Text = "Doc. fiscal:", Location = new Point(300, y + 3), AutoSize = true });
            txtDocFisc = new TextBox { Location = new Point(385, y), Width = 150, MaxLength = 13 };  // DOC_FISC: referência compartilhada do composto
            topo.Controls.AddRange(new Control[] { rbDebito, rbCredito, txtDocFisc }); y += 28;

            L("Documento:", y); txtDoc = new TextBox { Location = new Point(cx, y), Width = 120, MaxLength = 13 };
            var lf = Add(topo, new Label { Text = "Fornecedor:", Location = new Point(260, y + 3), AutoSize = true });
            txtForn = new TextBox { Location = new Point(345, y), Width = 200, MaxLength = 35 };
            topo.Controls.AddRange(new Control[] { txtDoc, txtForn });

            dgvLinhas = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = true, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoGenerateColumns = false,
            };
            dgvLinhas.Columns.Add(new DataGridViewTextBoxColumn { Name = "Conta", HeaderText = "Contrapartida (conta)", FillWeight = 35 });
            dgvLinhas.Columns.Add(new DataGridViewTextBoxColumn { Name = "Valor", HeaderText = "Valor", FillWeight = 18, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            dgvLinhas.Columns.Add(new DataGridViewTextBoxColumn { Name = "Historico", HeaderText = "Histórico", FillWeight = 47 });
            dgvLinhas.CellEndEdit += (s, e) => AtualizaTotal();
            dgvLinhas.EditingControlShowing += (s, e) =>
            {
                if (dgvLinhas.CurrentCell != null && dgvLinhas.Columns[dgvLinhas.CurrentCell.ColumnIndex].Name == "Conta" && e.Control is TextBox tb)
                    Autocomplete.Aplicar(tb, _fonte);
            };

            var rodape = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8) };
            lblTotal = new Label { Location = new Point(8, 12), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            var btnOk = new Button { Text = "Salvar", Location = new Point(380, 8), Width = 110, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnOk.Click += (s, e) => Confirmar();
            var btnCancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(500, 8), Width = 110, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            rodape.Controls.AddRange(new Control[] { lblTotal, btnOk, btnCancel });
            CancelButton = btnCancel;

            Controls.Add(dgvLinhas);
            Controls.Add(rodape);
            Controls.Add(topo);
            RotuloLinhas();
        }

        private static T Add<T>(Control parent, T c) where T : Control { parent.Controls.Add(c); return c; }

        private void RotuloLinhas()
        {
            if (dgvLinhas?.Columns.Count > 0)
                dgvLinhas.Columns["Conta"].HeaderText = rbDebito.Checked ? "Contrapartida (Crédito)" : "Contrapartida (Débito)";
        }

        private void AtualizaPrincDesc()
        {
            var v = txtPrincipal.Text.Trim();
            var nc = _plano?.Resolver(v);
            if (v.Length == 0) lblPrincDesc.Text = "";
            else if (nc != null && _plano.Contas.TryGetValue(nc, out var c)) { lblPrincDesc.Text = $"{nc} — {c.Descricao}"; lblPrincDesc.ForeColor = Color.DimGray; }
            else { lblPrincDesc.Text = "conta não encontrada"; lblPrincDesc.ForeColor = Color.Firebrick; }
        }

        private void AtualizaTotal()
        {
            decimal total = 0;
            foreach (DataGridViewRow r in dgvLinhas.Rows)
                if (!r.IsNewRow && decimal.TryParse(Cel(r, "Valor"), NumberStyles.Any, CultureInfo.CurrentCulture, out var v)) total += v;
            lblTotal.Text = $"Valor do principal (= soma das contrapartidas): R$ {total:N2}";
        }

        private static string Cel(DataGridViewRow r, string col) => Convert.ToString(r.Cells[col].Value)?.Trim() ?? "";

        private void Preencher(LancamentoMovfin m, List<LancamentoMovfin> dets)
        {
            if (DateTime.TryParseExact(m.Data, "yyyyMMdd", null, DateTimeStyles.None, out var d)) dtData.Value = d;
            cboTipo.SelectedIndex = m.Tipo == "R" ? 1 : (m.Tipo == "P" ? 2 : 0);
            bool principalDebito = !string.IsNullOrWhiteSpace(m.Debito);
            rbDebito.Checked = principalDebito; rbCredito.Checked = !principalDebito;
            txtPrincipal.Text = principalDebito ? m.Debito : m.Credito;
            txtDoc.Text = m.Doc; txtForn.Text = m.Forn;
            // DOC_FISC é a referência fiscal compartilhada; pega do mestre, ou de algum detalhe se o mestre estiver vazio
            txtDocFisc.Text = !string.IsNullOrWhiteSpace(m.DocFisc) ? m.DocFisc
                            : (dets?.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.DocFisc))?.DocFisc ?? "");
            foreach (var det in dets)
            {
                var conta = principalDebito ? det.Credito : det.Debito; // contrapartida = lado oposto ao principal
                dgvLinhas.Rows.Add(conta, det.Valor.ToString("N2", CultureInfo.CurrentCulture), det.Historico);
            }
            AtualizaTotal();
        }

        private void Confirmar()
        {
            dgvLinhas.EndEdit();   // comita qualquer célula em edição ANTES de ler (senão o valor recém-digitado se perde)
            if (dgvLinhas.IsCurrentCellInEditMode) dgvLinhas.CommitEdit(DataGridViewDataErrorContexts.Commit);
            var princ = txtPrincipal.Text.Trim();
            if (_plano != null && _plano.Resolver(princ) == null) { Aviso("Conta principal não encontrada."); return; }
            if (!rbDebito.Checked && !rbCredito.Checked) { Aviso("Escolha se a conta principal é débito ou crédito."); return; }

            var linhas = new List<(string conta, decimal valor, string hist)>();
            foreach (DataGridViewRow r in dgvLinhas.Rows)
            {
                if (r.IsNewRow) continue;
                var conta = Cel(r, "Conta");
                if (conta.Length == 0) continue;
                if (_plano != null && _plano.Resolver(conta) == null) { Aviso($"Contrapartida não encontrada: {conta}"); return; }
                if (!decimal.TryParse(Cel(r, "Valor"), NumberStyles.Any, CultureInfo.CurrentCulture, out var v) || v <= 0)
                { Aviso($"Valor inválido na linha de {conta}."); return; }
                linhas.Add((conta, v, Cel(r, "Historico")));
            }
            if (linhas.Count == 0) { Aviso("Informe pelo menos uma contrapartida."); return; }

            decimal total = linhas.Sum(l => l.valor);
            bool principalDebito = rbDebito.Checked;
            var tipo = cboTipo.SelectedIndex == 1 ? "R" : (cboTipo.SelectedIndex == 2 ? "P" : "");
            bool tpFin = cboTipo.SelectedIndex != 0;
            var data = dtData.Value.ToString("yyyyMMdd");
            var docFisc = txtDocFisc.Text.Trim();   // referência fiscal compartilhada por todas as linhas do composto

            Mestre = new LancamentoMovfin
            {
                Data = data, Valor = total, Tipo = tipo, TpFin = tpFin, Doc = txtDoc.Text.Trim(), Forn = txtForn.Text.Trim(),
                DocFisc = docFisc,
                Debito = principalDebito ? princ : "", Credito = principalDebito ? "" : princ,
            };
            Detalhes = linhas.Select(l => new LancamentoMovfin
            {
                Data = data, Valor = l.valor, Tipo = tipo, TpFin = tpFin, Historico = l.hist, DocFisc = docFisc,
                Debito = principalDebito ? "" : l.conta,   // contrapartida no lado oposto ao principal
                Credito = principalDebito ? l.conta : "",
            }).ToList();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "Composto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
