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

            // Os módulos abrem NÃO-MODAIS (Show): o menu continua acessível e o usuário pode manter
            // várias janelas abertas lado a lado p/ comparar (ex.: Exportar Lote + Lançamentos).
            // Se o módulo já está aberto, só traz pra frente (não duplica).
            var mImob = M("&Imobilizado");
            It(mImob, "Bens / Depreciação…", (s, e) => Abrir<FrmImobilizado>());

            var mCont = M("&Contabilidade");
            It(mCont, "Lançamentos…", (s, e) => Abrir<FrmLancamentos>());
            It(mCont, "Apropriações…", (s, e) => Abrir<FrmApropriacao>());
            It(mCont, "Plano de Contas…", (s, e) => Abrir<FrmPlacon>());
            It(mCont, "Balancete…", (s, e) => Abrir<FrmBalancete>());

            var mAlter = M("&AlterData");
            It(mAlter, "Exportar Lote…", (s, e) => Abrir<FrmExportaAlterData>());
            It(mAlter, "Importar RELACIONA (planilha PESQUISA)…", (s, e) => Abrir<FrmImportaRelaciona>());

            var mUtil = M("&Utilitários");
            It(mUtil, "Copiar MOVFIN por período…", (s, e) => Abrir<FrmCopiaMovfin>());

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

        /// <summary>Abre um módulo NÃO-modal; se já existe uma janela desse tipo, só a traz pra frente.</summary>
        private void Abrir<T>() where T : Form, new()
        {
            foreach (Form f in Application.OpenForms)
                if (f is T)
                {
                    if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
                    f.BringToFront();
                    f.Activate();
                    return;
                }
            var nf = new T();
            nf.Show(this);   // não-modal, com owner: fica acima da principal, fecha junto com o app
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
