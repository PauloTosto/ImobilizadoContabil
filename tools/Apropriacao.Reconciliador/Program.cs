using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Contabil.Core;
using Contabil.Core.Apropriacao;
using Imobilizado.Core.Dbf;

// Reconciliador da apropriação: roda o MotorApropriacao para um período e compara
// com os lançamentos SIST_APROP reais do MOVFIN.
//
// Uso: Apropriacao.Reconciliador <pastaDados> <ano>   (ex.: 2025)

internal static class Program
{
    private static decimal Num(string s)
        => decimal.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var pasta = args.Length > 0 ? args[0] : @"C:\Clipper_Migração\DADOS_ATUAIS";
        var ano = args.Length > 1 ? args[1] : "2025";
        var ancora = args.Length > 2 ? args[2] : "placon.DBF";  // PTPLA<ano-1>.DBF (âncora de saldo)
        var data1 = ano + "0101";
        var data2 = ano + "1231";

        // procedimento oficial: estrutura+apelidos do placon master; saldo da âncora PTPLA<ano-1>.
        Console.WriteLine($"(estrutura: placon.DBF | âncora de saldos: {ancora})");
        var plano = PlanoContas.Carregar(Path.Combine(pasta, "placon.DBF"), Path.Combine(pasta, ancora));
        var produtos = ProdutoCusto.Carregar(Path.Combine(pasta, "CADCUSTO.DBF"));
        var movs = MovimentoEstoque.CarregarPorProduto(Path.Combine(pasta, "ENTSAI.DBF"));
        var engine = new EngineSaldo(plano);
        var movfin = Path.Combine(pasta, "MOVFIN.DBF");

        // estado "como a apropriação viu": a apropriação roda ANTES do fechamento.
        // Exclui o próprio lote (SIST_APROP), o fechamento de balanço (SIST_BAL) e os
        // lançamentos manuais de fechamento (DOC vazio) datados em 31/12 do ano.
        var apur = engine.ApurarPeriodo(movfin, data1, data2,
            excluir: (doc, data) => ((doc == "SIST_APROP" || doc == "SIST_BAL") && data.StartsWith(ano, StringComparison.Ordinal))
                                  || (doc.Length == 0 && data == data2));

        var motor = new MotorApropriacao();
        var previstos = motor.Gerar(produtos, movs, apur, data1, data2);

        // gabarito real
        var reais = new List<(string d, string c, decimal v, string h)>();
        var dbf = new DbfReader(movfin);
        foreach (var r in dbf.Registros())
        {
            if (r["DOC"] != "SIST_APROP") continue;
            if (!r["DATA"].StartsWith(ano, StringComparison.Ordinal)) continue;
            reais.Add((r["DEBITO"], r["CREDITO"], Math.Round(Num(r["VALOR"]), 2), r["HIST"]));
        }

        Console.WriteLine($"=== Apropriação {ano} ===");
        Console.WriteLine($"Previsto: {previstos.Count} lançamentos (total {previstos.Sum(l => l.Valor):N2})");
        Console.WriteLine($"Real:     {reais.Count} lançamentos (total {reais.Sum(r => r.v):N2})");

        // multiset por (debito, credito, valor)
        var pred = new Dictionary<(string, string, decimal), int>();
        foreach (var l in previstos) Incr(pred, (l.Debito, l.Credito, l.Valor));
        var real = new Dictionary<(string, string, decimal), int>();
        foreach (var r in reais) Incr(real, (r.d, r.c, r.v));
        int match = pred.Where(kv => real.TryGetValue(kv.Key, out var c) && c > 0).Sum(kv => Math.Min(kv.Value, real[kv.Key]));
        Console.WriteLine($"Casaram exatos (deb,cred,valor): {match}");

        Console.WriteLine("\n-- Previsto (até 25) --");
        foreach (var l in previstos.OrderBy(l => l.Credito).ThenBy(l => l.Debito).Take(25))
            Console.WriteLine("  " + l);
        Console.WriteLine("\n-- Real (até 25) --");
        foreach (var r in reais.OrderBy(r => r.c).ThenBy(r => r.d).Take(25))
            Console.WriteLine($"  D={r.d} C={r.c} V={r.v,14:N2}  {r.h}");
        return 0;
    }

    private static void Incr<TK>(Dictionary<TK, int> d, TK k) { d.TryGetValue(k, out var c); d[k] = c + 1; }
}
