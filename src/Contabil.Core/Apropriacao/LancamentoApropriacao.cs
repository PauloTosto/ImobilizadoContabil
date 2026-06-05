namespace Contabil.Core.Apropriacao
{
    /// <summary>Um lançamento de apropriação a gravar no MOVFIN (DOC = "SIST_APROP").</summary>
    public sealed class LancamentoApropriacao
    {
        public string Data;       // "YYYYMMDD" — data final do período
        public string Debito;
        public string Credito;
        public decimal Valor;
        public string Historico;
        public const string Doc = "SIST_APROP";

        public override string ToString()
            => $"{Data} D={Debito} C={Credito} V={Valor,14:N2}  {Historico}";
    }
}
