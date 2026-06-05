using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Imobilizado.Core;
using Imobilizado.Core.Dbf;
using Imobilizado.Core.Dominio;
using Imobilizado.Dados;

namespace Imobilizado.App
{
    /// <summary>
    /// Tela principal: escolher pasta de dados + competência, ver os bens, pré-visualizar
    /// o lote de depreciação do mês (dry-run na grade) e gravar no MOVFIN.
    /// UI construída em código (sem Designer) para facilitar manutenção.
    /// </summary>
    public sealed class FrmPrincipal : Form
    {
        private TextBox txtPasta;
        private NumericUpDown numAno, numMes;
        private Button btnPasta, btnCarregar;
        private TabControl tabs;
        private DataGridView dgvBens, dgvLanc;
        private Label lblResumoBens, lblResumoLanc;
        private CheckBox chkSubstituir;
        private Button btnGravar;
        private StatusStrip status;
        private ToolStripStatusLabel statusLabel;

        private Button btnIncluir, btnAlterar;

        // estado carregado
        private List<Bem> _bens;
        private List<BemEdicao> _bensEdicao;
        private Dictionary<string, decimal> _taxas;
        private MotorDepreciacao _motor;
        private IReadOnlyList<LancamentoDepreciacao> _lancamentosPreview;

        public FrmPrincipal()
        {
            Text = "Imobilizado / Depreciação — Contabilidade";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 560);
            Size = new Size(1040, 640);
            MontarUI();
            CarregarConfig();
        }

        private void MontarUI()
        {
            // ----- painel de topo -----
            var topo = new Panel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(8) };

