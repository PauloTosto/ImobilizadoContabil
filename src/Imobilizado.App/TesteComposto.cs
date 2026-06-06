using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Imobilizado.Dados;

namespace Imobilizado.App
{
    /// <summary>
    /// Teste headless do NOVO editor direto de composto (FrmGrupoComposto): edita o VALOR do
    /// principal de forma independente + o valor de um detalhe, salva por RECNO (UPDATE no
    /// lugar) e relê para conferir que cada valor foi gravado como digitado. Aponta para CÓPIA.
    /// </summary>
    internal static class TesteComposto
    {
        public static void Rodar(string pasta, decimal mestreMovId)
        {
            var caminho = System.IO.Path.Combine(pasta, "_teste_saida.txt");
            var log = new System.Text.StringBuilder();
            Action<string> P = s => { Console.WriteLine(s); log.AppendLine(s); System.IO.File.WriteAllText(caminho, log.ToString()); };
            try { RodarInterno(pasta, mestreMovId == 0 ? 74052m : mestreMovId, P); }
            catch (Exception ex) { P("EXCEÇÃO: " + ex); }
        }

        /// <summary>Valida export Excel do balancete + detecção/inserção de contas novas no PTPLA (CÓPIA).</summary>
        public static void TestaExcelNovas(string pasta, string d1, string d2)
        {
            var caminho = System.IO.Path.Combine(pasta, "_teste_excel.txt");
            var log = new System.Text.StringBuilder();
            Action<string> P = s => { Console.WriteLine(s); log.AppendLine(s); System.IO.File.WriteAllText(caminho, log.ToString()); };
            try
            {
                var f = new FrmBalancete();
                var _h = f.Handle;
                var tp = typeof(FrmBalancete);
                var bf = BindingFlags.NonPublic | BindingFlags.Instance;
                T Campo<T>(string n) => (T)tp.GetField(n, bf).GetValue(f);
                Campo<TextBox>("txtPasta").Text = pasta;
                Campo<DateTimePicker>("dtDe").Value = new DateTime(int.Parse(d1.Substring(0, 4)), int.Parse(d1.Substring(4, 2)), int.Parse(d1.Substring(6, 2)));
                Campo<DateTimePicker>("dtAte").Value = new DateTime(int.Parse(d2.Substring(0, 4)), int.Parse(d2.Substring(4, 2)), int.Parse(d2.Substring(6, 2)));
                tp.GetMethod("Calcular", bf).Invoke(f, null);

                var novas = (System.Collections.Generic.List<string>)tp.GetMethod("NovasComMovimento", bf).Invoke(f, null);
                P($"=== Contas novas (placon, fora do PTPLA{int.Parse(d2.Substring(0, 4)) - 1}) COM movimento: {novas.Count} ===");
                foreach (var nc in novas.Take(10)) P("   " + nc);

                var linhas = tp.GetMethod("MontarLinhas", bf).Invoke(f, null);
                var xlsx = System.IO.Path.Combine(pasta, "balancete_teste.xlsx");
                tp.GetMethod("GravarExcel", bf).Invoke(f, new object[] { xlsx, linhas });
                var fi = new System.IO.FileInfo(xlsx);
                P($"\n.xlsx gerado: existe={fi.Exists}, {fi.Length} bytes");
                using (var pkg = new OfficeOpenXml.ExcelPackage(fi))
                {
                    var ws = pkg.Workbook.Worksheets["Balancete"];
                    P($"   Dimensão: {ws.Dimension?.Address}");
                    P($"   A1: {ws.Cells[1, 1].Value}");
                    P($"   Cabeçalhos: {string.Join(" | ", System.Linq.Enumerable.Range(1, 6).Select(c => ws.Cells[3, c].Value))}");
                    P($"   1ª linha: {string.Join(" | ", System.Linq.Enumerable.Range(1, 6).Select(c => ws.Cells[4, c].Value))}");
                }

                // testa o INSERT no PTPLA via VFPOLEDB (na CÓPIA) com uma conta fictícia que certamente não existe
                var pg = new PtplaGravador(pasta, int.Parse(d2.Substring(0, 4)) - 1);
                var alvo = novas.Count > 0 ? novas[0] : "19999991";
                bool antes = pg.Existe(alvo);
                bool ins = pg.InserirConta(alvo, "5", "TESTE CONTA NOVA", "TST # NOVA", d1);
                bool depois = pg.Existe(alvo);
                bool ins2 = pg.InserirConta(alvo, "5", "TESTE", "TST # NOVA", d1);   // 2ª vez deve recusar (já existe)
                P($"\nPtplaGravador.InserirConta({alvo}): existia_antes={antes}, inseriu={ins}, existe_agora={depois}, reinserir={ins2} (esperado false)");
                f.Dispose();
            }
            catch (Exception ex) { P("EXCEÇÃO: " + ex); }
        }

