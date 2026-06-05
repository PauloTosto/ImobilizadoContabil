namespace Imobilizado.Dados
{
    /// <summary>
    /// Um lançamento do MOVFIN para leitura/edição manual (partida simples: uma linha com
    /// conta de débito, conta de crédito e valor). MovId identifica a linha para alterar/excluir.
    /// </summary>
    public sealed class LancamentoMovfin
    {
        public int Recno;         // número físico do registro no DBF — identidade ÚNICA (MOV_ID não é único!)
        public decimal MovId;
        public decimal OutroId;   // 0 = mestre/avulso; != 0 = detalhe ligado ao mestre (partida dobrada)
        public string Data;       // "YYYYMMDD"
        public string Debito;     // apelido ou número de conta
        public string Credito;
        public decimal Valor;
        public string Historico;
        public string Doc;
        public string Forn;       // titular/fornecedor
        public string Tipo;       // "R" (recebimento), "P" (pagamento), "" (contábil)
        public bool TpFin;        // true = financeiro, false = contábil
        public string Venc;       // "YYYYMMDD" ou ""
        public string DocFisc;    // número do documento fiscal (ex.: "12/2024")
        public string Emissor;    // conta do fornecedor/cliente (referência fiscal)
        public string DataEmi;    // "YYYYMMDD" ou "" — data de emissão do documento
    }
}
