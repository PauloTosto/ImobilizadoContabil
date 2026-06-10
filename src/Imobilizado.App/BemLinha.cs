namespace Imobilizado.App
{
    /// <summary>Linha da grade de bens (projeção do Bem + valores calculados para a competência).</summary>
    public sealed class BemLinha
    {
        public string Codigo { get; set; }
        public string Descricao { get; set; }
        public string Conta { get; set; }
        public string ContaResultado { get; set; }    // débito da depreciação (despesa) — PLACON
        public string ContaDepAcum { get; set; }      // crédito da depreciação (dep. acumulada) — PLACON
        public string Grupo { get; set; }
        public decimal Taxa { get; set; }            // % a.a.
        public decimal Base { get; set; }            // base depreciável (Real)
        public decimal DepInicial { get; set; }
        public string Partida { get; set; }          // YYYYMM
        public decimal QuotaMes { get; set; }        // quota cheia mensal
        public decimal QuotaCompetencia { get; set; }// quota efetiva na competência selecionada
        public string Situacao { get; set; }         // Ativo / Esgotado / Baixado / Sem taxa
    }
}
