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
    /// Editor DIRETO das linhas de um grupo composto. Mostra TODAS as linhas (o mestre e os
    /// detalhes) como estão no MOVFIN — cada uma com débito, crédito, VALOR e histórico
    /// editáveis livremente (sem agregação automática; o valor do principal é editável como
    /// qualquer outro). Salvar atualiza cada linha existente pelo RECNO (UPDATE no lugar,
    /// preservando MOV_ID/OUTRO_ID e a identidade), exclui as removidas e insere as novas
    /// (ligadas ao mestre). Data, Tipo e Doc. fiscal são do grupo (aplicados a todas as linhas).
    /// </summary>
    public sealed class FrmGrupoComposto : Form
    {
        private readonly PlanoContas _plano;
        private readonly List<LancamentoMovfin> _orig;
        private readonly decimal _mestreMovId;   // MOV_ID do mestre (p/ ligar linhas novas)

        private DateTimePicker dtData;
        private ComboBox cboTipo;
        private TextBox txtDocFisc;
        private DataGridView dgv;
        private Label lblBalanco;
        private AutoCompleteStringCollection _fonte;

        /// <summary>Linhas a gravar; cada uma com Recno preservado (Recno=0 = linha nova a inserir).</summary>
        public List<LancamentoMovfin> Linhas { get; private set; }
        /// <summary>RECNOs de linhas que o usuário removeu (a excluir).</summary>
        public List<int> Excluidos { get; private set; } = new List<int>();

        public FrmGrupoComposto(List<LancamentoMovfin> grupo, PlanoContas plano)
        {
            _plano = plano;
            _orig = grupo ?? new List<LancamentoMovfin>();
            var mestre = _orig.FirstOrDefault(l => l.OutroId == 0) ?? _orig.FirstOrDefault();
            _mestreMovId = mestre?.MovId ?? 0;

            Text = $"Editar composto — mestre MOV_ID {_mestreMovId:0} ({_orig.Count} linhas)";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ClientSize = new Size(820, 460);
            MinimumSize = new Size(720, 380);

            MontarUI();
            _fonte = Autocomplete.DeContas(_plano);
            Preencher(mestre);
        }

        private void MontarUI()
        {
            var topo = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 8, 8, 4) };
            Label L(string t, int x) => Add(topo, new Label { Text = t, Location = new Point(x, 8), AutoSize = true });
            L("Data:", 4); dtData = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Location = new Point(46, 4), Width = 105 };
            L("Tipo:", 165); cboTipo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(205, 4), Width = 150 };
            cboTipo.Items.AddRange(new object[] { "Contábil", "Recebimento (R)", "Pagamento (P)" });
            L("Doc. fiscal:", 370); txtDocFisc = new TextBox { Location = new Point(450, 4), Width = 130, MaxLength = 13 };
            topo.Controls.AddRange(new Control[] { dtData, cboTipo, txtDocFisc });

            dgv = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersVisible = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoGenerateColumns = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
            };
            dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tipo", HeaderText = "", FillWeight = 12, ReadOnly = true });
            dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "Debito", HeaderText = "Débito (conta)", FillWeight = 24 });
            dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "Credito", HeaderText = "Crédito (conta)", FillWeight = 24 });
            dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "Valor", HeaderText = "Valor", FillWeight = 16, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "Historico", HeaderText = "Histórico", FillWeight = 40 });
            dgv.CellEndEdit += (s, e) => { ReformataValor(e); AtualizaBalanco(); };
            dgv.UserDeletingRow += Dgv_UserDeletingRow;
            dgv.EditingControlShowing += (s, e) =>
            {
                if (dgv.CurrentCell == null || !(e.Control is TextBox tb)) return;
                var col = dgv.Columns[dgv.CurrentCell.ColumnIndex].Name;
                tb.KeyPress -= ValorKeyPress;   // tira o handler de qualquer célula reaproveitada
                if (col == "Debito" || col == "Credito") { Autocomplete.Aplicar(tb, _fonte); }
                else if (col == "Valor")
                {
                    tb.AutoCompleteMode = AutoCompleteMode.None;
                    _valorReset = true;          // a 1ª tecla recomeça do zero (máscara de centavos)
                    tb.KeyPress += ValorKeyPress;
                }
                else tb.AutoCompleteMode = AutoCompleteMode.None;
            };

            // Rodapé como FlowLayoutPanel (RightToLeft): os botões se posicionam sozinhos,
            // sem o bug de Anchor+Dock que jogava o "Salvar" pra fora da área visível.
            var rodape = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Padding = new Padding(8, 10, 8, 10),
            };
            var btnOk = new Button { Text = "✔ Salvar", Size = new Size(140, 30) };
            btnOk.Click += (s, e) => Confirmar();
            var btnCancel = new Button { Text = "Cancelar", Size = new Size(120, 30), DialogResult = DialogResult.Cancel };
            lblBalanco = new Label { AutoSize = false, Size = new Size(420, 30), TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) };
            rodape.Controls.Add(btnOk);       // 1º = mais à direita
            rodape.Controls.Add(btnCancel);   // à esquerda do Salvar
            rodape.Controls.Add(lblBalanco);  // mais à esquerda
            AcceptButton = btnOk;             // Enter salva
            CancelButton = btnCancel;         // Esc cancela

            Controls.Add(dgv);
            Controls.Add(rodape);
            Controls.Add(topo);
        }

        private static T Add<T>(Control parent, T c) where T : Control { parent.Controls.Add(c); return c; }

        private void Preencher(LancamentoMovfin mestre)
        {
            if (mestre != null && DateTime.TryParseExact(mestre.Data, "yyyyMMdd", null, DateTimeStyles.None, out var d)) dtData.Value = d;
            cboTipo.SelectedIndex = mestre?.Tipo == "R" ? 1 : (mestre?.Tipo == "P" ? 2 : 0);
            txtDocFisc.Text = mestre?.DocFisc
                ?? _orig.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.DocFisc))?.DocFisc ?? "";

            // mestre primeiro, depois os detalhes — cada linha leva o original no Tag (preserva Recno/MovId/OutroId)
            foreach (var l in _orig.OrderBy(l => l.OutroId == 0 ? 0 : 1).ThenBy(l => l.Recno))
            {
                int i = dgv.Rows.Add(l.OutroId == 0 ? "PRINCIPAL" : "", l.Debito, l.Credito,
                    l.Valor.ToString("N2", CultureInfo.CurrentCulture), l.Historico);
                dgv.Rows[i].Tag = l;
                if (l.OutroId == 0) dgv.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(235, 242, 252);
            }
            AtualizaBalanco();
        }

        private void Dgv_UserDeletingRow(object sender, DataGridViewRowCancelEventArgs e)
        {
            if (e.Row?.Tag is LancamentoMovfin l && l.Recno != 0)
            {
                if (l.OutroId == 0 && MessageBox.Show(this,
                        "Esta é a linha PRINCIPAL (mestre) do composto. Excluí-la deixa os detalhes órfãos.\nExcluir mesmo assim?",
                        "Atenção", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                { e.Cancel = true; return; }
                Excluidos.Add(l.Recno);
            }
        }

        private static string Cel(DataGridViewRow r, string col) => Convert.ToString(r.Cells[col].Value)?.Trim() ?? "";

        private bool _valorReset;   // true = a próxima tecla na célula de Valor recomeça do zero

        /// <summary>
        /// Máscara de centavos (estilo calculadora) na célula de Valor: cada dígito entra pela
        /// direita e empurra os anteriores p/ a esquerda (1→0,01; 12→0,12; 123→1,23). Backspace
        /// remove o último dígito. Vírgula/ponto/letras são ignorados (o decimal é implícito).
        /// </summary>
        private void ValorKeyPress(object sender, KeyPressEventArgs e)
        {
            var tb = (TextBox)sender;
            char c = e.KeyChar;
            if (char.IsControl(c) && c != '\b') return;   // Enter/Esc/Tab seguem o fluxo normal
            e.Handled = true;
            string digitos = _valorReset ? "" : new string(tb.Text.Where(char.IsDigit).ToArray()).TrimStart('0');
            if (c == '\b') digitos = digitos.Length > 0 ? digitos.Substring(0, digitos.Length - 1) : "";
            else if (c >= '0' && c <= '9') { if (digitos.Length < 15) digitos += c; }
            else return;   // ignora separadores/letras
            _valorReset = false;
            decimal cents = digitos.Length == 0 ? 0m : decimal.Parse(digitos, CultureInfo.InvariantCulture);
            tb.Text = (cents / 100m).ToString("N2", CultureInfo.CurrentCulture);
            tb.SelectionStart = tb.Text.Length;
        }

        /// <summary>Reformata a célula de Valor recém-editada para 2 casas decimais (N2).</summary>
        private void ReformataValor(DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || dgv.Columns[e.ColumnIndex].Name != "Valor") return;
            var r = dgv.Rows[e.RowIndex];
            if (r.IsNewRow) return;
            var txt = Cel(r, "Valor");
            if (txt.Length > 0 && decimal.TryParse(txt, NumberStyles.Any, CultureInfo.CurrentCulture, out var v))
                r.Cells["Valor"].Value = v.ToString("N2", CultureInfo.CurrentCulture);
        }

        private void AtualizaBalanco()
        {
            decimal td = 0, tc = 0;
            foreach (DataGridViewRow r in dgv.Rows)
            {
                if (r.IsNewRow) continue;
                if (!decimal.TryParse(Cel(r, "Valor"), NumberStyles.Any, CultureInfo.CurrentCulture, out var v)) continue;
                if (Cel(r, "Debito").Length > 0) td += v;
                if (Cel(r, "Credito").Length > 0) tc += v;
            }
            var dif = td - tc;
            lblBalanco.Text = $"Débitos: R$ {td:N2}    Créditos: R$ {tc:N2}    Diferença: R$ {dif:N2}";
            lblBalanco.ForeColor = dif == 0 ? Color.Green : Color.Firebrick;
        }

        private void Confirmar()
        {
            dgv.EndEdit();
            if (dgv.IsCurrentCellInEditMode) dgv.CommitEdit(DataGridViewDataErrorContexts.Commit);

            var tipo = cboTipo.SelectedIndex == 1 ? "R" : (cboTipo.SelectedIndex == 2 ? "P" : "");
            bool tpFin = cboTipo.SelectedIndex != 0;
            var data = dtData.Value.ToString("yyyyMMdd");
            var docFisc = txtDocFisc.Text.Trim();

            var result = new List<LancamentoMovfin>();
            foreach (DataGridViewRow r in dgv.Rows)
            {
                if (r.IsNewRow) continue;
                var deb = Cel(r, "Debito");
                var cred = Cel(r, "Credito");
                var hist = Cel(r, "Historico");
                if (deb.Length == 0 && cred.Length == 0) { Aviso("Há uma linha sem débito nem crédito. Preencha ou remova a linha."); return; }
                if (deb.Length > 0 && _plano != null && _plano.Resolver(deb) == null) { Aviso($"Conta de débito não encontrada: {deb}"); return; }
                if (cred.Length > 0 && _plano != null && _plano.Resolver(cred) == null) { Aviso($"Conta de crédito não encontrada: {cred}"); return; }
                if (!decimal.TryParse(Cel(r, "Valor"), NumberStyles.Any, CultureInfo.CurrentCulture, out var v) || v <= 0)
                { Aviso($"Valor inválido na linha (D={deb} C={cred})."); return; }

                var o = r.Tag as LancamentoMovfin;   // original (null = linha nova)
                // GARANTE o vínculo OUTRO_ID ao gravar (o servidor às vezes manda detalhe sem o link):
                // a linha PRINCIPAL fica OUTRO_ID=0; QUALQUER outra linha aponta para o MOV_ID do mestre.
                bool ehMestre = o != null && o.OutroId == 0 && o.MovId == _mestreMovId;
                result.Add(new LancamentoMovfin
                {
                    Recno = o?.Recno ?? 0,
                    MovId = o?.MovId ?? 0,
                    OutroId = ehMestre ? 0m : _mestreMovId,   // detalhe (existente OU novo) sempre ligado ao mestre
                    Data = data, Debito = deb, Credito = cred, Valor = v, Historico = hist,
                    Tipo = tipo, TpFin = tpFin, DocFisc = docFisc,
                    Doc = o?.Doc ?? "", Forn = o?.Forn ?? "", Venc = o?.Venc ?? "",
                    Emissor = o?.Emissor ?? "", DataEmi = o?.DataEmi ?? "",
                });
            }
            if (result.Count == 0) { Aviso("O composto ficou sem linhas."); return; }
            if (!result.Any(l => l.OutroId == 0))
                if (MessageBox.Show(this, "Nenhuma linha PRINCIPAL (mestre) restou no grupo. Salvar assim mesmo?",
                        "Atenção", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            Linhas = result;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "Editar composto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
