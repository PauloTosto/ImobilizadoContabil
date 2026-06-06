using System;
using System.Data.OleDb;
using System.Globalization;

namespace Imobilizado.Dados
{
    /// <summary>
    /// Insere no snapshot de saldos PTPLA&lt;ano&gt;.DBF (via VFPOLEDB) as contas novas que já
    /// existem no PLACON mas ainda não estão no PTPLA — espelha o btnNovasContasPlacon do
    /// Contabil2020. A conta nova entra com SDO/VALs zerados e a DATA do snapshot; o balancete
    /// é quem apura os movimentos. Usar VFPOLEDB mantém o índice .CDX em dia.
    /// </summary>
    public sealed class PtplaGravador
    {
        private readonly string _pasta;
        private readonly string _tabela;   // ex.: "PTPLA2024"

        public PtplaGravador(string pastaDados, int ano)
        {
            _pasta = pastaDados ?? throw new ArgumentNullException(nameof(pastaDados));
            _tabela = "PTPLA" + ano;
        }

        public bool Existe(string numConta)
        {
            using (var con = ConexaoVfp.Abrir(_pasta))
            using (var cmd = new OleDbCommand($"SELECT COUNT(*) FROM {_tabela} WHERE NUMCONTA=?", con))
            {
                cmd.Parameters.Add("nc", OleDbType.Char, 8).Value = (numConta ?? "").Trim();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        /// <summary>
        /// Insere uma conta nova no PTPLA: SDO/VALs = 0, GRAU/DESCRICAO/DESC2 do plano, e
        /// <paramref name="dataSnapshot"/> ("YYYYMMDD") na DATA (a data-âncora do snapshot;
        /// vazia → CTOD('')). Retorna true se inseriu, false se a conta já existia.
        /// </summary>
        public bool InserirConta(string numConta, string grau, string descricao, string desc2, string dataSnapshot)
        {
            numConta = (numConta ?? "").Trim();
            if (numConta.Length == 0 || Existe(numConta)) return false;

            using (var con = ConexaoVfp.Abrir(_pasta))
            {
                decimal id;
                using (var cmdMax = new OleDbCommand($"SELECT MAX(ID) FROM {_tabela}", con))
                {
                    var r = cmdMax.ExecuteScalar();
                    id = (r == null || r == DBNull.Value) ? 0m : Convert.ToDecimal(r, CultureInfo.InvariantCulture);
                }
                bool temData = !string.IsNullOrWhiteSpace(dataSnapshot) && dataSnapshot.Length == 8;
                var sql = $"INSERT INTO {_tabela} (NUMCONTA, GRAU, DESCRICAO, DESC2, " +
                          "SDO, ANT_SDO, VAL1, VAL2, VAL3, VAL4, INDICE, TAXA, ID, DATA, DATA_ANT) " +
                          $"VALUES (?,?,?,?,0,0,0,0,0,0,'',0,?,{(temData ? "?" : "CTOD('')")},CTOD(''))";
                using (var cmd = new OleDbCommand(sql, con))
                {
                    cmd.Parameters.Add("nc", OleDbType.Char, 8).Value = numConta;
                    cmd.Parameters.Add("gr", OleDbType.Char, 1).Value = (grau ?? "").Trim();
                    cmd.Parameters.Add("ds", OleDbType.Char, 40).Value = (descricao ?? "").Trim();
                    cmd.Parameters.Add("ap", OleDbType.Char, 25).Value = (desc2 ?? "").Trim();
                    cmd.Parameters.Add("id", OleDbType.Numeric).Value = id + 1;
                    if (temData) cmd.Parameters.Add("dt", OleDbType.Date).Value =
                        DateTime.ParseExact(dataSnapshot, "yyyyMMdd", CultureInfo.InvariantCulture);
                    cmd.ExecuteNonQuery();
                }
            }
            return true;
        }
    }
}
