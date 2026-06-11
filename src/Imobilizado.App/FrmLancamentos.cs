using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Contabil.Core;
using Imobilizado.Dados;

namespace Imobilizado.App
{
    /// <summary>
    /// Editor de lançamentos do MOVFIN: lista por período (e tipo), e permite incluir,
    /// alterar e excluir lançamentos manuais (partida simples). Réplica unificada dos
    /// forms FLT_1 (financeiro) e FLT_2 (contábil) do Clipper.
    /// </summary>
    public sealed class FrmLancamentos : Form
    {
        private TextBox txtPasta, txtFiltro;
        private DateTimePicker dtDe, dtAte;
        private ComboBox cboTipo, cboContab;
        private CheckBox chkAbertos, chkTransf;
        private Button btnPasta, btnCarregar, btnIncluir, btnAlterar, btnExcluir, btnComposto;
        private DataGridView dgv;
        private Label lblResumo;
        private StatusStrip status;
        private ToolStripStatusLabel statusLabel;

        private List<LancamentoMovfin> _lancamentos = new List<LancamentoMovfin>();
        private PlanoContas _plano;
        private string _ordCol = "Data";
        private bool _ordAsc = true;

        public FrmLancamentos()
        {
            Text = "Lançamentos do MOVFIN — Contabilidade";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(940, 560);
            Size = new Size(1080, 640);
            MontarUI();
            txtPasta.Text = ConfigApp.CarregarPastaDados();
        }

        private void MontarUI()
        {
            var topo = new Panel { Dock = DockStyle.Top, Height = 104, Padding = new Padding(8) };
            var lblPasta = new Label { Text = "Pasta de dados:", AutoSize = true, Location = new Point(10, 12) };
            txtPasta = new TextBox { Location = new Point(120, 8), Width = 600, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            btnPasta = new Button { Text = "...", Location = new Point(726, 7), Width = 34, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnPasta.Click += (s, e) => EscolherPasta();

            var hoje = DateTime.Today;
            var ini = new DateTime(hoje.Year, hoje.Month, 1);
            var lblP = new Label { Text = "Período:", AutoSize = true, Location = new Point(10, 46) };
            dtDe = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 110, Location = new Point(120, 42), Value = ini };
            var lblAte = new Label { Text = "a", AutoSize = true, Location = new Point(238, 46) };
            dtAte = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 110, Location = new Point(258, 42), Value = ini.AddMonths(1).AddDays(-1) };
            var lblT = new Label { Text = "Tipo:", AutoSize = true, Location = new Point(380, 46) };
            cboTipo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Location = new Point(420, 42) };
            cboTipo.Items.AddRange(new object[] { "Todos", "Contábil", "Financeiro" }); cboTipo.SelectedIndex = 0;
            btnCarregar = new Button { Text = "Carregar", Location = new Point(585, 41), Width = 100 };
            btnCarregar.Click += (s, e) => Carregar();
            chkAbertos = new CheckBox { Text = "Só compostos que não fecham", AutoSize = true, Location = new Point(120, 76) };
            chkAbertos.CheckedChanged += (s, e) => Exibir();
            chkTransf = new CheckBox { Text = "Só transferências bancárias", AutoSize = true, Location = new Point(330, 76) };
            chkTransf.CheckedChanged += (s, e) => Exibir();
            topo.Controls.AddRange(new Control[] { lblPasta, txtPasta, btnPasta, lblP, dtDe, lblAte, dtAte, lblT, cboTipo, btnCarregar, chkAbertos, chkTransf });

