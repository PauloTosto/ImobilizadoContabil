namespace Imobilizado.Core.Dominio
{
    /// <summary>
    /// Um lançamento de depreciação a ser gravado no MOVFIN.
    /// Direção contábil (confirmada nos dados reais): débito na conta de
    /// resultado/despesa, crédito na depreciação acumulada.
    /// </summary>
    public sealed class LancamentoDepreciacao
    {
        public string Data { get; set; }      // DATA — último dia do mês, "YYYYMMDD"
        public string Debito { get; set; }     // DEBITO — conta RESULTADO
        public string Credito { get; set; }    // CREDITO — conta DEP_ACUM
        public decimal Valor { get; set; }     // VALOR — soma das quotas do par, arredondada a 2 casas
        public string Historico { get; set; }  // HIST — "REF.DEPREC <valor 4 casas> UFIRS"
        public const string Doc = "SIST_IMOB"; // DOC — tag de origem

        public override string ToString()
            => $"{Data} D={Debito} C={Credito} V={Valor,12:N2}  {Historico}";
    }
}
