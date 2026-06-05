using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Contabil.Core;
using Imobilizado.Dados;

namespace Imobilizado.App
{
    /// <summary>
    /// Diálogo de inclusão/alteração de um lançamento do MOVFIN (partida simples).
    /// Débito/crédito são comboboxes com BUSCA DINÂMICA: digite parte do número, do apelido
    /// (DESC2) ou da descrição e a lista filtra; armazena o DESC2 da conta escolhida.
    /// </summary>
    public sealed class FrmLancamento : Form
    {
        private readonly bool _incluindo;
        private readonly PlanoContas _plano;
        private DateTimePicker dtData, dtVenc, dtEmi;
        private ComboBox cboTipo;
        private ComboBuscaConta cboDeb, cboCred;
        private TextBox txtHist, txtDoc, txtForn, txtDocFisc, txtEmissor;
        private Label lblDebDesc, lblCredDesc, lblEmissorDesc;
        private NumericUpDown numValor;
        private string[] _itensConta;

        public LancamentoMovfin Lancamento { get; private set; }

        public FrmLancamento(LancamentoMovfin existente, PlanoContas plano)
        {
            _incluindo = existente == null;
            _plano = plano;
            Lancamento = existente ?? new LancamentoMovfin { Data = DateTime.Today.ToString("yyyyMMdd") };
            _itensConta = ItensConta();

            Text = _incluindo ? "Incluir lançamento" : $"Alterar lançamento (MOV_ID {Lancamento.MovId:0})";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(500, 514);
            MontarUI();
            Autocomplete.Aplicar(txtEmissor, Autocomplete.DeContas(_plano));
            Preencher();
        }

        /// <summary>Itens "DESC2 — Descrição" de todas as contas analíticas (para busca dinâmica).</summary>
        private string[] ItensConta()
        {
            if (_plano == null) return new string[0];
            return _plano.Contas.Values
                .Where(c => PlanoContas.EhAnalitica(c.NumConta) && !string.IsNullOrWhiteSpace(c.Desc2))
                .Select(c => $"{c.Desc2.Trim()} — {c.Descricao.Trim()}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private void MontarUI()
        {
            int y = 14; const int lx = 14, cx = 130, cw = 340;
            Label L(string t) { var l = new Label { Text = t, Location = new Point(lx, y + 3), AutoSize = true }; Controls.Add(l); return l; }
            void Linha(Control c, int h = 34) { c.Location = new Point(cx, y); if (c.Width < 10) c.Width = cw; Controls.Add(c); y += h; }

            L("Data:"); dtData = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 130 }; Linha(dtData);
            L("Tipo:"); cboTipo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            cboTipo.Items.AddRange(new object[] { "Contábil", "Recebimento (R)", "Pagamento (P)" });
            Linha(cboTipo);

            L("Débito (conta):"); cboDeb = NovaConta(() => lblDebDesc); Linha(cboDeb, 24);
            lblDebDesc = new Label { Location = new Point(cx, y), AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(cw, 0) }; Controls.Add(lblDebDesc); y += 26;
            L("Crédito (conta):"); cboCred = NovaConta(() => lblCredDesc); Linha(cboCred, 24);
            lblCredDesc = new Label { Location = new Point(cx, y), AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(cw, 0) }; Controls.Add(lblCredDesc); y += 26;

            L("Valor (R$):"); numValor = new NumericUpDown { Width = 160, Maximum = 1_000_000_000, DecimalPlaces = 2, ThousandsSeparator = true }; Linha(numValor);
            L("Histórico:"); txtHist = new TextBox { MaxLength = 40, Width = cw }; Linha(txtHist);
            L("Documento:"); txtDoc = new TextBox { MaxLength = 13, Width = 160 }; Linha(txtDoc);
            L("Fornecedor:"); txtForn = new TextBox { MaxLength = 35, Width = cw }; Linha(txtForn);
            L("Vencimento:"); dtVenc = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", ShowCheckBox = true, Checked = false, Width = 130 }; Linha(dtVenc);
            L("Emissor (forn/cli):"); txtEmissor = new TextBox { MaxLength = 8, Width = cw }; txtEmissor.TextChanged += (s, e) => AtualizaDesc(txtEmissor.Text, lblEmissorDesc); Linha(txtEmissor, 24);
            lblEmissorDesc = new Label { Location = new Point(cx, y), AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(cw, 0) }; Controls.Add(lblEmissorDesc); y += 26;
            L("Doc. fiscal:"); txtDocFisc = new TextBox { MaxLength = 13, Width = 160 }; Linha(txtDocFisc);
            L("Data emissão:"); dtEmi = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", ShowCheckBox = true, Checked = false, Width = 130 }; Linha(dtEmi);

            var btnOk = new Button { Text = _incluindo ? "Incluir" : "Salvar", Location = new Point(cx, y + 6), Width = 120 };
            btnOk.Click += (s, e) => Confirmar();
            var btnCancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(cx + 140, y + 6), Width = 120 };
            Controls.AddRange(new Control[] { btnOk, btnCancel });
            CancelButton = btnCancel;
        }

