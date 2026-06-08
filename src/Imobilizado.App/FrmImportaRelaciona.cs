using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Imobilizado.Dados;

namespace Imobilizado.App
{
    /// <summary>
    /// Importa a planilha <b>PESQUISA</b> (.xlsx) e RECRIA o RELACIONA.DBF (de-para NUMCONTA → REDUZIDO/
    /// NOVOCOD do AlterData) — equivale ao FrmRelaciona.btnImporta do Contabil2020. Valida NUMCONTA
    /// único, pula linhas sem NOVOCOD, ordena por NOVOCOD. Faz BACKUP do RELACIONA.DBF atual antes de
    /// sobrescrever (e recria o arquivo limpo, sem o lixo de registros deletados).
    /// </summary>
    public sealed class FrmImportaRelaciona : Form
    {
        private TextBox txtPasta, txtArquivo;
        private Button btnPasta, btnArquivo, btnImportar;
        private Label lblResumo;
        private DataGridView dgv;
        private StatusStrip status;
        private ToolStripStatusLabel statusLabel;

        private List<ItemRelaciona> _itens;
        private List<string> _dups;

        public FrmImportaRelaciona()
        {
            Text = "Importar RELACIONA — planilha PESQUISA";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 540);
            Size = new Size(1020, 620);
            MontarUI();
            txtPasta.Text = ConfigApp.CarregarPastaDados();
        }

        private void MontarUI()
        {
            var topo = new Panel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(8) };
            var lblPasta = new Label { Text = "Pasta de dados:", AutoSize = true, Location = new Point(10, 12) };
            txtPasta = new TextBox { Location = new Point(120, 8), Width = 700 };
            btnPasta = new Button { Text = "...", Location = new Point(826, 7), Width = 34 };
            btnPasta.Click += (s, e) => EscolherPasta();

            var lblArq = new Label { Text = "Planilha PESQUISA:", AutoSize = true, Location = new Point(10, 46) };
            txtArquivo = new TextBox { Location = new Point(120, 42), Width = 700, ReadOnly = true };
            btnArquivo = new Button { Text = "Escolher .xlsx…", Location = new Point(826, 41), Width = 120 };
            btnArquivo.Click += (s, e) => EscolherArquivo();

            btnImportar = new Button { Text = "Importar (recria RELACIONA.DBF)", Location = new Point(120, 68), Width = 240, Enabled = false };
            btnImportar.Click += (s, e) => Importar();

            topo.Controls.AddRange(new Control[] { lblPasta, txtPasta, btnPasta, lblArq, txtArquivo, btnArquivo, btnImportar });

            dgv = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoGenerateColumns = true,
            };

            lblResumo = new Label { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), Font = new Font(Font, FontStyle.Bold) };
            status = new StatusStrip(); statusLabel = new ToolStripStatusLabel("Escolha a planilha PESQUISA."); status.Items.Add(statusLabel);

            Controls.Add(dgv);
            Controls.Add(lblResumo);
            Controls.Add(topo);
            Controls.Add(status);
        }

        private void EscolherPasta()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pasta onde fica o RELACIONA.DBF" })
            {
                if (Directory.Exists(txtPasta.Text)) dlg.SelectedPath = txtPasta.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtPasta.Text = dlg.SelectedPath;
            }
        }

        private sealed class LinhaPrev
        {
            public string NUMCONTA { get; set; }
            public int REDUZIDO { get; set; }
            public string NOVOCOD { get; set; }
            public string DESCRICAO { get; set; }
            public string NOVADESC { get; set; }
        }

        private void EscolherArquivo()
        {
            using (var dlg = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", Title = "Planilha PESQUISA" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                CarregarArquivo(dlg.FileName);
            }
        }

        /// <summary>Lê a planilha, valida e popula o preview (separado p/ permitir teste/captura).</summary>
        internal void CarregarArquivo(string caminho)
        {
            try
            {
                UseWaitCursor = true;
                _itens = PesquisaReader.Ler(caminho);
                txtArquivo.Text = caminho;

                var comCod = _itens.Where(i => !string.IsNullOrWhiteSpace(i.NovoCod)).ToList();
                _dups = new RelacionaGravador(txtPasta.Text.Trim()).NumcontasDuplicados(_itens);

                dgv.DataSource = comCod
                    .OrderBy(i => i.NovoCod.Trim(), StringComparer.Ordinal)
                    .Select(i => new LinhaPrev { NUMCONTA = i.NumConta, REDUZIDO = i.Reduzido, NOVOCOD = i.NovoCod, DESCRICAO = i.Descricao, NOVADESC = i.NovaDesc })
                    .ToList();

                bool ok = _dups.Count == 0 && comCod.Count > 0;
                btnImportar.Enabled = ok;
                lblResumo.Text = $"{_itens.Count} linhas | {comCod.Count} com NOVOCOD (serão gravadas) | {_itens.Count - comCod.Count} sem NOVOCOD (puladas)"
                    + (_dups.Count > 0 ? $"  ⚠ {_dups.Count} NUMCONTA DUPLICADO — corrija antes de importar: {string.Join(", ", _dups.Take(6))}" : "  ✓ pronto para importar");
                lblResumo.ForeColor = _dups.Count > 0 ? Color.Firebrick : Color.Green;
                statusLabel.Text = ok ? "Confira o preview e clique Importar." : "Corrija os problemas antes de importar.";
            }
            catch (Exception ex) { Aviso("Erro ao ler a planilha:\n" + ex.Message); btnImportar.Enabled = false; }
            finally { UseWaitCursor = false; }
        }

        private void Importar()
        {
            if (_itens == null || _itens.Count == 0) { Aviso("Escolha a planilha PESQUISA primeiro."); return; }
            var pasta = txtPasta.Text.Trim();
            if (!Directory.Exists(pasta)) { Aviso("Pasta de dados inválida."); return; }
            var dbf = Path.Combine(pasta, "RELACIONA.DBF");
            var msg = (File.Exists(dbf) ? "Vai SOBRESCREVER o RELACIONA.DBF atual (um backup será criado).\n\n" : "")
                + $"Gravar {_itens.Count(i => !string.IsNullOrWhiteSpace(i.NovoCod))} registros em:\n{dbf}\n\nConfirma?";
            if (MessageBox.Show(this, msg, "Importar RELACIONA", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

            try
            {
                UseWaitCursor = true;
                statusLabel.Text = "Importando…";
                Application.DoEvents();
                var res = new RelacionaGravador(pasta).Recriar(_itens);
                statusLabel.Text = $"OK: {res.Gravados} registros gravados.";
                MessageBox.Show(this,
                    $"RELACIONA.DBF recriado com {res.Gravados} registros (puladas {res.PuladosSemNovocod} sem NOVOCOD)."
                    + (res.CaminhoBackup != null ? $"\n\nBackup do anterior:\n{res.CaminhoBackup}" : ""),
                    "Importação concluída", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { Aviso("Erro ao gravar o RELACIONA.DBF:\n" + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        private void Aviso(string m) => MessageBox.Show(this, m, "Importar RELACIONA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