            var lblPasta = new Label { Text = "Pasta de dados (DBFs):", AutoSize = true, Location = new Point(10, 12) };
            txtPasta = new TextBox { Location = new Point(150, 8), Width = 560, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            btnPasta = new Button { Text = "...", Location = new Point(716, 7), Width = 34, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnPasta.Click += (s, e) => EscolherPasta();

            var lblComp = new Label { Text = "Competência:", AutoSize = true, Location = new Point(10, 46) };
            numAno = new NumericUpDown { Location = new Point(150, 42), Width = 70, Minimum = 1990, Maximum = 2100, Value = Math.Min(2100, Math.Max(1990, DateTime.Today.Year)) };
            var lblBarra = new Label { Text = "/", AutoSize = true, Location = new Point(226, 46) };
            numMes = new NumericUpDown { Location = new Point(240, 42), Width = 50, Minimum = 1, Maximum = 12, Value = DateTime.Today.Month };
            btnCarregar = new Button { Text = "Carregar / Atualizar", Location = new Point(310, 41), Width = 150 };
            btnCarregar.Click += (s, e) => CarregarTudo();

            var btnAprop = new Button { Text = "Apropriações…", Location = new Point(470, 41), Width = 120 };
            btnAprop.Click += (s, e) => { using (var f = new FrmApropriacao()) f.ShowDialog(this); };
            var btnPlacon = new Button { Text = "Plano de contas…", Location = new Point(600, 41), Width = 130 };
            btnPlacon.Click += (s, e) => { using (var f = new FrmPlacon()) f.ShowDialog(this); };
            var btnLanc = new Button { Text = "Lançamentos…", Location = new Point(740, 41), Width = 120 };
            btnLanc.Click += (s, e) => { using (var f = new FrmLancamentos()) f.ShowDialog(this); };

            topo.Controls.AddRange(new Control[] { lblPasta, txtPasta, btnPasta, lblComp, numAno, lblBarra, numMes, btnCarregar, btnAprop, btnPlacon, btnLanc });

            // ----- abas -----
            tabs = new TabControl { Dock = DockStyle.Fill };

            var tabBens = new TabPage("Bens");
            dgvBens = NovaGrade();
            dgvBens.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) AlterarBem(); };
            var toolBens = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 4, 6, 4) };
            btnIncluir = new Button { Text = "&Incluir bem", Location = new Point(6, 5), Width = 100, Enabled = false };
            btnIncluir.Click += (s, e) => IncluirBem();
            btnAlterar = new Button { Text = "&Alterar / Baixar", Location = new Point(112, 5), Width = 120, Enabled = false };
            btnAlterar.Click += (s, e) => AlterarBem();
            toolBens.Controls.AddRange(new Control[] { btnIncluir, btnAlterar });
            lblResumoBens = new Label { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };
            tabBens.Controls.Add(dgvBens);
            tabBens.Controls.Add(toolBens);
            tabBens.Controls.Add(lblResumoBens);

            var tabDep = new TabPage("Depreciação do mês");
            dgvLanc = NovaGrade();
            var painelDep = new Panel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(6) };
            lblResumoLanc = new Label { Location = new Point(6, 6), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            chkSubstituir = new CheckBox { Text = "Substituir lançamentos já existentes do mês", Location = new Point(6, 32), AutoSize = true };
            btnGravar = new Button { Text = "Gravar no MOVFIN", Location = new Point(380, 24), Width = 170, Height = 30, Enabled = false };
            btnGravar.Click += (s, e) => Gravar();
            painelDep.Controls.AddRange(new Control[] { lblResumoLanc, chkSubstituir, btnGravar });
            tabDep.Controls.Add(dgvLanc);
            tabDep.Controls.Add(painelDep);

            tabs.TabPages.Add(tabBens);
            tabs.TabPages.Add(tabDep);

            // ----- status -----
            status = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("Pronto.");
            status.Items.Add(statusLabel);

            Controls.Add(tabs);
            Controls.Add(topo);
            Controls.Add(status);
        }

        private static DataGridView NovaGrade() => new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            AutoGenerateColumns = true,
        };

        private void CarregarConfig()
        {
            var p = ConfigApp.CarregarPastaDados();
            if (!string.IsNullOrWhiteSpace(p)) txtPasta.Text = p;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Se a pasta lembrada já tem as DBFs, carrega de cara (só leitura).
            var p = txtPasta.Text.Trim();
            if (Directory.Exists(p) && File.Exists(Path.Combine(p, "IMOBIL.DBF")) && File.Exists(Path.Combine(p, "placon.DBF")))
                CarregarTudo();
        }

        private void EscolherPasta()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Selecione a pasta com IMOBIL.DBF, placon.DBF e MOVFIN.DBF" })
            {
                if (!string.IsNullOrWhiteSpace(txtPasta.Text) && Directory.Exists(txtPasta.Text))
                    dlg.SelectedPath = txtPasta.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    txtPasta.Text = dlg.SelectedPath;
            }
        }

        private AnoMes Competencia() => new AnoMes((int)numAno.Value, (int)numMes.Value);

        private void CarregarTudo()
        {
            var pasta = txtPasta.Text.Trim();
            if (!Directory.Exists(pasta)) { Aviso("Pasta inválida."); return; }
            var imobil = Path.Combine(pasta, "IMOBIL.DBF");
            var placon = Path.Combine(pasta, "placon.DBF");
            if (!File.Exists(imobil) || !File.Exists(placon))
            {
                Aviso("Não encontrei IMOBIL.DBF e/ou placon.DBF nessa pasta.");
                return;
            }

            try
            {
                UseWaitCursor = true;
                _bensEdicao = CadastroDbf.CarregarBensEdicao(imobil);
                _bens = _bensEdicao.ConvertAll(be => be.ParaBem());
                _taxas = CadastroDbf.CarregarTaxas(placon);
                _motor = new MotorDepreciacao(g => _taxas.TryGetValue(g, out var t) ? t : 0m);
                ConfigApp.SalvarPastaDados(pasta);

                AtualizarGradeBens();
                AtualizarPreviewDepreciacao();
                btnIncluir.Enabled = btnAlterar.Enabled = true;
                statusLabel.Text = $"Carregado de {pasta} — {_bens.Count} bens.";
            }
            catch (Exception ex)
            {
                Aviso("Erro ao carregar dados:\n" + ex.Message);
            }
            finally { UseWaitCursor = false; }
        }

        private void AtualizarGradeBens()
        {
            var comp = Competencia();
            var linhas = _bens.Select(b =>
            {
                var quotaMes = _motor.QuotaMensal(b);
                var quotaComp = _motor.QuotaDoMes(b, comp);
                return new BemLinha
                {
                    Codigo = b.Codigo,
                    Descricao = b.Descricao,
                    Conta = b.ContaImobilizado,
                    Grupo = b.ContaGrupo(),
                    Taxa = _taxas.TryGetValue(b.ContaGrupo(), out var t) ? t : 0m,
                    Base = b.BaseDepreciavel,
                    DepInicial = b.DepreciacaoInicial,
                    Partida = b.DataPartida?.ToString() ?? "",
                    QuotaMes = decimal.Round(quotaMes, 2),
                    QuotaCompetencia = decimal.Round(quotaComp, 2),
                    Situacao = Situacao(b, comp, quotaMes, quotaComp),
                };
            }).ToList();

            dgvBens.DataSource = linhas;
            FormatarColunasBens();
            int ativos = linhas.Count(l => l.Situacao == "Ativo");
            lblResumoBens.Text = $"{linhas.Count} bens | {ativos} depreciando em {comp} | base total: {linhas.Sum(l => l.Base):N2}";
        }

        private string Situacao(Bem b, AnoMes comp, decimal quotaMes, decimal quotaComp)
        {
            if (b.DataBaixa is AnoMes bx && bx.EhAnteriorA(comp)) return "Baixado";
            if (quotaMes <= 0) return "Sem taxa";
            if (quotaComp <= 0) return "Esgotado";
            return "Ativo";
        }

        private void AtualizarPreviewDepreciacao()
        {
            var comp = Competencia();
            _lancamentosPreview = _motor.GerarLancamentos(_bens, comp);
            var view = _lancamentosPreview.Select(l => new
            {
                Data = l.Data,
                Debito = l.Debito,
                Credito = l.Credito,
                Valor = l.Valor,
                Historico = l.Historico,
            }).ToList();
            dgvLanc.DataSource = view;
            decimal total = _lancamentosPreview.Sum(l => l.Valor);
            lblResumoLanc.Text = $"Competência {comp}: {_lancamentosPreview.Count} lançamentos — total R$ {total:N2}  (pré-visualização, nada gravado)";
            btnGravar.Enabled = _lancamentosPreview.Count > 0;
        }

        private void FormatarColunasBens()
        {
            void H(string nome, string titulo, string fmt = null, bool dir = false)
            {
                if (!dgvBens.Columns.Contains(nome)) return;
                var c = dgvBens.Columns[nome];
                c.HeaderText = titulo;
                if (fmt != null) c.DefaultCellStyle.Format = fmt;
                if (dir) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            H("Codigo", "Código"); H("Descricao", "Descrição"); H("Conta", "Conta");
            H("Grupo", "Grupo"); H("Taxa", "Taxa%", "N2", true); H("Base", "Base", "N2", true);
            H("DepInicial", "Dep.inicial", "N2", true); H("Partida", "Partida");
            H("QuotaMes", "Quota/mês", "N2", true); H("QuotaCompetencia", "Quota compet.", "N2", true);
            H("Situacao", "Situação");
        }

        private void Gravar()
        {
            if (_lancamentosPreview == null || _lancamentosPreview.Count == 0) return;
            var pasta = txtPasta.Text.Trim();
            if (!File.Exists(Path.Combine(pasta, "MOVFIN.DBF"))) { Aviso("MOVFIN.DBF não encontrado na pasta."); return; }

            var comp = Competencia();
            decimal total = _lancamentosPreview.Sum(l => l.Valor);
            var msg = $"Gravar {_lancamentosPreview.Count} lançamentos de depreciação da competência {comp} " +
                      $"(total R$ {total:N2}) no MOVFIN?\n\nPasta: {pasta}" +
                      (chkSubstituir.Checked ? "\n\nOs lançamentos SIST_IMOB já existentes deste mês serão EXCLUÍDOS antes." : "");
            if (MessageBox.Show(this, msg, "Confirmar gravação", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            try
            {
                UseWaitCursor = true;
                var gravador = new MovfinGravador(pasta);
                int jaExistem = gravador.ContarDepreciacaoExistente(comp);
                if (jaExistem > 0 && !chkSubstituir.Checked)
                {
                    Aviso($"Já existem {jaExistem} lançamentos SIST_IMOB em {comp}.\n" +
                          "Marque \"Substituir\" para reprocessar o mês.");
                    return;
                }
                int n = gravador.Gravar(comp, _lancamentosPreview, gravarDeFato: true, substituirExistentes: chkSubstituir.Checked);
                statusLabel.Text = $"Gravados {n} lançamentos em {comp}.";
                MessageBox.Show(this, $"Gravados {n} lançamentos no MOVFIN" +
                                      (chkSubstituir.Checked ? $" (após excluir {jaExistem})." : "."),
                                "Concluído", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Aviso("Erro ao gravar:\n" + ex.Message);
            }
            finally { UseWaitCursor = false; }
        }

        private void IncluirBem()
        {
            if (_bensEdicao == null) return;
            using (var dlg = new FrmBem(null, Taxa))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var pasta = txtPasta.Text.Trim();
                try
                {
                    var grav = new ImobilGravador(pasta);
                    if (grav.Existe(dlg.Bem.Codigo)) { Aviso($"Já existe um bem com código {dlg.Bem.Codigo}."); return; }
                    grav.Incluir(dlg.Bem);
                    statusLabel.Text = $"Bem {dlg.Bem.Codigo} incluído.";
                    CarregarTudo();
                }
                catch (Exception ex) { Aviso("Erro ao incluir:\n" + ex.Message); }
            }
        }

        private void AlterarBem()
        {
            if (_bensEdicao == null) return;
            var cod = CodigoSelecionado();
            if (cod == null) { Aviso("Selecione um bem na grade."); return; }
            var atual = _bensEdicao.Find(b => b.Codigo == cod);
            if (atual == null) return;

            // edita uma cópia para não alterar o estado em memória se cancelar
            var copia = Clonar(atual);
            using (var dlg = new FrmBem(copia, Taxa))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var pasta = txtPasta.Text.Trim();
                try
                {
                    new ImobilGravador(pasta).Alterar(dlg.Bem);
                    statusLabel.Text = $"Bem {dlg.Bem.Codigo} alterado.";
                    CarregarTudo();
                }
                catch (Exception ex) { Aviso("Erro ao alterar:\n" + ex.Message); }
            }
        }

        private decimal Taxa(string grupo) => _taxas != null && _taxas.TryGetValue(grupo, out var t) ? t : 0m;

        private string CodigoSelecionado()
        {
            if (dgvBens.CurrentRow?.DataBoundItem is BemLinha l) return l.Codigo;
            return null;
        }

        private static BemEdicao Clonar(BemEdicao b) => new BemEdicao
        {
            Codigo = b.Codigo, Descricao = b.Descricao, ContaImobilizado = b.ContaImobilizado,
            ContaDepAcumulada = b.ContaDepAcumulada, ContaResultado = b.ContaResultado,
            ValorAquisicao = b.ValorAquisicao, DataAquisicao = b.DataAquisicao, DataCorrecao = b.DataCorrecao,
            BaseDepreciavel = b.BaseDepreciavel, DepreciacaoInicial = b.DepreciacaoInicial,
            DataBaixa = b.DataBaixa, ValorBaixa = b.ValorBaixa,
        };

        private void Aviso(string m) => MessageBox.Show(this, m, "Imobilizado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
