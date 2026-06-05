namespace Imobilizado.Dados
{
    /// <summary>Uma linha genérica a inserir no MOVFIN (usada por depreciação e apropriação).</summary>
    public sealed class LinhaMovfin
    {
        public string Data;       // "YYYYMMDD"
        public string Debito;
        public string Credito;
        public decimal Valor;
        public string Historico;
        public string Doc;        // "SIST_IMOB", "SIST_APROP", ...
    }
}