            var barra = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8, 4, 8, 4) };
            btnIncluir = new Button { Text = "&Incluir", Location = new Point(8, 5), Width = 78, Enabled = false };
            btnIncluir.Click += (s, e) => Incluir();
            btnComposto = new Button { Text = "Composto…", Location = new Point(90, 5), Width = 90, Enabled = false };
            btnComposto.Click += (s, e) => IncluirComposto();
            btnAlterar = new Button { Text = "&Alterar", Location = new Point(184, 5), Width = 78, Enabled = false };
            btnAlterar.Click += (s, e) => Alterar();
            btnExcluir = new Button { Text = "&Excluir", Location = new Point(266, 5), Width = 78, Enabled = false };
            btnExcluir.Click += (s, e) => Excluir();
            var lblF = new Label { Text = "Filtrar:", AutoSize = true, Location = new Point(360, 9) };
            txtFiltro = new TextBox { Location = new Point(408, 6), Width = 180 };
            txtFiltro.TextChanged += (s, e) => Exibir();
            var lblC = new Label { Text = "Contabilidade:", AutoSize = true, Location = new Point(600, 9) };
            cboContab = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175, Location = new Point(688, 5) };
            cboContab.Items.AddRange(new object[] { "Todos", "Válidos p/ contab.", "Pendentes (inválidos)" });
            cboContab.SelectedIndex = 0;
            cboContab.SelectedIndexChanged += (s, e) => Exibir();
            barra.Controls.AddRange(new Control[] { btnIncluir, btnComposto, btnAlterar, btnExcluir, lblF, txtFiltro, lblC, cboContab });

            dgv = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoGenerateColumns = true,
            };
            dgv.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) Alterar(); };
            dgv.ColumnHeaderMouseClick += (s, e) => OrdenarPor(dgv.Columns[e.ColumnIndex].Name);

            lblResumo = new Label { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };
            status = new StatusStrip(); statusLabel = new ToolStripStatusLabel("Pronto."); status.Items.Add(statusLabel);

            Controls.Add(dgv);
            Controls.Add(barra);
            Controls.Add(topo);
            Controls.Add(lblResumo);
            Controls.Add(status);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (File.Exists(Path.Combine(txtPasta.Text.Trim(), "MOVFIN.DBF"))) Carregar();
        }

        private void EscolherPasta()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pasta com MOVFIN.DBF e placon.DBF" })
            {
                if (Directory.Exists(txtPasta.Text)) dlg.SelectedPath = txtPasta.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtPasta.Text = dlg.SelectedPath;
            }
        }

        private MovfinGravador Gravador() => new MovfinGravador(txtPasta.Text.Trim());

        private void Carregar()
        {
            var pasta = txtPasta.Text.Trim();
            if (!File.Exists(Path.Combine(pasta, "MOVFIN.DBF")) || !File.Exists(Path.Combine(pasta, "placon.DBF")))
            { Aviso("Pasta precisa conter MOVFIN.DBF e placon.DBF."); return; }
            try
            {
                UseWaitCursor = true;
                _plano = PlanoContas.Carregar(Path.Combine(pasta, "placon.DBF"));
                bool? tp = cboTipo.SelectedIndex == 1 ? false : (cboTipo.SelectedIndex == 2 ? (bool?)true : null);
                _lancamentos = Gravador().LerPeriodo(dtDe.Value.ToString("yyyyMMdd"), dtAte.Value.ToString("yyyyMMdd"), tp);
                Exibir();
                btnIncluir.Enabled = btnComposto.Enabled = btnAlterar.Enabled = btnExcluir.Enabled = true;
                statusLabel.Text = $"Carregado de {pasta}.";
            }
            catch (Exception ex) { Aviso("Erro ao carregar:\n" + ex.Message); }
            finally { UseWaitCursor = false; }
        }

        private void Exibir()
        {
            var f = (txtFiltro?.Text ?? "").Trim();
            // filtro por VALOR: se o texto digitado é um número, casa também pelo valor exato (ex.: "11680" ou "11.680,00")
            decimal? fValor = decimal.TryParse(f, NumberStyles.Any, CultureInfo.CurrentCulture, out var fv) ? fv : (decimal?)null;
            bool Casa(LancamentoMovfin l) => f.Length == 0
                || (l.Debito ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                || (l.Credito ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                || (l.Historico ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                || (l.Doc ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                || (l.DocFisc ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                || (l.Emissor ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                || (l.Forn ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                || (fValor.HasValue && decimal.Round(l.Valor, 2) == decimal.Round(fValor.Value, 2));
            int modoContab = cboContab?.SelectedIndex ?? 0;   // 0=todos, 1=válidos, 2=pendentes
            bool CasaContab(LancamentoMovfin l) => modoContab == 0
                || (modoContab == 1 ? ValidoContab(l) : !ValidoContab(l));
            var difGrupos = GruposNaoFecham(out var fechamViaNf);
            var transfStatus = AnalisarTransferencias();
            bool soAbertos = chkAbertos?.Checked ?? false;
            bool soTransf = chkTransf?.Checked ?? false;
            // "não fecha" p/ exibição: membro de grupo aberto OU a peça órfã que completa via DOC_FISC
            bool Aberto(LancamentoMovfin l) => NaoFecha(l, difGrupos) || EhComplementoNf(l);
            // status do grupo: fecha via doc.fiscal (vínculo OUTRO_ID quebrado, mas a NF completa) ou não fecha mesmo
            string StatusGrupo(LancamentoMovfin l)
            {
                if (EhComplementoNf(l)) return "órfã (completa via doc.fiscal)";
                if (!NaoFecha(l, difGrupos)) return "";
                var k = l.OutroId != 0 ? (l.OutroId, l.Data) : (l.MovId, l.Data);
                return fechamViaNf.Contains(k) ? "fecha via doc.fiscal" : "NÃO fecha";
            }
            var filtrados = Ordenar(_lancamentos.Where(l => Casa(l) && CasaContab(l)
                && (!soAbertos || Aberto(l))
                && (!soTransf || transfStatus.ContainsKey(l.Recno))).ToList());
            var view = filtrados.Select(l => new
            {
                l.Recno,
                Cont = ValidoContab(l) ? "✓" : "✗",   // válido para a contabilidade?
                Data = Fmt(l.Data),
                l.Debito,
                l.Credito,
                l.Valor,
                Tipo = l.TpFin ? (l.Tipo == "R" ? "Receb." : l.Tipo == "P" ? "Pagto" : "Fin.") : "Contábil",
                Historico = l.Historico,
                l.Doc,
                l.Forn,
                l.Emissor,
                DocFisc = l.DocFisc,
                Grupo = StatusGrupo(l),               // status do composto (vazio / fecha via NF / NÃO fecha / órfã)
                NaoFecha = Aberto(l),                 // (oculta) → pinta a linha
                Espelho = transfStatus.TryGetValue(l.Recno, out var es) ? (es.Length == 0 ? "ok" : es) : "",
                TransfProb = transfStatus.TryGetValue(l.Recno, out var ep) && ep.Length > 0,   // (oculta) transferência com problema
            }).ToList();
            dgv.DataSource = view;
            if (dgv.Columns.Contains("Recno")) dgv.Columns["Recno"].Visible = false;
            // larguras proporcionais: Débito/Crédito/Histórico (campos longos) ganham mais espaço
            void Larg(string n, int w) { if (dgv.Columns.Contains(n)) dgv.Columns[n].FillWeight = w; }
            Larg("Cont", 7); Larg("Data", 14); Larg("Debito", 26); Larg("Credito", 26); Larg("Valor", 15);
            Larg("Tipo", 11); Larg("Historico", 36); Larg("Doc", 12); Larg("Forn", 14); Larg("Emissor", 12); Larg("DocFisc", 12);
            if (dgv.Columns.Contains("Cont")) { dgv.Columns["Cont"].HeaderText = "Cont."; dgv.Columns["Cont"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter; dgv.Columns["Cont"].ToolTipText = "Válido para a contabilidade (✓) ou pendente (✗)"; }
            if (dgv.Columns.Contains("Valor")) { dgv.Columns["Valor"].DefaultCellStyle.Format = "N2"; dgv.Columns["Valor"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
            foreach (DataGridViewColumn c in dgv.Columns)
            {
                c.SortMode = DataGridViewColumnSortMode.Programmatic;
                c.HeaderCell.SortGlyphDirection = SortOrder.None;
            }
            if (dgv.Columns.Contains(_ordCol))
                dgv.Columns[_ordCol].HeaderCell.SortGlyphDirection = _ordAsc ? SortOrder.Ascending : SortOrder.Descending;
            if (dgv.Columns.Contains("NaoFecha")) dgv.Columns["NaoFecha"].Visible = false;
            if (dgv.Columns.Contains("TransfProb")) dgv.Columns["TransfProb"].Visible = false;
            if (dgv.Columns.Contains("Espelho")) { dgv.Columns["Espelho"].HeaderText = "Espelho"; dgv.Columns["Espelho"].Visible = soTransf; dgv.Columns["Espelho"].FillWeight = 26; }
            if (dgv.Columns.Contains("Grupo")) { dgv.Columns["Grupo"].HeaderText = "Composto"; dgv.Columns["Grupo"].Visible = soAbertos; dgv.Columns["Grupo"].FillWeight = 30; }
            // composto que não fecha OU transferência sem espelho → laranja; pendente p/ contab. → vermelho-claro
            foreach (DataGridViewRow row in dgv.Rows)
            {
                var item = row.DataBoundItem;
                bool naoFecha = (item?.GetType().GetProperty("NaoFecha")?.GetValue(item) as bool?) ?? false;
                bool transfProb = (item?.GetType().GetProperty("TransfProb")?.GetValue(item) as bool?) ?? false;
                var cont = item?.GetType().GetProperty("Cont")?.GetValue(item) as string;
                if (naoFecha || transfProb) row.DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 205);   // laranja-claro
                else if (cont == "✗") row.DefaultCellStyle.BackColor = Color.FromArgb(255, 233, 233);
            }
            int validos = filtrados.Count(ValidoContab);
            decimal totDeb = filtrados.Where(l => !string.IsNullOrWhiteSpace(l.Debito)).Sum(l => l.Valor);
            decimal totCred = filtrados.Where(l => !string.IsNullOrWhiteSpace(l.Credito)).Sum(l => l.Valor);
            int qtdTransfProb = transfStatus.Count(kv => kv.Value.Length > 0);
            int soDocFisc = fechamViaNf.Count;
            string aviso = difGrupos.Count > 0
                ? $"  |  ⚠ {difGrupos.Count} composto(s) não fecham por OUTRO_ID"
                  + (soDocFisc > 0 ? $" ({soDocFisc} fecham via doc.fiscal)" : "")
                  + $" (Σ |dif| R$ {difGrupos.Values.Sum(Math.Abs):N2})"
                : "";
            if (soTransf) aviso += $"  |  {transfStatus.Count} transferência(s)" + (qtdTransfProb > 0 ? $", ⚠ {qtdTransfProb} com espelho que NÃO casa" : " — todas casam ✓");
            else if (qtdTransfProb > 0) aviso += $"  |  ⚠ {qtdTransfProb} transferência(s) com espelho que não casa";
            lblResumo.Text = (f.Length > 0 || modoContab != 0 || soAbertos || soTransf ? $"{filtrados.Count} de {_lancamentos.Count} lançamentos" : $"{_lancamentos.Count} lançamentos")
                + $" | válidos p/ contab.: {validos}, pendentes: {filtrados.Count - validos}"
                + $" | Débitos R$ {totDeb:N2}   Créditos R$ {totCred:N2}" + aviso;
            lblResumo.ForeColor = (difGrupos.Count > 0 || qtdTransfProb > 0) ? Color.Firebrick : SystemColors.ControlText;
        }

        /// <summary>True se o lançamento é válido para a contabilidade (contas resolvem no PLACON / banco→CONTAB analítico).</summary>
        private bool ValidoContab(LancamentoMovfin l) => _plano != null && _plano.ValidoParaContabilidade(l.Debito, l.Credito);

        /// <summary>
        /// Calcula, por grupo COMPOSTO (chave = MOV_ID do mestre + DATA), a diferença Débito−Crédito.
        /// Retorna só os grupos que NÃO fecham (diferença ≠ 0) → ajuda a achar erro nos dados do servidor.
        /// Um grupo = o mestre (OUTRO_ID=0, MOV_ID=k) + os detalhes (OUTRO_ID=k), todos na MESMA data.
        /// </summary>
        /// <summary>Grupos compostos que não fecham por OUTRO_ID. Valor>0/<0 = diferença; o set
        /// <paramref name="fechamViaDocFisc"/> recebe as chaves cuja diferença SOME quando se inclui
        /// as meias-entradas soltas do MESMO (DOC_FISC, data) — ou seja, o vínculo OUTRO_ID está
        /// quebrado mas a nota fiscal completa o grupo (caso típico de dado vindo do servidor).</summary>
        private Dictionary<(decimal mov, string data), decimal> GruposNaoFecham(out HashSet<(decimal, string)> fechamViaDocFisc)
        {
            fechamViaDocFisc = new HashSet<(decimal, string)>();
            _nfComplemento.Clear();
            // chaves de composto = (MOV_ID do mestre, data) que tenham ao menos um detalhe (OUTRO_ID != 0)
            var comDetalhe = new HashSet<(decimal, string)>();
            foreach (var l in _lancamentos)
                if (l.OutroId != 0) comDetalhe.Add((l.OutroId, l.Data));
            var res = new Dictionary<(decimal, string), decimal>();
            if (comDetalhe.Count == 0) return res;

            // acumula débito/crédito por grupo numa passada (mestre conta só se a chave tiver detalhe);
            // guarda também o DocFisc dominante do grupo p/ o fallback
            var bal = new Dictionary<(decimal, string), (decimal td, decimal tc, string docFisc)>();
            foreach (var l in _lancamentos)
            {
                (decimal, string) k = l.OutroId != 0 ? (l.OutroId, l.Data) : (l.MovId, l.Data);
                if (!comDetalhe.Contains(k)) continue;
                bal.TryGetValue(k, out var b);
                if (!string.IsNullOrWhiteSpace(l.Debito)) b.td += l.Valor;
                if (!string.IsNullOrWhiteSpace(l.Credito)) b.tc += l.Valor;
                if (string.IsNullOrWhiteSpace(b.docFisc) && !string.IsNullOrWhiteSpace(l.DocFisc)) b.docFisc = l.DocFisc.Trim();
                bal[k] = b;
            }

            // meias-entradas SOLTAS (fora de qualquer grupo) por (DocFisc, data) — candidatas do fallback
            var soltasPorNf = new Dictionary<(string, string), (decimal td, decimal tc)>();
            foreach (var l in _lancamentos)
            {
                if (l.OutroId != 0 || comDetalhe.Contains((l.MovId, l.Data))) continue;   // já está num grupo
                if (string.IsNullOrWhiteSpace(l.DocFisc)) continue;
                bool meiaD = !string.IsNullOrWhiteSpace(l.Debito) && string.IsNullOrWhiteSpace(l.Credito);
                bool meiaC = string.IsNullOrWhiteSpace(l.Debito) && !string.IsNullOrWhiteSpace(l.Credito);
                if (!meiaD && !meiaC) continue;   // só meia-entrada serve de complemento
                var k = (l.DocFisc.Trim(), l.Data);
                soltasPorNf.TryGetValue(k, out var b);
                if (meiaD) b.td += l.Valor; else b.tc += l.Valor;
                soltasPorNf[k] = b;
            }

            foreach (var kv in bal)
            {
                var dif = decimal.Round(kv.Value.td - kv.Value.tc, 2);
                if (dif == 0) continue;
                res[kv.Key] = dif;
                // FALLBACK: não fechou por OUTRO_ID → tenta completar com as soltas do mesmo (DOC_FISC, data)
                if (!string.IsNullOrWhiteSpace(kv.Value.docFisc)
                    && soltasPorNf.TryGetValue((kv.Value.docFisc, kv.Key.Item2), out var s)
                    && decimal.Round(kv.Value.td + s.td - kv.Value.tc - s.tc, 2) == 0)
                {
                    fechamViaDocFisc.Add(kv.Key);
                    _nfComplemento.Add((kv.Value.docFisc, kv.Key.Item2));   // p/ exibir as peças órfãs junto
                }
            }
            return res;
        }

        /// <summary>(DOC_FISC, data) dos grupos que fecham via nota fiscal — as meias-entradas soltas
        /// com essa chave são as "peças órfãs" e aparecem junto no filtro de compostos.</summary>
        private readonly HashSet<(string, string)> _nfComplemento = new HashSet<(string, string)>();

        /// <summary>True se é meia-entrada SOLTA que complementa um grupo via DOC_FISC (peça órfã).</summary>
        private bool EhComplementoNf(LancamentoMovfin l)
            => l.OutroId == 0 && !string.IsNullOrWhiteSpace(l.DocFisc)
               && (string.IsNullOrWhiteSpace(l.Debito) ^ string.IsNullOrWhiteSpace(l.Credito))
               && _nfComplemento.Contains((l.DocFisc.Trim(), l.Data));

        private static bool NaoFecha(LancamentoMovfin l, Dictionary<(decimal, string), decimal> dif)
            => l.OutroId != 0 ? dif.ContainsKey((l.OutroId, l.Data)) : dif.ContainsKey((l.MovId, l.Data));

        /// <summary>
        /// Analisa as TRANSFERÊNCIAS bancárias (espelho financeiro: um lado é código de banco 2-díg,
        /// o outro é o DESC2 de um banco). Cada transferência são 2 registros espelho com MESMO
        /// (bancos, valor, data), com as representações cód/DESC2 trocadas. Retorna o status por RECNO
        /// (só transferências): "" = par espelho OK; senão o motivo do problema. Quem não é
        /// transferência não entra no dicionário.
        /// </summary>
        private Dictionary<int, string> AnalisarTransferencias()
        {
            var res = new Dictionary<int, string>();
            if (_plano == null) return res;
            bool Len2(string s) => (s ?? "").Trim().Length == 2;
            bool EhD2(string s) => _plano.EhBancoContabilDesc2((s ?? "").Trim());
            bool EhTransf(LancamentoMovfin l) => (Len2(l.Debito) && EhD2(l.Credito)) || (Len2(l.Credito) && EhD2(l.Debito));
            string Norm(string s) => Len2(s) ? _plano.NBancoDesc2((s ?? "").Trim()).Trim() : (s ?? "").Trim();

            var transf = _lancamentos.Where(EhTransf).ToList();
            if (transf.Count == 0) return res;

            // índices p/ diagnosticar o motivo do registro órfão
            var porBancoValorData = transf.GroupBy(l => (Norm(l.Debito), Norm(l.Credito), decimal.Round(l.Valor, 2), l.Data)).ToDictionary(g => g.Key, g => g.ToList());
            var porBancoValor = new HashSet<(string, string, decimal)>(transf.Select(l => (Norm(l.Debito), Norm(l.Credito), decimal.Round(l.Valor, 2))));
            var porBanco = transf.GroupBy(l => (Norm(l.Debito), Norm(l.Credito))).ToDictionary(g => g.Key, g => g.Count());

            foreach (var l in transf)
            {
                var kExato = (Norm(l.Debito), Norm(l.Credito), decimal.Round(l.Valor, 2), l.Data);
                var par = porBancoValorData[kExato];
                bool espelhoOk = par.Count == 2 && par.Any(x => Len2(x.Debito)) && par.Any(x => Len2(x.Credito));
                if (espelhoOk) { res[l.Recno] = ""; continue; }   // par espelho perfeito
                // órfão: deduz o motivo
                if (par.Count >= 2) res[l.Recno] = "Espelho duplicado/igual";
                else if (CountValorOutraData(transf, Norm, l) > 0) res[l.Recno] = "Data diverge";
                else if (porBanco[(Norm(l.Debito), Norm(l.Credito))] >= 2) res[l.Recno] = "Valor diverge";
                else res[l.Recno] = "Sem espelho";
            }
            return res;
        }

        /// <summary>Conta quantos OUTROS registros têm o mesmo par de bancos + valor, mas DATA diferente (= data divergente).</summary>
        private static int CountValorOutraData(List<LancamentoMovfin> transf, Func<string, string> norm, LancamentoMovfin l)
        {
            var nd = norm(l.Debito); var nc = norm(l.Credito); var v = decimal.Round(l.Valor, 2);
            return transf.Count(x => !ReferenceEquals(x, l) && norm(x.Debito) == nd && norm(x.Credito) == nc
                                     && decimal.Round(x.Valor, 2) == v && x.Data != l.Data);
        }

        private void OrdenarPor(string col)
        {
            if (string.IsNullOrEmpty(col)) return;
            if (col == _ordCol) _ordAsc = !_ordAsc; else { _ordCol = col; _ordAsc = true; }
            Exibir();
        }

        private List<LancamentoMovfin> Ordenar(List<LancamentoMovfin> src)
        {
            var s = StringComparer.OrdinalIgnoreCase;
            IEnumerable<LancamentoMovfin> q;
            switch (_ordCol)
            {
                case "MovId": q = src.OrderBy(l => l.MovId); break;
                case "Valor": q = src.OrderBy(l => l.Valor); break;
                case "Debito": q = src.OrderBy(l => l.Debito, s); break;
                case "Credito": q = src.OrderBy(l => l.Credito, s); break;
                case "Historico": q = src.OrderBy(l => l.Historico, s); break;
                case "Doc": q = src.OrderBy(l => l.Doc, s); break;
                case "Forn": q = src.OrderBy(l => l.Forn, s); break;
                case "Emissor": q = src.OrderBy(l => l.Emissor, s); break;
                case "DocFisc": q = src.OrderBy(l => l.DocFisc, s); break;
                case "Tipo": q = src.OrderBy(l => l.TpFin).ThenBy(l => l.Tipo, s); break;
                default: q = src.OrderBy(l => l.Data, StringComparer.Ordinal); break; // Data
            }
            var lista = q.ToList();
            if (!_ordAsc) lista.Reverse();
            return lista;
        }

        private static string Fmt(string yyyymmdd)
            => DateTime.TryParseExact(yyyymmdd, "yyyyMMdd", null, DateTimeStyles.None, out var d) ? d.ToString("dd/MM/yyyy") : yyyymmdd;

        private LancamentoMovfin Selecionado()
        {
            var item = dgv.CurrentRow?.DataBoundItem;
            var rec = item?.GetType().GetProperty("Recno")?.GetValue(item);
            if (rec == null) return null;
            var r = Convert.ToInt32(rec);
            return _lancamentos.Find(l => l.Recno == r);   // RECNO é único (MOV_ID não é!)
        }

        private void Incluir()
        {
            using (var dlg = new FrmLancamento(null, _plano))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { Gravador().InserirLancamento(dlg.Lancamento); statusLabel.Text = "Lançamento incluído."; Carregar(); }
                catch (Exception ex) { Aviso("Erro ao incluir:\n" + ex.Message); }
            }
        }

        /// <summary>
        /// MOV_ID do mestre se o lançamento faz parte de um grupo composto; senão 0.
        /// Consulta o BANCO (não só o período carregado) para detectar mestres cujos
        /// detalhes estejam em outro período.
        /// </summary>
        private decimal MestreDoGrupo(LancamentoMovfin sel)
        {
            if (sel.OutroId != 0)   // é detalhe → o mestre é OUTRO_ID, MAS só se existir no MESMO DIA (senão é órfão/artefato → trata avulso)
            {
                if (_lancamentos.Exists(x => x.MovId == sel.OutroId && x.OutroId == 0 && x.Data == sel.Data)) return sel.OutroId;
                return Gravador().MestreExiste(sel.OutroId, sel.Data) ? sel.OutroId : 0m;
            }
            if (sel.MovId == 0) return 0;                                      // MOV_ID=0 não é mestre de nada
            if (_lancamentos.Exists(x => x.OutroId == sel.MovId && x.Data == sel.Data)) return sel.MovId;  // mestre com detalhes no período (rápido)
            if (Gravador().TemDetalhes(sel.MovId, sel.Data)) return sel.MovId; // mestre com detalhes fora do período (consulta o banco, ancorado na data)
            return 0;
        }

        private void IncluirComposto()
        {
            using (var dlg = new FrmLancamentoComposto(null, null, _plano))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { Gravador().InserirComposto(dlg.Mestre, dlg.Detalhes); statusLabel.Text = "Lançamento composto incluído."; Carregar(); }
                catch (Exception ex) { Aviso("Erro ao incluir composto:\n" + ex.Message); }
            }
        }

        private void Alterar()
        {
            var sel = Selecionado();
            if (sel == null) { Aviso("Selecione um lançamento."); return; }

            var masterId = MestreDoGrupo(sel);
            if (masterId != 0) { AlterarComposto(masterId, sel.Data); return; }   // partida dobrada → editor composto (ancorado na data)

            var copia = Clonar(sel);
            using (var dlg = new FrmLancamento(copia, _plano))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { Gravador().AlterarLancamento(dlg.Lancamento); statusLabel.Text = $"Lançamento {sel.MovId:0} alterado."; Carregar(); }
                catch (Exception ex) { Aviso("Erro ao alterar:\n" + ex.Message); }
            }
        }

        private void AlterarComposto(decimal masterId, string dataAnchor)
        {
            try
            {
                var grupo = Gravador().LerGrupo(masterId, dataAnchor);
                if (grupo.Count == 0) { Aviso("Grupo do composto não encontrado."); return; }
                using (var dlg = new FrmGrupoComposto(grupo, _plano))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    var g = Gravador();
                    // edição DIRETA: atualiza cada linha pelo RECNO (preserva estrutura), exclui as removidas, insere as novas
                    foreach (var rec in dlg.Excluidos) g.ExcluirLancamento(rec);
                    int alt = 0, ins = 0;
                    foreach (var l in dlg.Linhas)
                    {
                        if (l.Recno != 0) { g.AlterarLancamento(l); alt++; }   // UPDATE WHERE RECNO()=?
                        else { g.InserirLancamento(l); ins++; }                // linha nova (já vem com OUTRO_ID do mestre)
                    }
                    statusLabel.Text = $"Composto {masterId:0}: {alt} linha(s) alterada(s), {ins} nova(s), {dlg.Excluidos.Count} excluída(s).";
                    Carregar();
                }
            }
            catch (Exception ex) { Aviso("Erro ao alterar composto:\n" + ex.Message); }
        }

        private void Excluir()
        {
            var sel = Selecionado();
            if (sel == null) { Aviso("Selecione um lançamento."); return; }

            var masterId = MestreDoGrupo(sel);
            if (masterId != 0)
            {
                int n = 1 + _lancamentos.Count(x => x.OutroId == masterId && x.Data == sel.Data);
                if (MessageBox.Show(this, $"O lançamento faz parte de um COMPOSTO (mestre {masterId:0}, {Fmt(sel.Data)}, ~{n} linhas).\nExcluir o grupo inteiro?",
                                    "Confirmar exclusão", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                try { Gravador().ExcluirComposto(masterId, sel.Data); statusLabel.Text = $"Composto {masterId:0} excluído."; Carregar(); }
                catch (Exception ex) { Aviso("Erro ao excluir composto:\n" + ex.Message); }
                return;
            }

            if (MessageBox.Show(this, $"Excluir o lançamento MOV_ID {sel.MovId:0}\n({Fmt(sel.Data)} D={sel.Debito} C={sel.Credito} R$ {sel.Valor:N2})?",
                                "Confirmar exclusão", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { Gravador().ExcluirLancamento(sel.Recno); statusLabel.Text = $"Lançamento excluído (reg {sel.Recno})."; Carregar(); }
            catch (Exception ex) { Aviso("Erro ao excluir:\n" + ex.Message); }
        }

        private static LancamentoMovfin Clonar(LancamentoMovfin l) => new LancamentoMovfin
        {
            Recno = l.Recno, MovId = l.MovId, OutroId = l.OutroId, Data = l.Data, Debito = l.Debito, Credito = l.Credito, Valor = l.Valor,
            Historico = l.Historico, Doc = l.Doc, Forn = l.Forn, Tipo = l.Tipo, TpFin = l.TpFin, Venc = l.Venc,
            DocFisc = l.DocFisc, Emissor = l.Emissor, DataEmi = l.DataEmi,
        };

        private void Aviso(string m) => MessageBox.Show(this, m, "Lançamentos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
