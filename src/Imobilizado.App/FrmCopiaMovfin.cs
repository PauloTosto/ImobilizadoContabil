using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Imobilizado.Dados;

namespace Imobilizado.App
{
    /// <summary>
    /// Utilitário: gera uma CÓPIA do MOVFIN contendo só os registros com DATA dentro do
    /// período informado. A cópia é um DBF novo (estrutura preservada via SELECT…INTO TABLE
    /// do VFP); o MOVFIN de origem não é tocado. Útil para recortes de teste/auditoria.
    /// </summary>
    public sealed class FrmCopiaMovfin : Form
    {
        private TextBox txtPasta, txtDestino;
        private DateTimePicker dtDe, dtAte;
        private Button btnPasta, btnDestino, btnCopiar;
        private Label lblInfo;

        public FrmCopiaMovfin()
        {
            Text = "Utilitários — Copiar MOVFIN por período";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(640, 240);
            MontarUI();
            txtPasta.Text = ConfigApp.CarregarPastaDados();
            SugerirDestino();
        }

        private void MontarUI()
        {
            int y = 14; const int lx = 14, cx = 130;
            Label L(string t) { var l = new Label { Text = t, Location = new Point(lx, y + 3), AutoSize = true }; Controls.Add(l); return l; }

            L("Pasta do MOVFIN:");
            txtPasta = new TextBox { Location = new Point(cx, y), Width = 420 };
            btnPasta = new Button { Text = "...", Location = new Point(556, y - 1), Width = 34 };
            btnPasta.Click += (s, e) => EscolherPasta();
            Controls.AddRange(new Control[] { txtPasta, btnPasta }); y += 34;

            L("Período:");
            dtDe = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 110, Location = new Point(cx, y), Value = new DateTime(DateTime.Today.Year, 1, 1) };
            var lblA = new Label { Text = "a", AutoSize = true, Location = new Point(cx + 118, y + 3) };
            dtAte = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 110, Location = new Point(cx + 138, y), Value = DateTime.Today };
            dtDe.ValueChanged += (s, e) => SugerirDestino();
            dtAte.ValueChanged += (s, e) => SugerirDestino();
            Controls.AddRange(new Control[] { lblA, dtDe, dtAte }); y += 34;

            L("Arquivo de destino:");
            txtDestino = new TextBox { Location = new Point(cx, y), Width = 420 };
            btnDestino = new Button { Text = "...", Location = new Point(556, y - 1), Width = 34 };
            btnDestino.Click += (s, e) => EscolherDestino();
            Controls.AddRange(new Control[] { txtDestino, btnDestino }); y += 34;

            lblInfo = new Label { Location = new Point(cx, y), AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(480, 0),
                Text = "A cópia contém só os registros com DATA dentro do período.\nO MOVFIN de origem não é alterado." };
            Controls.Add(lblInfo); y += 44;

            btnCopiar = new Button { Text = "Copiar", Location = new Point(cx, y), Width = 130, Height = 30 };
            btnCopiar.Click += (s, e) => Copiar();
            var btnFechar = new Button { Text = "Fechar", DialogResult = DialogResult.Cancel, Location = new Point(cx + 140, y), Width = 110, Height = 30 };
            Controls.AddRange(new Control[] { btnCopiar, btnFechar });
            CancelButton = btnFechar;
        }

        private void SugerirDestino()
        {
            var pasta = txtPasta.Text.Trim();
            if (pasta.Length == 0) return;
            txtDestino.Text = Path.Combine(pasta, $"MOVFIN_{dtDe.Value:yyyyMMdd}_{dtAte.Value:yyyyMMdd}.DBF");
        }

        private void EscolherPasta()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pasta onde está o MOVFIN.DBF" })
            {
                if (Directory.Exists(txtPasta.Text)) dlg.SelectedPath = txtPasta.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) { txtPasta.Text = dlg.SelectedPath; SugerirDestino(); }
            }
        }

        private void EscolherDestino()
        {
            using (var dlg = new SaveFileDialog
            {
                Filter = "Tabela DBF (*.DBF)|*.DBF", DefaultExt = "DBF", AddExtension = true, OverwritePrompt = false,
                FileName = Path.GetFileName(txtDestino.Text),
                InitialDirectory = Directory.Exists(Path.GetDirectoryName(txtDestino.Text)) ? Path.GetDirectoryName(txtDestino.Text) : txtPasta.Text.Trim(),
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) txtDestino.Text = dlg.FileName;
            }
        }

        private void Copiar()
        {
            var pasta = txtPasta.Text.Trim();
            if (!File.Exists(Path.Combine(pasta, "MOVFIN.DBF"))) { Aviso("Não encontrei o MOVFIN.DBF na pasta de origem."); return; }
            var destino = txtDestino.Text.Trim();
            if (destino.Length == 0) { Aviso("Informe o arquivo de destino."); return; }
            if (dtDe.Value.Date > dtAte.Value.Date) { Aviso("Período inválido (início depois do fim)."); return; }
            if (destino.EndsWith(".DBF", StringComparison.OrdinalIgnoreCase)) destino = destino.Substring(0, destino.Length - 4);

            if (File.Exists(destino + ".DBF") &&
                MessageBox.Show(this, $"O arquivo já existe e será SOBRESCRITO:\n{destino}.DBF\n\nContinuar?",
                    "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                UseWaitCursor = true;
                int n = new MovfinGravador(pasta).CopiarPeriodo(destino, dtDe.Value.ToString("yyyyMMdd"), dtAte.Value.ToString("yyyyMMdd"));
                lblInfo.ForeColor = Color.Green;
                lblInfo.Text = $"Copiados {n:N0} registros para {destino}.DBF";
                MessageBox.Show(this, $"Cópia gerada com {n:N0} registros do período {dtDe.Value:dd/MM/yyyy} a {dtAte.Value:dd/MM/yyyy}:\n\n{destino}.DBF",
                    "Concluído", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { Aviso("Erro ao copiar:\n" + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "Copiar MOVFIN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