        private ComboBuscaConta NovaConta(Func<Label> lbl)
        {
            var cb = new ComboBuscaConta { Width = 340 };
            cb.Carregar(_itensConta);
            cb.TextChanged += (s, e) => AtualizaDesc(cb.Valor, lbl());
            return cb;
        }

        private void Preencher()
        {
            var l = Lancamento;
            if (DateTime.TryParseExact(l.Data, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d)) dtData.Value = d;
            cboTipo.SelectedIndex = l.Tipo == "R" ? 1 : (l.Tipo == "P" ? 2 : 0);
            cboDeb.Valor = l.Debito; cboCred.Valor = l.Credito;
            numValor.Value = Math.Min(numValor.Maximum, Math.Max(0, l.Valor));
            txtHist.Text = l.Historico; txtDoc.Text = l.Doc; txtForn.Text = l.Forn; txtDocFisc.Text = l.DocFisc;
            txtEmissor.Text = l.Emissor;
            if (DateTime.TryParseExact(l.Venc, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var v)) { dtVenc.Value = v; dtVenc.Checked = true; }
            if (DateTime.TryParseExact(l.DataEmi, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var de)) { dtEmi.Value = de; dtEmi.Checked = true; }
        }

        private void AtualizaDesc(string valor, Label lbl)
        {
            var v = (valor ?? "").Trim();
            if (v.Length == 0) { lbl.Text = ""; return; }
            var nc = _plano?.Resolver(v);
            if (nc != null && _plano.Contas.TryGetValue(nc, out var c))
            { lbl.Text = $"{nc} — {c.Descricao}"; lbl.ForeColor = Color.DimGray; }
            else { lbl.Text = "conta não encontrada"; lbl.ForeColor = Color.Firebrick; }
        }

        private void Confirmar()
        {
            var deb = cboDeb.Valor;
            var cred = cboCred.Valor;
            bool debVazio = deb.Length == 0, credVazio = cred.Length == 0;
            // partida simples = débito + crédito; meia-entrada (partida dobrada) = só um lado.
            if (debVazio && credVazio) { Aviso("Informe a conta de débito e/ou de crédito."); return; }
            if (!debVazio && _plano != null && _plano.Resolver(deb) == null) { Aviso("Conta de débito não encontrada."); return; }
            if (!credVazio && _plano != null && _plano.Resolver(cred) == null) { Aviso("Conta de crédito não encontrada."); return; }
            if (!debVazio && !credVazio && string.Equals(deb, cred, StringComparison.OrdinalIgnoreCase))
            { Aviso("Débito e crédito não podem ser a mesma conta."); return; }
            if (numValor.Value <= 0) { Aviso("Valor deve ser maior que zero."); return; }

            var l = Lancamento;
            l.Data = dtData.Value.ToString("yyyyMMdd");
            l.Tipo = cboTipo.SelectedIndex == 1 ? "R" : (cboTipo.SelectedIndex == 2 ? "P" : "");
            l.TpFin = cboTipo.SelectedIndex != 0;
            l.Debito = deb; l.Credito = cred;
            l.Valor = numValor.Value;
            l.Historico = txtHist.Text.Trim(); l.Doc = txtDoc.Text.Trim();
            l.Forn = txtForn.Text.Trim(); l.DocFisc = txtDocFisc.Text.Trim();
            l.Emissor = txtEmissor.Text.Trim();
            l.Venc = dtVenc.Checked ? dtVenc.Value.ToString("yyyyMMdd") : "";
            l.DataEmi = dtEmi.Checked ? dtEmi.Value.ToString("yyyyMMdd") : "";
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "Lançamento", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
