using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Contabil.Core;
using Imobilizado.Core.Dbf;

// Validação total da EngineSaldo: ancora no PTPLA<ano-1>, soma o ano inteiro do MOVFIN
// e compara o saldo de TODAS as contas com o SDO do PTPLA<ano> (gerado pelo balancete
// oficial do ApoioContabil2020). É o cross-check dos dois sistemas, conta a conta.
//
// Uso: Contabil.ValidaPtpla <pastaDados> [ano]

internal static class Program
{
    private static decimal Num(string s)
        => decimal.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var pasta = args.Length > 0 ? args[0] : @"C:\Clipper_Migração\DADOS_ATUAIS";
        var ano = args.Length > 1 ? args[1] : "2025";

        var master = Path.Combine(pasta, "placon.DBF");
        var ancora = Path.Combine(pasta, $"PTPLA{int.Parse(ano) - 1}.DBF");
        var alvo = Path.Combine(pasta, $"PTPLA{ano}.DBF");
        var movfin = Path.Combine(pasta, "MOVFIN.DBF");
        foreach (var f in new[] { master, ancora, alvo, movfin })
            if (!File.Exists(f)) { Console.WriteLine("Falta: " + f); return 1; }

        // estrutura+apelidos do master; âncora SDO/DATA do PTPLA<ano-1>
        var plano = PlanoContas.Carregar(master, ancora);
        var engine = new EngineSaldo(plano);
        // PTPLA<ano> é pós-tudo (apropriação+fechamento) → sem exclusões. Com rollup das sintéticas.
        var saldos = engine.SaldosComRollup(movfin, ano + "1231");

        // SDO esperado do PTPLA<ano>
        var esperado = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in new DbfReader(alvo).Registros())
        {
            var nc = r["NUMCONTA"];
            if (!string.IsNullOrEmpty(nc)) esperado[nc] = Num(r["SDO"]);
        }

        bool Sintetica(string nc) => nc.Length >= 8 && nc.Substring(5, 3) == "000";

        int exatos = 0, divergentes = 0, divSintet = 0, divAnalit = 0;
        int exatoAnalit = 0, totalAnalit = 0;
        var pioresAnalit = new List<(string nc, decimal calc, decimal esp, decimal dif)>();
        foreach (var kv in esperado)
        {
            saldos.TryGetValue(kv.Key, out var calc);
            var calcR = Math.Round(calc, 2, MidpointRounding.AwayFromZero);
            var adif = Math.Abs(calcR - kv.Value);
            bool sint = Sintetica(kv.Key);
            if (!sint) totalAnalit++;
            if (adif <= 0.01m) { exatos++; if (!sint) exatoAnalit++; }
            else
            {
                divergentes++;
                if (sint) divSintet++;
                else { divAnalit++; pioresAnalit.Add((kv.Key, calcR, kv.Value, calcR - kv.Value)); }
            }
        }

        Console.WriteLine($"=== Validação EngineSaldo vs PTPLA{ano} (âncora PTPLA{int.Parse(ano) - 1}) ===");
        Console.WriteLine($"Contas comparadas: {esperado.Count}  (analíticas: {totalAnalit}, sintéticas: {esperado.Count - totalAnalit})");
        Console.WriteLine($"  EXATAS (≤ 1 centavo):        {exatos}  ({100.0 * exatos / esperado.Count:N1}%)");
        Console.WriteLine($"  divergentes:                 {divergentes}  (sintéticas: {divSintet}, analíticas: {divAnalit})");
        Console.WriteLine();
        Console.WriteLine($"ANALÍTICAS (o que importa p/ apropriação): {exatoAnalit}/{totalAnalit} exatas ({100.0 * exatoAnalit / totalAnalit:N2}%)");
        if (pioresAnalit.Count > 0)
        {
            Console.WriteLine("\n-- maiores divergências ANALÍTICAS (até 25) --");
            foreach (var p in pioresAnalit.OrderByDescending(p => Math.Abs(p.dif)).Take(25))
            {
                plano.Contas.TryGetValue(p.nc, out var c);
                Console.WriteLine($"  {p.nc} {c?.Descricao,-32} calc={p.calc,15:N2} esp={p.esp,15:N2} dif={p.dif,13:N2}");
            }
        }
        return 0;
    }
}
