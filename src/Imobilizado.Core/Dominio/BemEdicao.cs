using System;
using System.Globalization;

namespace Imobilizado.Core.Dominio
{
    /// <summary>
    /// Bem com fidelidade total para edição/gravação no IMOBIL.DBF (datas com dia,
    /// VAL_AQUIS separado da base). O motor de depreciação usa <see cref="ParaBem"/>,
    /// que reduz para o modelo enxuto (Bem, baseado em ano/mês).
    /// </summary>
    public sealed class BemEdicao
    {
        public string Codigo { get; set; } = "";           // COD
        public string Descricao { get; set; } = "";        // DESC
        public string ContaImobilizado { get; set; } = ""; // CONTAB
        public string ContaDepAcumulada { get; set; } = "";// DEP_ACUM
        public string ContaResultado { get; set; } = "";   // RESULTADO
        public decimal ValorAquisicao { get; set; }        // VAL_AQUIS
        public DateTime? DataAquisicao { get; set; }       // DATA_AQUIS
        public DateTime? DataCorrecao { get; set; }        // DATA_CORR
        public decimal BaseDepreciavel { get; set; }       // VAL_UFIR (base em Real)
        public decimal DepreciacaoInicial { get; set; }    // DEP_UFIR
        public DateTime? DataBaixa { get; set; }           // DATA_BAIXA
        public decimal ValorBaixa { get; set; }            // VAL_BAIXA

        /// <summary>Conta-grupo (grau 4), de onde sai a taxa. Ex.: 12265001 → 12265000.</summary>
        public string ContaGrupo()
        {
            var c = ContaImobilizado?.Trim() ?? "";
            return c.Length >= 5 ? c.Substring(0, 5) + "000" : c;
        }

        /// <summary>Projeta para o modelo do motor de depreciação.</summary>
        public Bem ParaBem() => new Bem
        {
            Codigo = Codigo,
            Descricao = Descricao,
            ContaImobilizado = ContaImobilizado,
            ContaDepAcumulada = ContaDepAcumulada,
            ContaResultado = ContaResultado,
            BaseDepreciavel = BaseDepreciavel,
            DepreciacaoInicial = DepreciacaoInicial,
            DataPartida = (DataCorrecao ?? DataAquisicao) is DateTime p ? (AnoMes?)AnoMes.De(p) : null,
            DataBaixa = DataBaixa is DateTime b ? (AnoMes?)AnoMes.De(b) : null,
        };

        /// <summary>Parse de data DBF "YYYYMMDD" → DateTime? (null se vazia/ano&lt;1900).</summary>
        public static DateTime? ParseDataDbf(string yyyymmdd)
        {
            if (string.IsNullOrWhiteSpace(yyyymmdd)) return null;
            var s = yyyymmdd.Trim();
            if (s.Length < 8) return null;
            if (!DateTime.TryParseExact(s.Substring(0, 8), "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return null;
            if (d.Year < 1900) return null;
            return d;
        }
    }
}
