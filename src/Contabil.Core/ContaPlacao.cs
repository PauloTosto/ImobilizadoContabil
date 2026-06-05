using System;
using System.Collections.Generic;
using System.Globalization;
using Imobilizado.Core.Dbf;

namespace Contabil.Core
{
    /// <summary>
    /// Uma conta do PLACON master para cadastro (CRUD). Campos editáveis: número, grau,
    /// descrição, apelido (DESC2) e taxa. SDO é só exibição — quem apura é o balancete.
    /// </summary>
    public sealed class ContaPlacao
    {
        public string NumConta { get; set; } = "";  // 8 dígitos (chave)
        public string Grau { get; set; } = "";       // nível hierárquico (1 char)
        public string Descricao { get; set; } = "";
        public string Apelido { get; set; } = "";     // DESC2
        public decimal Taxa { get; set; }             // % depreciação (contas-grupo de imobilizado)
        public decimal Sdo { get; set; }              // último saldo apurado (somente leitura)

        public static List<ContaPlacao> Carregar(string caminhoPlacon)
        {
            var lista = new List<ContaPlacao>();
            foreach (var r in new DbfReader(caminhoPlacon).Registros())
            {
                var nc = r["NUMCONTA"];
                if (string.IsNullOrEmpty(nc)) continue;
                lista.Add(new ContaPlacao
                {
                    NumConta = nc,
                    Grau = r["GRAU"],
                    Descricao = r["DESCRICAO"],
                    Apelido = r["DESC2"],
                    Taxa = Num(r["TAXA"]),
                    Sdo = Num(r["SDO"]),
                });
            }
            return lista;
        }

        private static decimal Num(string s)
            => decimal.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }
}
