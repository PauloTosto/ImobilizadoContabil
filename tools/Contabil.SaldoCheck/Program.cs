using System;
using System.IO;
using System.Text;
using Contabil.Core;

// Valida a EngineSaldo: calcula o saldo de em-curso do cacau (11126001) como a
// apropriação o via, e compara com o gabarito (custo de produção 2025 = 30% do saldo).
//
// Uso: Contabil.SaldoCheck <pastaDados> [conta] [dataLimite YYYYMMDD]

internal static class Program
{
    private static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var pasta = args.Length > 0 ? args[0] : @"C:\Clipper_Migração\DADOS_ATUAIS";
        var conta = args.Length > 1 ? args[1] : "11126001";
        var ate = args.Length > 2 ? args[2] : "20251231";

        var placon = Path.Combine(pasta, "placon.DBF");
        var movfin = Path.Combine(pasta, "MOVFIN.DBF");

        var plano = PlanoContas.Carregar(placon);
        var engine = new EngineSaldo(plano);

        plano.Contas.TryGetValue(conta, out var info);
        Console.WriteLine($"Conta {conta} ({info?.Descricao}) | SDO âncora={info?.Sdo:N2} @ {info?.DataAncora}");

        // "como a apropriação viu": exclui o lote SIST_APROP do ano e o fechamento manual (DOC vazio) de 31/12.
        var ano = ate.Substring(0, 4);
        var saldos = engine.SaldosAte(movfin, ate,
            excluir: (doc, data) =>
                (doc == "SIST_APROP" && data.StartsWith(ano, StringComparison.Ordinal)) ||
                (doc.Length == 0 && data == ano + "1231"));

        saldos.TryGetValue(conta, out var saldo);
        Console.WriteLine($"\nSaldo calculado (excl. SIST_APROP {ano} e fechamento manual 31/12): {saldo:N2}");
        Console.WriteLine($"  30% (PERC1 cacau) = {Math.Round(saldo * 0.30m, 2):N2}");
        Console.WriteLine($"  GABARITO custo de produção {ano} = 2.236.932,88");

        // saldo "cru" até a véspera do fechamento, sem exclusões
        var saldosVespera = engine.SaldosAte(movfin, ano + "1230");
        saldosVespera.TryGetValue(conta, out var sv);
        Console.WriteLine($"\nSaldo até {ano}-12-30 (sem exclusões): {sv:N2}  | 30% = {Math.Round(sv * 0.30m, 2):N2}");
        return 0;
    }
}
