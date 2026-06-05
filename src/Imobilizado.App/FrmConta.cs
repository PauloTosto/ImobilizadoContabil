using System;
using System.Drawing;
using System.Windows.Forms;
using Contabil.Core;

namespace Imobilizado.App
{
    /// <summary>
    /// Diálogo de inclusão/alteração de uma conta do PLACON master, com a hierarquia
    /// 1.1.1.2.3: o grau é DERIVADO do número e a conta-pai é validada (precisa existir).
    /// </summary>
    public sealed class FrmConta : Form
    {
        private readonly bool _incluindo;
        private readonly Func<string, bool> _existeConta;
        private TextBox txtNum, txtDesc, txtApelido;
        private NumericUpDown numTaxa;
        private Label lblHier;

        public ContaPlacao Conta { get; private set; }

        public FrmConta(ContaPlacao existente, Func<string, bool> existeConta)
        {
            _incluindo = existente == null;
            _existeConta = existeConta ?? (_ => true);
            Conta = existente ?? new ContaPlacao();

            Text = _incluindo ? "Incluir conta" : $"Alterar conta {Conta.NumConta}";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(460, 280);
            MontarUI();
            Preencher();
        }

        private void MontarUI()
        {
            int y = 16; const int lx = 16, cx = 150, cw = 280;
            Label L(string t) { var l = new Label { Text = t, Location = new Point(lx, y + 3), AutoSize = true }; Controls.Add(l); return l; }
            void Linha(Control c) { c.Location = new Point(cx, y); c.Width = cw; Controls.Add(c); y += 36; }

            L("Número (1.1.1.2.3):"); txtNum = new TextBox { MaxLength = 8 }; txtNum.Enabled = _incluindo;
            txtNum.TextChanged += (s, e) => AtualizarHierarquia();
            Linha(txtNum);
            lblHier = new Label { Location = new Point(cx, y), AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(cw, 0) };
            Controls.Add(lblHier); y += 30;
            L("Descrição:"); txtDesc = new TextBox { MaxLength = 40 }; Linha(txtDesc);
            L("Apelido (DESC2):"); txtApelido = new TextBox { MaxLength = 25 }; Linha(txtApelido);
            L("Taxa deprec. (%):"); numTaxa = new NumericUpDown { Maximum = 100, DecimalPlaces = 2, Increment = 0.5m }; Linha(numTaxa);

            var btnOk = new Button { Text = _incluindo ? "Incluir" : "Salvar", Location = new Point(cx, y + 6), Width = 130 };
            btnOk.Click += (s, e) => Confirmar();
            var btnCancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(cx + 150, y + 6), Width = 130 };
            Controls.AddRange(new Control[] { btnOk, btnCancel });
            CancelButton = btnCancel;
        }

        private void Preencher()
        {
            txtNum.Text = Conta.NumConta; txtDesc.Text = Conta.Descricao; txtApelido.Text = Conta.Apelido;
            numTaxa.Value = Conta.Taxa < 0 ? 0 : (Conta.Taxa > 100 ? 100 : Conta.Taxa);
            AtualizarHierarquia();
        }

        private void AtualizarHierarquia()
        {
            var nc = txtNum.Text.Trim();
            if (!HierarquiaContas.MascaraValida(nc, out var erro))
            {
                lblHier.Text = nc.Length == 0 ? "" : erro;
                lblHier.ForeColor = Color.Firebrick;
                return;
            }
            int nivel = HierarquiaContas.Nivel(nc);
            var pai = HierarquiaContas.ContaPai(nc);
            if (pai == null) { lblHier.Text = $"Nível {nivel} (raiz)."; lblHier.ForeColor = Color.DimGray; return; }
            bool paiExiste = _existeConta(pai);
            lblHier.Text = $"Nível {nivel} — conta-pai {pai}: " + (paiExiste ? "ok" : "NÃO EXISTE (crie-a antes)");
            lblHier.ForeColor = paiExiste ? Color.DimGray : Color.Firebrick;
        }

        private void Confirmar()
        {
            var nc = txtNum.Text.Trim();
            if (!HierarquiaContas.MascaraValida(nc, out var erro)) { Aviso(erro); return; }
            if (txtDesc.Text.Trim().Length == 0) { Aviso("Informe a descrição."); return; }
            var pai = HierarquiaContas.ContaPai(nc);
            if (_incluindo && pai != null && !_existeConta(pai))
            {
                Aviso($"A conta-pai {pai} não existe. Crie a hierarquia de cima para baixo.");
                return;
            }

            Conta.NumConta = nc;
            Conta.Grau = HierarquiaContas.GrauDerivado(nc);   // grau derivado do número
            Conta.Descricao = txtDesc.Text.Trim();
            Conta.Apelido = txtApelido.Text.Trim();
            Conta.Taxa = numTaxa.Value;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "Conta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
