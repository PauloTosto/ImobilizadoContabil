using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Imobilizado.App
{
    /// <summary>
    /// Janela principal: SÓ o menu (Arquivo / Imobilizado / Contabilidade / AlterData) + status.
    /// Nenhum módulo carrega na abertura — o Imobilizado (bens/depreciação) virou o
    /// <see cref="FrmImobilizado"/> e se comporta como os demais: abre só quando chamado.
    /// </summary>
    public sealed class FrmPrincipal : Form
    {
        private MenuStrip menu;
        private StatusStrip status;
        private ToolStripStatusLabel statusLabel;

        public FrmPrincipal()
        {
            Text = "Contabilidade / Imobilizado — M.Libanio";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(720, 420);
            Size = new Size(860, 520);
            MontarUI();
        }

        private void MontarUI()
        {
            menu = new MenuStrip();
            ToolStripMenuItem M(string txt) { var m = new ToolStripMenuItem(txt); menu.Items.Add(m); return m; }
            void It(ToolStripMenuItem pai, string txt, EventHandler ev, Keys atalho = Keys.None)
            {
                var it = new ToolStripMenuItem(txt) { ShortcutKeys = atalho };
                it.Click += ev; pai.DropDownItems.Add(it);
            }

            var mArq = M("&Arquivo");
            It(mArq, "Escolher pasta de dados…", (s, e) => EscolherPasta());
            mArq.DropDownItems.Add(new ToolStripSeparator());
            It(mArq, "Sair", (s, e) => Close());

            var mImob = M("&Imobilizado");
            It(mImob, "Bens / Depreciação…", (s, e) => { using (var f = new FrmImobilizado()) f.ShowDialog(this); });

            var mCont = M("&Contabilidade");
            It(mCont, "Lançamentos…", (s, e) => { using (var f = new FrmLancamentos()) f.ShowDialog(this); });
            It(mCont, "Apropriações…", (s, e) => { using (var f = new FrmApropriacao()) f.ShowDialog(this); });
            It(mCont, "Plano de Contas…", (s, e) => { using (var f = new FrmPlacon()) f.ShowDialog(this); });
            It(mCont, "Balancete…", (s, e) => { using (var f = new FrmBalancete()) f.ShowDialog(this); });

            var mAlter = M("&AlterData");
            It(mAlter, "Exportar Lote…", (s, e) => { using (var f = new FrmExportaAlterData()) f.ShowDialog(this); });
            It(mAlter, "Importar RELACIONA (planilha PESQUISA)…", (s, e) => { using (var f = new FrmImportaRelaciona()) f.ShowDialog(this); });

            // painel central com a pasta de dados em uso (informativo)
            var centro = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.DimGray,
                Font = new Font(Font.FontFamily, 10f),
            };
            centro.Paint += (s, e) => { };   // sem conteúdo extra
            AtualizaCentro(centro);

            status = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("Pronto. Use o menu para abrir os módulos.");
            status.Items.Add(statusLabel);

            Controls.Add(centro);
            Controls.Add(status);
            Controls.Add(menu);
            MainMenuStrip = menu;
        }

        private void AtualizaCentro(Label centro)
        {
            var p = ConfigApp.CarregarPastaDados();
            centro.Text = string.IsNullOrWhiteSpace(p)
                ? "Nenhuma pasta de dados configurada.\nUse Arquivo → Escolher pasta de dados…"
                : $"Pasta de dados:\n{p}";
        }

        private void EscolherPasta()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Selecione a pasta com as DBFs (IMOBIL, placon, MOVFIN, BANCOS, RELACIONA…)" })
            {
                var atual = ConfigApp.CarregarPastaDados();
                if (!string.IsNullOrWhiteSpace(atual) && Directory.Exists(atual)) dlg.SelectedPath = atual;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                ConfigApp.SalvarPastaDados(dlg.SelectedPath);
                statusLabel.Text = $"Pasta de dados: {dlg.SelectedPath}";
                foreach (Control c in Controls) if (c is Label l && l.Dock == DockStyle.Fill) AtualizaCentro(l);
            }
        }
    }
}
