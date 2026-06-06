using System;
using System.Windows.Forms;

namespace Imobilizado.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0 && args[0] == "--selftest")
            {
                // Constrói os formulários forçando a criação do handle, para flagrar
                // qualquer erro de montagem de UI sem abrir janela. CI/headless.
                using (var f1 = new FrmPrincipal()) { var _ = f1.Handle; }
                using (var f2 = new FrmBem(null, g => 0m)) { var _ = f2.Handle; }
                using (var f3 = new FrmApropriacao()) { var _ = f3.Handle; }
                using (var f4 = new FrmPlacon()) { var _ = f4.Handle; }
                using (var f5 = new FrmConta(null, _ => true)) { var _ = f5.Handle; }
                using (var f6 = new FrmLancamentos()) { var _ = f6.Handle; }
                using (var f7 = new FrmLancamento(null, null)) { var _ = f7.Handle; }
                using (var f8 = new FrmLancamentoComposto(null, null, null)) { var _ = f8.Handle; }
                using (var f9 = new FrmGrupoComposto(new System.Collections.Generic.List<Imobilizado.Dados.LancamentoMovfin>(), null)) { var _ = f9.Handle; }
                Console.WriteLine("SELFTEST OK");
                return;
            }

            if (args.Length > 1 && args[0] == "--testcomposto")
            {
                TesteComposto.Rodar(args[1], args.Length > 2 ? decimal.Parse(args[2]) : 0m);
                return;
            }

            if (args.Length > 3 && args[0] == "--capturacomposto")
            {
                CapturaComposto.Rodar(args[1], decimal.Parse(args[2]), args[3]);
                return;
            }

            if (args.Length > 4 && args[0] == "--capturalanc")
            {
                CapturaComposto.RodarLanc(args[1], args[2], args[3], args[4]);
                return;
            }

            if (args.Length > 0 && args[0] == "--testmask")
            {
                TesteComposto.TestaMascara();
                return;
            }

            if (args.Length > 1 && args[0] == "--testcontab")
            {
                TesteComposto.TestaContab(args[1], args.Length > 2 ? args[2] : "20250101", args.Length > 3 ? args[3] : "20251231");
                return;
            }

            if (args.Length > 0 && args[0] == "--apropriacao")
            {
                Application.Run(new FrmApropriacao());
                return;
            }
            if (args.Length > 0 && args[0] == "--placon")
            {
                Application.Run(new FrmPlacon());
                return;
            }
            if (args.Length > 0 && args[0] == "--lancamentos")
            {
                Application.Run(new FrmLancamentos());
                return;
            }
            if (args.Length > 0 && args[0] == "--composto")
            {
                Application.Run(new FrmLancamentoComposto(null, null, null));
                return;
            }
            if (args.Length > 0 && args[0] == "--lancamento")
            {
                var pasta = ConfigApp.CarregarPastaDados();
                var pl = System.IO.File.Exists(System.IO.Path.Combine(pasta, "placon.DBF"))
                    ? Contabil.Core.PlanoContas.Carregar(System.IO.Path.Combine(pasta, "placon.DBF")) : null;
                Application.Run(new FrmLancamento(null, pl));
                return;
            }

            Application.Run(new FrmPrincipal());
        }
    }
}
