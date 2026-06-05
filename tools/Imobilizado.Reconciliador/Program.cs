using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Imobilizado.Core;
using Imobilizado.Core.Dbf;
using Imobilizado.Core.Dominio;

// Reconciliador: roda o MotorDepreciacao para uma competência e compara, linha a
// linha, com os lançamentos SIST_IMOB realmente gravados no MOVFIN daquele mês.
//
// Uso: Imobilizado.Reconciliador <pastaDados> <YYYYMM>
// Ex.: Imobilizado.Reconciliador "C:\Clipper_Migração\DADOS Clipper" 202401

internal static class Program
{
    private static decimal Num(string s)
        => decimal.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var pasta = args.Length > 0 ? args[0] : @"C:\Clipper_Migração\DADOS Clipper";
        var alvo = args.Length > 1 ? args[1] : "202401";

        var imobilPath = Path.Combine(pasta, "IMOBIL.DBF");
        var placonPath = Path.Combine(pasta, "placon.DBF");
        var movfinPath = Path.Combine(pasta, "MOVFIN.DBF");

        var comp = new AnoMes(int.Parse(alvo.Substring(0, 4)), int.Parse(alvo.Substring(4, 2)));

        var bens = CadastroDbf.CarregarBens(imobilPath);
        var taxas = CadastroDbf.CarregarTaxas(placonPath);
        var motor = new MotorDepreciacao(grupo => taxas.TryGetValue(grupo, out var t) ? t : 0m);

        // Previsto pelo motor
        var previstos = motor.GerarLancamentos(bens, comp);
        var pred = new Dictionary<(string, string, decimal), int>();
        foreach (var l in previstos)
            Incr(pred, (l.Debito, l.Credito, l.Valor));

        // Real do MOVFIN
        var real = new Dictionary<(string, string, decimal), int>();
        int totalReal = 0;
        var movfin = new DbfReader(movfinPath);
        foreach (var r in movfin.Registros())
        {
            if (r["DOC"] != LancamentoDepreciacao.Doc) continue;
            if (!r["DATA"].StartsWith(alvo, StringComparison.Ordinal)) continue;
            var chave = (r["DEBITO"], r["CREDITO"], Math.Round(Num(r["VALOR"]), 2, MidpointRounding.AwayFromZero));
            Incr(real, chave);
            totalReal++;
        }

        int nPred = pred.Values.Sum();
        int nMatch = pred.Where(kv => real.TryGetValue(kv.Key, out var c) && c > 0)
                          .Sum(kv => Math.Min(kv.Value, real[kv.Key]));

        Console.WriteLine($"=== Reconciliação competência {alvo} (motor C#) ===");
        Console.WriteLine($"Bens lidos: {bens.Count} | Previsto: {nPred} linhas | Real MOVFIN: {totalReal} linhas | Casaram: {nMatch}");

        Console.WriteLine("\nSó no PREVISTO (motor gerou, MOVFIN não tem):");
        foreach (var kv in Diferenca(pred, real))
            Console.WriteLine($"  {kv.Value}x D={kv.Key.Item1,-9} C={kv.Key.Item2,-9} V={kv.Key.Item3,12:N2}");

        Console.WriteLine("\nSó no REAL (MOVFIN tem, motor não gerou):");
        foreach (var kv in Diferenca(real, pred))
            Console.WriteLine($"  {kv.Value}x D={kv.Key.Item1,-9} C={kv.Key.Item2,-9} V={kv.Key.Item3,12:N2}");

        return 0;
    }

    private static void Incr<TK>(Dictionary<TK, int> d, TK k)
    {
        d.TryGetValue(k, out var c);
        d[k] = c + 1;
    }

    // a - b (multiset)
    private static IEnumerable<KeyValuePair<(string, string, decimal), int>> Diferenca(
        Dictionary<(string, string, decimal), int> a, Dictionary<(string, string, decimal), int> b)
    {
        foreach (var kv in a)
        {
            b.TryGetValue(kv.Key, out var bc);
            int resto = kv.Value - bc;
            if (resto > 0) yield return new KeyValuePair<(string, string, decimal), int>(kv.Key, resto);
        }
    }
}