        /// <summary>Valida o balancete: confere débito=crédito e mostra os grupos de 1º nível.</summary>
        public static void TestaBalancete(string pasta, string d1, string d2)
        {
            var caminho = System.IO.Path.Combine(pasta, "_teste_balancete.txt");
            var log = new System.Text.StringBuilder();
            Action<string> P = s => { Console.WriteLine(s); log.AppendLine(s); System.IO.File.WriteAllText(caminho, log.ToString()); };
            try
            {
                int ano = int.Parse(d2.Substring(0, 4));
                var ptpla = System.IO.Path.Combine(pasta, $"PTPLA{ano - 1}.DBF");
                var plano = Contabil.Core.PlanoContas.Carregar(System.IO.Path.Combine(pasta, "placon.DBF"), ptpla);
                var eng = new Contabil.Core.EngineSaldo(plano);
                var ap = eng.ApurarPeriodoComRollup(System.IO.Path.Combine(pasta, "MOVFIN.DBF"), d1, d2);
                decimal td = 0, tc = 0;
                foreach (var kv in ap) if (Contabil.Core.HierarquiaContas.EhAnalitica(kv.Key)) { td += kv.Value.Val2; tc += kv.Value.Val3; }
                P($"=== Balancete {d1}..{d2} (âncora PTPLA{ano - 1}) ===");
                P($"Total Débitos:  {td:N2}");
                P($"Total Créditos: {tc:N2}");
                P($"Diferença: {td - tc:N2}  -> {(decimal.Round(td - tc, 2) == 0 ? "CONFERE" : "NÃO CONFERE")}");
                P("");
                P("Grupos de 1º nível (sintéticos) com saldo:");
                foreach (var nc in ap.Keys.Where(k => Contabil.Core.HierarquiaContas.Nivel(k) == 1).OrderBy(k => k))
                {
                    var a = ap[nc];
                    if (a.Val1 == 0 && a.Val2 == 0 && a.Val3 == 0 && a.SaldoFinal == 0) continue;
                    plano.Contas.TryGetValue(nc, out var c);
                    P($"   {nc} {c?.Descricao,-28} ant={a.Val1,16:N2} déb={a.Val2,14:N2} cré={a.Val3,14:N2} atual={a.SaldoFinal,16:N2}");
                }
            }
            catch (Exception ex) { P("EXCEÇÃO: " + ex); }
        }

        /// <summary>Valida a regra "válido para a contabilidade" contra os dados reais de um período.</summary>
        public static void TestaContab(string pasta, string d1, string d2)
        {
            var caminho = System.IO.Path.Combine(pasta, "_teste_contab.txt");
            var log = new System.Text.StringBuilder();
            Action<string> P = s => { Console.WriteLine(s); log.AppendLine(s); System.IO.File.WriteAllText(caminho, log.ToString()); };
            try
            {
                var plano = Contabil.Core.PlanoContas.Carregar(System.IO.Path.Combine(pasta, "placon.DBF"));
                var lanc = new MovfinGravador(pasta).LerPeriodo(d1, d2, null);
                int validos = lanc.Count(l => plano.ValidoParaContabilidade(l.Debito, l.Credito));
                P($"=== Validação contábil {d1}..{d2} ===");
                P($"Total: {lanc.Count} | Válidos: {validos} | Pendentes: {lanc.Count - validos}");
                P("");
                P("Exemplos de PENDENTES (lado que não resolve):");
                foreach (var l in lanc.Where(l => !plano.ValidoParaContabilidade(l.Debito, l.Credito)).Take(12))
                {
                    string motivo = "";
                    if (!string.IsNullOrWhiteSpace(l.Debito) && plano.ResolverContabil(l.Debito) == null) motivo += $"DÉB [{l.Debito}] não resolve; ";
                    if (!string.IsNullOrWhiteSpace(l.Credito) && plano.ResolverContabil(l.Credito) == null) motivo += $"CRÉD [{l.Credito}] não resolve; ";
                    if (string.IsNullOrWhiteSpace(l.Debito) && string.IsNullOrWhiteSpace(l.Credito)) motivo = "sem débito nem crédito";
                    P($"   rec={l.Recno} {l.Data} D=[{l.Debito}] C=[{l.Credito}] V={l.Valor:N2} -> {motivo}");
                }
                P("");
                P("Exemplos de VÁLIDOS:");
                foreach (var l in lanc.Where(l => plano.ValidoParaContabilidade(l.Debito, l.Credito)).Take(6))
                    P($"   rec={l.Recno} D=[{l.Debito}]->{plano.ResolverContabil(l.Debito)} C=[{l.Credito}]->{plano.ResolverContabil(l.Credito)}");
            }
            catch (Exception ex) { P("EXCEÇÃO: " + ex); }
        }

