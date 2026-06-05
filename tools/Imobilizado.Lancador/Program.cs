using System;
using System.IO;
using System.Linq;
using Imobilizado.Core;
using Imobilizado.Core.Dbf;
using Imobilizado.Core.Dominio;
using Imobilizado.Dados;

// Lançador de depreciação: calcula a competência com o motor e (opcionalmente)
// grava no MOVFIN via VFPOLEDB.
//
// Uso:
//   Imobilizado.Lancador <pastaDados> <YYYYMM> [--gravar] [--substituir]
//
//   (sem --gravar)  => DRY-RUN: só mostra o que seria lançado, não escreve nada.
//   --gravar        => insere de fato no MOVFIN.DBF da pasta.
//   --substituir    => antes de inserir, exclui os SIST_IMOB já existentes do mês.
//
// AVISO: aponte para uma CÓPIA dos dados ao testar. MOVFIN é produção.

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Uso: Imobilizado.Lancador <pastaDados> <YYYYMM> [--gravar] [--substituir]");
            return 1;
        }

        var pasta = args[0];
        var alvo = args[1];
        bool gravar = args.Contains("--gravar", StringComparer.OrdinalIgnoreCase);
        bool substituir = args.Contains("--substituir", StringComparer.OrdinalIgnoreCase);

        var comp = new AnoMes(int.Parse(alvo.Substring(0, 4)), int.Parse(alvo.Substring(4, 2)));

        var bens = CadastroDbf.CarregarBens(Path.Combine(pasta, "IMOBIL.DBF"));
        var taxas = CadastroDbf.CarregarTaxas(Path.Combine(pasta, "placon.DBF"));
        var motor = new MotorDepreciacao(g => taxas.TryGetValue(g, out var t) ? t : 0m);

        var lancamentos = motor.GerarLancamentos(bens, comp);
        decimal total = lancamentos.Sum(l => l.Valor);

        Console.WriteLine($"=== Depreciação competência {alvo} ===");
        Console.WriteLine($"Bens lidos: {bens.Count} | Linhas a lançar: {lancamentos.Count} | Total: R$ {total:N2}");
        Console.WriteLine();
        foreach (var l in lancamentos)
            Console.WriteLine("  " + l);
        Console.WriteLine();

        var gravador = new MovfinGravador(pasta);

        if (!gravar)
        {
            Console.WriteLine(">> DRY-RUN: nada foi gravado. Use --gravar para inserir no MOVFIN.");
            return 0;
        }

        int jaExistem = gravador.ContarDepreciacaoExistente(comp);
        if (jaExistem > 0 && !substituir)
        {
            Console.WriteLine($">> ABORTADO: já existem {jaExistem} lançamentos SIST_IMOB em {alvo}. " +
                              "Use --substituir para reprocessar o mês.");
            return 2;
        }

        int n = gravador.Gravar(comp, lancamentos, gravarDeFato: true, substituirExistentes: substituir);
        Console.WriteLine($">> GRAVADO: {n} lançamentos inseridos no MOVFIN" +
                          (substituir ? $" (após excluir {jaExistem} existentes)." : "."));
        return 0;
    }
}
