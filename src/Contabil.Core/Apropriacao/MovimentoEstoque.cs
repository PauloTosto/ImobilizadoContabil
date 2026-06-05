using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Imobilizado.Core.Dbf;

namespace Contabil.Core.Apropriacao
{
    /// <summary>Um movimento físico de estoque (ENTSAI): entrada/saída de um produto numa data.</summary>
    public sealed class MovimentoEstoque
    {
        public string Data;     // "YYYYMMDD"
        public string Cod;      // produto
        public decimal Ent;     // quantidade entrada
        public decimal Sai;     // quantidade saída
        public string ObsEnt;
        public string ObsSai;

        /// <summary>Carrega ENTSAI agrupado por COD, cada lista ordenada por data (para reproduzir o SEEK do Clipper).</summary>
        public static Dictionary<string, List<MovimentoEstoque>> CarregarPorProduto(string caminhoEntsai)
        {
            var dbf = new DbfReader(caminhoEntsai);
            var todos = new List<MovimentoEstoque>();
            foreach (var r in dbf.Registros())
            {
                todos.Add(new MovimentoEstoque
                {
                    Data = r["DATA"].Trim(), Cod = r["COD"],
                    Ent = Num(r["ENT"]), Sai = Num(r["SAI"]),
                    ObsEnt = r["OBS_ENT"], ObsSai = r["OBS_SAI"],
                });
            }
            return todos
                .GroupBy(m => m.Cod)
                .ToDictionary(g => g.Key,
                              g => g.OrderBy(m => m.Data, StringComparer.Ordinal).ToList(),
                              StringComparer.OrdinalIgnoreCase);
        }

        private static decimal Num(string s)
            => decimal.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }
}