        /// <summary>Simula a digitação na máscara de centavos do FrmGrupoComposto e imprime o resultado.</summary>
        public static void TestaMascara()
        {
            var f = new FrmGrupoComposto(new System.Collections.Generic.List<LancamentoMovfin>(), null);
            var _ = f.Handle;
            var tb = new TextBox();
            var mKey = typeof(FrmGrupoComposto).GetMethod("ValorKeyPress", BindingFlags.NonPublic | BindingFlags.Instance);
            var fReset = typeof(FrmGrupoComposto).GetField("_valorReset", BindingFlags.NonPublic | BindingFlags.Instance);

            void Press(char c) => mKey.Invoke(f, new object[] { tb, new KeyPressEventArgs(c) });
            void Seq(string label, string teclas)
            {
                tb.Text = ""; fReset.SetValue(f, true);
                foreach (var c in teclas) Press(c);
                Console.WriteLine($"   digitar \"{teclas}\"  ->  [{tb.Text}]   ({label})");
            }
            Console.WriteLine("=== Máscara de centavos (estilo calculadora) ===");
            Seq("um dígito", "1");
            Seq("dois", "12");
            Seq("três", "123");
            Seq("quatro", "1234");
            Seq("seis", "123456");
            // backspace (\b) depois de 1234
            tb.Text = ""; fReset.SetValue(f, true);
            foreach (var c in "1234") Press(c);
            Press('\b');
            Console.WriteLine($"   \"1234\" + backspace  ->  [{tb.Text}]   (esperado 1,23)");
            // ignora letra e vírgula
            tb.Text = ""; fReset.SetValue(f, true);
            foreach (var c in "9a,9") Press(c);
            Console.WriteLine($"   \"9a,9\" (letra/vírgula ignoradas)  ->  [{tb.Text}]   (esperado 0,99)");
            f.Dispose();
        }

        private static void RodarInterno(string pasta, decimal masterId, Action<string> P)
        {
            var g = new MovfinGravador(pasta);
            var plano = Contabil.Core.PlanoContas.Carregar(System.IO.Path.Combine(pasta, "placon.DBF"));

            // acha a data-âncora pelo mestre
            var amostra = g.LerPeriodo("20200101", "20301231", null).FirstOrDefault(l => l.MovId == masterId && l.OutroId == 0);
            if (amostra == null) { P($"Mestre {masterId} não achado."); return; }
            var data = amostra.Data;
            var grupo = g.LerGrupo(masterId, data);
            var mestreRec = grupo.First(l => l.OutroId == 0).Recno;
            var primDetRec = grupo.Where(l => l.OutroId == masterId).OrderBy(l => l.Recno).First().Recno;
            P($"=== Grupo {masterId} ({data}): {grupo.Count} linhas ===");
            foreach (var l in grupo.OrderBy(l => l.OutroId == 0 ? 0 : 1).ThenBy(l => l.Recno))
                P($"   rec={l.Recno} {(l.OutroId == 0 ? "MESTRE" : "det   ")} D=[{l.Debito}] C=[{l.Credito}] V={l.Valor}");

            // abre o NOVO editor
            var dlg = new FrmGrupoComposto(grupo, plano);
            var _h = dlg.Handle;
            Application.DoEvents();
            var dgv = (DataGridView)typeof(FrmGrupoComposto).GetField("dgv", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dlg);

            // edita DIRETO: valor do MESTRE -> 7777,77 (independente!) e valor do 1º detalhe -> 111,11
            foreach (DataGridViewRow r in dgv.Rows)
            {
                if (r.IsNewRow) continue;
                var o = r.Tag as LancamentoMovfin;
                if (o != null && o.Recno == mestreRec) r.Cells["Valor"].Value = "7777,77";
                if (o != null && o.Recno == primDetRec) r.Cells["Valor"].Value = "111,11";
            }
            P("\n   Editei DIRETO: VALOR do mestre -> 7777,77 ; VALOR do 1º detalhe -> 111,11");

            typeof(FrmGrupoComposto).GetMethod("Confirmar", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dlg, null);
            P($"   DialogResult = {dlg.DialogResult}");
            if (dlg.DialogResult != DialogResult.OK) { P("   >>> Confirmar bloqueou — ver Aviso <<<"); dlg.Dispose(); return; }

            // aplica como o FrmLancamentos faz: UPDATE por RECNO
            foreach (var rec in dlg.Excluidos) g.ExcluirLancamento(rec);
            foreach (var l in dlg.Linhas) { if (l.Recno != 0) g.AlterarLancamento(l); else g.InserirLancamento(l); }
            dlg.Dispose();
            P($"   Aplicado: {dlg.Linhas.Count(l => l.Recno != 0)} UPDATE por RECNO, {dlg.Linhas.Count(l => l.Recno == 0)} insert, {dlg.Excluidos.Count} delete.");

            // relê e confere
            var grupo2 = g.LerGrupo(masterId, data);
            var mestre2 = grupo2.First(l => l.Recno == mestreRec);
            var det2 = grupo2.First(l => l.Recno == primDetRec);
            P($"\n=== Releitura ===");
            P($"   Mestre rec={mestre2.Recno} VALOR={mestre2.Valor} (esperado 7777,77)");
            P($"   1º detalhe rec={det2.Recno} VALOR={det2.Valor} (esperado 111,11)");
            bool ok = mestre2.Valor == 7777.77m && det2.Valor == 111.11m;
            P($"   >>> {(ok ? "EDIÇÃO DIRETA OK — valores gravados como digitados, RECNOs preservados" : "FALHOU")} <<<");
        }
    }
}
