using System;
using System.Collections.Generic;
using System.Globalization;
using Imobilizado.Core.Dbf;

namespace Contabil.Core.Apropriacao
{
    /// <summary>Um produto do CADCUSTO: liga o produto às contas contábeis e percentuais.</summary>
    public sealed class ProdutoCusto
    {
        public string Cod;          // COD (4)
        public string Desc;         // DESC
        public string Producao;     // PRODUCAO — conta de produção acabada/estoque
        public string EmCurso;      // EMCURSO — conta de produção em curso
        public string Receita;      // RECEITA
        public string CustoVenda;   // CUSTOVENDA
        public string Unid;         // UNID
        public decimal Estoque;     // ESTOQUE — qtd base de estoque (na DATA)
        public string Data;         // DATA "YYYYMMDD" — data-base do estoque do cadastro
        public decimal Perc1, Perc2, Perc3, Perc4;

        public static List<ProdutoCusto> Carregar(string caminhoCadcusto)
        {
            var lista = new List<ProdutoCusto>();
            var dbf = new DbfReader(caminhoCadcusto);
            foreach (var r in dbf.Registros())
            {
                lista.Add(new ProdutoCusto
                {
                    Cod = r["COD"], Desc = r["DESC"],
                    Producao = r["PRODUCAO"], EmCurso = r["EMCURSO"],
                    Receita = r["RECEITA"], CustoVenda = r["CUSTOVENDA"],
                    Unid = r["UNID"], Estoque = Num(r["ESTOQUE"]), Data = r["DATA"].Trim(),
                    Perc1 = Num(r["PERC1"]), Perc2 = Num(r["PERC2"]),
                    Perc3 = Num(r["PERC3"]), Perc4 = Num(r["PERC4"]),
                });
            }
            return lista;
        }

        private static decimal Num(string s)
            => decimal.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }
}
