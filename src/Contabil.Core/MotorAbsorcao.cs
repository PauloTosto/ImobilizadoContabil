using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Imobilizado.Core.Dbf;

namespace Contabil.Core
{
    /// <summary>Linha do RELAC.DBF: par débito (conta de ativo/estoque) × crédito (custo apropriado) + % de rateio.</summary>
    public sealed class LinhaRelac
    {
        public string Debito;    // conta de ativo/estoque (ex.: 11126001 CACAU EM AMENDOAS)
        public string Credito;   // conta "custo apropriado" do centro (ex.: 31341090)
        public decimal Quant1;   // % do rateio (0 = leva o valor cheio do grupo)

        public static List<LinhaRelac> Carregar(string caminhoRelac)
        {
            var lista = new List<LinhaRelac>();
            foreach (var r in new DbfReader(caminhoRelac).Registros())
                lista.Add(new LinhaRelac
                {
                    Debito = r["DEBITO"].Trim(),
                    Credito = r["CREDITO"].Trim(),
                    Quant1 = decimal.TryParse(r["QUANT1"].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var q) ? q : 0m,
                });
            return lista;
        }
    }

    /// <summary>
    /// Absorção de custo de produção — porte do Absorcao_Custo/Monta_Bloco do Clipper (FLT_ABS.PRG):
    /// para cada linha do RELAC (filtrada por prefixo de débito/crédito), toma o GRUPO da conta de
    /// crédito (5 primeiros dígitos + "000") e o seu custo líquido (Val2−Val3 da apuração do
    /// período — no Clipper era o VAL2/VAL3 do PLACON deixado pelo balancete); QUANT1 vazio leva o
    /// valor cheio, senão rateia o % com ajuste de arredondamento (diferença &lt; |2| vai pra linha
    /// corrente, igual ao Clipper). Gera os pares D=ativo / C=custo-apropriado que, lançados,
    /// ZERAM os grupos de custo contra o estoque (DOC=SIST_ABSOR).
    /// VALIDADO: reproduz 100% a absorção real de abr/2026 (inclusive os rateios 85/15, 60/40,
    /// 20/80 e 60/18/4/6/12). Linhas com '*' (desativadas) são puladas.
    /// </summary>
    public static class MotorAbsorcao
    {
        public sealed class ItemAbsorcao
        {
            public string Debito;     // NUMCONTA do ativo
            public string Credito;    // NUMCONTA do custo apropriado
            public string Grupo;      // grupo de custo (XXXXX000)
            public decimal Quant1;    // % aplicado (0 = cheio)
            public decimal Valor;
        }

        /// <param name="valorDoGrupo">grupo "XXXXX000" → custo líquido (Val2−Val3) do período.</param>
        public static List<ItemAbsorcao> Gerar(IEnumerable<LinhaRelac> relac,
            Func<string, decimal> valorDoGrupo, string prefixoDebito, string prefixoCredito)
        {
            string pd = (prefixoDebito ?? "").Trim(), pc = (prefixoCredito ?? "").Trim();
            var saida = new List<ItemAbsorcao>();
            var somaPorGrupo = new Dictionary<string, decimal>(StringComparer.Ordinal);

            foreach (var r in relac)
            {
                var deb = (r.Debito ?? "").Trim();
                var cred = (r.Credito ?? "").Trim();
                if (deb.StartsWith("*") || cred.StartsWith("*")) continue;            // linha desativada
                if (deb.Length == 0 || cred.Length < 5) continue;
                if (pd.Length > 0 && !deb.StartsWith(pd, StringComparison.Ordinal)) continue;
                if (pc.Length > 0 && !cred.StartsWith(pc, StringComparison.Ordinal)) continue;

                var grupo = cred.Substring(0, 5) + "000";
                var tval = decimal.Round(valorDoGrupo(grupo), 2);
                if (tval == 0m) continue;

                decimal valor;
                if (r.Quant1 == 0m)
                {
                    valor = tval;                                                      // sem %: valor cheio
                }
                else
                {
                    somaPorGrupo.TryGetValue(grupo, out var jaRateado);
                    valor = decimal.Round(tval * r.Quant1 / 100m, 2);
                    var resto = tval - (jaRateado + valor);
                    if (resto != 0m && Math.Abs(resto) < 2m) valor += resto;           // ajuste de centavos (Clipper)
                }
                somaPorGrupo.TryGetValue(grupo, out var acc);
                somaPorGrupo[grupo] = acc + valor;

                saida.Add(new ItemAbsorcao { Debito = deb, Credito = cred, Grupo = grupo, Quant1 = r.Quant1, Valor = valor });
            }
            return saida;
        }
    }
}
