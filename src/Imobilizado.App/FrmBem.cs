using System;
using System.Drawing;
using System.Windows.Forms;
using Imobilizado.Core.Dominio;

namespace Imobilizado.App
{
    /// <summary>Diálogo de inclusão/alteração/baixa de um bem do imobilizado.</summary>
    public sealed class FrmBem : Form
    {
        private readonly bool _incluindo;
        private readonly Func<string, decimal> _taxaPorGrupo;

        private TextBox txtCod, txtDesc, txtConta, txtDepAcum, txtResultado;
        private Label lblTaxa;
        private NumericUpDown numBase, numDepIni, numValAquis, numValBaixa;
        private DateTimePicker dtAquis, dtCorr, dtBaixa;

        public BemEdicao Bem { get; private set; }

        public FrmBem(BemEdicao existente, Func<string, decimal> taxaPorGrupo)
        {
            _incluindo = existente == null;
            _taxaPorGrupo = taxaPorGrupo ?? (g => 0m);
            Bem = existente ?? new BemEdicao();

            Text = _incluindo ? "Incluir bem" : $"Alterar bem {Bem.Codigo}";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(470, 520);
            MontarUI();
            PreencherDe(Bem);
        }

        private void MontarUI()
        {
            int y = 16; const int lx = 16, cx = 170, cw = 280;
            Label L(string t) { var l = new Label { Text = t, Location = new Point(lx, y + 3), AutoSize = true }; Controls.Add(l); return l; }
            void Linha(Control c) { c.Location = new Point(cx, y); c.Width = cw; Controls.Add(c); y += 34; }

            L("Código:"); txtCod = new TextBox { MaxLength = 5, CharacterCasing = CharacterCasing.Upper }; txtCod.Enabled = _incluindo; Linha(txtCod);
            L("Descrição:"); txtDesc = new TextBox { MaxLength = 35 }; Linha(txtDesc);
            L("Conta imobilizado:"); txtConta = new TextBox { MaxLength = 8 }; txtConta.TextChanged += (s, e) => AtualizaTaxa(); Linha(txtConta);
            lblTaxa = new Label { Location = new Point(cx, y), AutoSize = true, ForeColor = Color.DimGray }; Controls.Add(lblTaxa); y += 26;
            L("Conta dep. acumulada:"); txtDepAcum = new TextBox { MaxLength = 8 }; Linha(txtDepAcum);
            L("Conta resultado (despesa):"); txtResultado = new TextBox { MaxLength = 8 }; Linha(txtResultado);

            L("Base depreciável (R$):"); numBase = NovoNum(); Linha(numBase);
            L("Depreciação inicial (R$):"); numDepIni = NovoNum(); Linha(numDepIni);
            L("Valor aquisição (R$):"); numValAquis = NovoNum(); Linha(numValAquis);

            L("Data aquisição:"); dtAquis = NovoData(); Linha(dtAquis);
            L("Data correção/partida:"); dtCorr = NovoData(); Linha(dtCorr);

            var grpBaixa = new GroupBox { Text = "Baixa (opcional)", Location = new Point(lx, y), Size = new Size(cw + cx - lx, 84) };
            var lb1 = new Label { Text = "Data baixa:", Location = new Point(10, 26), AutoSize = true };
            dtBaixa = new DateTimePicker { Location = new Point(150, 22), Width = 130, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", ShowCheckBox = true, Checked = false };
            var lb2 = new Label { Text = "Valor baixa (R$):", Location = new Point(10, 54), AutoSize = true };
            numValBaixa = new NumericUpDown { Location = new Point(150, 50), Width = 130, Maximum = 1_000_000_000, DecimalPlaces = 2, ThousandsSeparator = true };
            grpBaixa.Controls.AddRange(new Control[] { lb1, dtBaixa, lb2, numValBaixa });
            Controls.Add(grpBaixa);
            y += 96;

            var btnOk = new Button { Text = _incluindo ? "Incluir" : "Salvar", DialogResult = DialogResult.None, Location = new Point(cx, y), Width = 130 };
            btnOk.Click += (s, e) => Confirmar();
            var btnCancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(cx + 150, y), Width = 130 };
            Controls.AddRange(new Control[] { btnOk, btnCancel });
            CancelButton = btnCancel;
        }

        private static NumericUpDown NovoNum() => new NumericUpDown { Maximum = 1_000_000_000, DecimalPlaces = 2, ThousandsSeparator = true };
        private static DateTimePicker NovoData() => new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", ShowCheckBox = true, Checked = false };

        private void AtualizaTaxa()
        {
            var c = txtConta.Text.Trim();
            var grupo = c.Length >= 5 ? c.Substring(0, 5) + "000" : c;
            var taxa = _taxaPorGrupo(grupo);
            lblTaxa.Text = string.IsNullOrEmpty(c) ? "" :
                taxa > 0 ? $"grupo {grupo} — {taxa:N2}% a.a." : $"grupo {grupo} — sem taxa (não deprecia)";
            lblTaxa.ForeColor = taxa > 0 ? Color.DimGray : Color.Firebrick;
        }

        private void PreencherDe(BemEdicao b)
        {
            txtCod.Text = b.Codigo; txtDesc.Text = b.Descricao; txtConta.Text = b.ContaImobilizado;
            txtDepAcum.Text = b.ContaDepAcumulada; txtResultado.Text = b.ContaResultado;
            numBase.Value = Clamp(b.BaseDepreciavel); numDepIni.Value = Clamp(b.DepreciacaoInicial);
            numValAquis.Value = Clamp(b.ValorAquisicao);
            SetData(dtAquis, b.DataAquisicao); SetData(dtCorr, b.DataCorrecao); SetData(dtBaixa, b.DataBaixa);
            numValBaixa.Value = Clamp(b.ValorBaixa);
            AtualizaTaxa();
        }

        private static decimal Clamp(decimal v) => v < 0 ? 0 : (v > 1_000_000_000 ? 1_000_000_000 : v);
        private static void SetData(DateTimePicker dt, DateTime? v) { if (v.HasValue) { dt.Value = v.Value; dt.Checked = true; } else dt.Checked = false; }
        private static DateTime? GetData(DateTimePicker dt) => dt.Checked ? dt.Value.Date : (DateTime?)null;

        private void Confirmar()
        {
            var cod = txtCod.Text.Trim();
            if (cod.Length == 0) { Aviso("Informe o código."); return; }
            if (txtConta.Text.Trim().Length == 0) { Aviso("Informe a conta imobilizado."); return; }
            if (txtDepAcum.Text.Trim().Length == 0 || txtResultado.Text.Trim().Length == 0)
            { Aviso("Informe as contas de depreciação acumulada e de resultado."); return; }

            Bem.Codigo = cod;
            Bem.Descricao = txtDesc.Text.Trim();
            Bem.ContaImobilizado = txtConta.Text.Trim();
            Bem.ContaDepAcumulada = txtDepAcum.Text.Trim();
            Bem.ContaResultado = txtResultado.Text.Trim();
            Bem.BaseDepreciavel = numBase.Value;
            Bem.DepreciacaoInicial = numDepIni.Value;
            Bem.ValorAquisicao = numValAquis.Value;
            Bem.DataAquisicao = GetData(dtAquis);
            Bem.DataCorrecao = GetData(dtCorr);
            Bem.DataBaixa = GetData(dtBaixa);
            Bem.ValorBaixa = numValBaixa.Value;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "Bem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
