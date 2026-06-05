using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Imobilizado.Core.Dominio;

namespace Imobilizado.Core.Dbf
{
    /// <summary>Carrega Bem (IMOBIL.DBF) e a tabela de taxas (PLACON) a partir das DBFs.</summary>
    public static class CadastroDbf
    {
        private static decimal Num(string s)
            => decimal.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;

        /// <summary>Carrega os bens com fidelidade total (datas com dia, VAL_AQUIS) para edição.</summary>
        public static List<BemEdicao> CarregarBensEdicao(string caminhoImobil)
        {
            var bens = new List<BemEdicao>();
            var dbf = new DbfReader(caminhoImobil);
            foreach (var r in dbf.Registros())
            {
                bens.Add(new BemEdicao
                {
                    Codigo = r["COD"],
                    Descricao = r["DESC"],
                    ContaImobilizado = r["CONTAB"],
                    ContaDepAcumulada = r["DEP_ACUM"],
                    ContaResultado = r["RESULTADO"],
                    ValorAquisicao = Num(r["VAL_AQUIS"]),
                    DataAquisicao = BemEdicao.ParseDataDbf(r["DATA_AQUIS"]),
                    DataCorrecao = BemEdicao.ParseDataDbf(r["DATA_CORR"]),
                    BaseDepreciavel = Num(r["VAL_UFIR"]),
                    DepreciacaoInicial = Num(r["DEP_UFIR"]),
                    DataBaixa = BemEdicao.ParseDataDbf(r["DATA_BAIXA"]),
                    ValorBaixa = Num(r["VAL_BAIXA"]),
                });
            }
            return bens;
        }

        /// <summary>Bens no modelo enxuto do motor de depreciação.</summary>
        public static List<Bem> CarregarBens(string caminhoImobil)
        {
            var bens = new List<Bem>();
            foreach (var be in CarregarBensEdicao(caminhoImobil))
                bens.Add(be.ParaBem());
            return bens;
        }

        /// <summary>Mapa NUMCONTA → TAXA(%). A taxa fica nas contas-grupo grau 4.</summary>
        public static Dictionary<string, decimal> CarregarTaxas(string caminhoPlacon)
        {
            var taxas = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var dbf = new DbfReader(caminhoPlacon);
            foreach (var r in dbf.Registros())
                taxas[r["NUMCONTA"]] = Num(r["TAXA"]);
            return taxas;
        }
    }
}
