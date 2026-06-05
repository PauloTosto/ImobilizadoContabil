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
