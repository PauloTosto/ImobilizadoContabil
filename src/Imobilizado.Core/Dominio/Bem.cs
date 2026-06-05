namespace Imobilizado.Core.Dominio
{
    /// <summary>
    /// Um bem do imobilizado (uma linha do IMOBIL.DBF).
    ///
    /// NOTA HISTÓRICA importante: o campo <see cref="BaseDepreciavel"/> vem de
    /// VAL_UFIR. O nome é legado — a UFIR foi extinta em 2000 e hoje vale 1:1 com
    /// o Real, então VAL_UFIR guarda a base depreciável em Reais. Por isso usamos
    /// VAL_UFIR (já normalizado) e NÃO VAL_AQUIS (que tem moeda antiga misturada).
    /// </summary>
    public sealed class Bem
    {
        public string Codigo { get; set; }            // COD
        public string Descricao { get; set; }         // DESC
        public string ContaImobilizado { get; set; }  // CONTAB — define a taxa pelo seu grupo (grau 4)
        public string ContaDepAcumulada { get; set; } // DEP_ACUM — crédito do lançamento
        public string ContaResultado { get; set; }    // RESULTADO — débito do lançamento (despesa)
        public decimal BaseDepreciavel { get; set; }  // VAL_UFIR (em Real, ver nota acima)
        public decimal DepreciacaoInicial { get; set; } // DEP_UFIR — acumulada na data de partida
        public AnoMes? DataPartida { get; set; }      // DATA_CORR (fallback DATA_AQUIS)
        public AnoMes? DataBaixa { get; set; }         // DATA_BAIXA — se preenchida, bem deixou de existir

        /// <summary>
        /// Conta-grupo (grau 4) de onde sai a taxa de depreciação.
        /// Ex.: CONTAB 12265001 → grupo 12265000 (TRATORES, 10% a.a.).
        /// </summary>
        public string ContaGrupo()
        {
            var c = ContaImobilizado?.Trim() ?? "";
            return c.Length >= 5 ? c.Substring(0, 5) + "000" : c;
        }
    }
}
