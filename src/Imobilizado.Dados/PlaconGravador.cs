using System;
using System.Data.OleDb;
using System.Globalization;

namespace Imobilizado.Dados
{
    /// <summary>
    /// Cadastro de contas no PLACON master via VFPOLEDB: incluir e alterar. Usa VFPOLEDB
    /// (não escrita crua) para manter o .CDX estrutural em dia — o índice por DESC2/NUMCONTA
    /// é o que o balancete e a resolução de apelidos usam.
    ///
    /// Inclui só os campos de cadastro (número, grau, descrição, apelido, taxa); os saldos
    /// (SDO/VAL*) entram zerados — quem os apura é o balancete. Alterar NÃO toca em saldos.
    /// </summary>
    public sealed class PlaconGravador
    {
        private readonly string _pasta;
        private const string Tabela = "PLACON";

        public PlaconGravador(string pastaDados)
        {
            _pasta = pastaDados ?? throw new ArgumentNullException(nameof(pastaDados));
        }

        public bool Existe(string numConta)
        {
            using (var con = ConexaoVfp.Abrir(_pasta))
            using (var cmd = new OleDbCommand($"SELECT COUNT(*) FROM {Tabela} WHERE NUMCONTA=?", con))
            {
                cmd.Parameters.Add("nc", OleDbType.Char, 8).Value = (numConta ?? "").Trim();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        public void Incluir(string numConta, string grau, string descricao, string apelido, decimal taxa)
        {
            using (var con = ConexaoVfp.Abrir(_pasta))
            {
                decimal id;
                using (var cmdMax = new OleDbCommand($"SELECT MAX(ID) FROM {Tabela}", con))
                {
                    var r = cmdMax.ExecuteScalar();
                    id = (r == null || r == DBNull.Value) ? 0m : Convert.ToDecimal(r, CultureInfo.InvariantCulture);
                }
                // saldos zerados; datas vazias via CTOD(''); INDICE vazio.
                using (var cmd = new OleDbCommand(
                    $"INSERT INTO {Tabela} (NUMCONTA, GRAU, DESCRICAO, DESC2, TAXA, ID, " +
                    "SDO, ANT_SDO, VAL1, VAL2, VAL3, VAL4, INDICE, DATA, DATA_ANT) " +
                    "VALUES (?,?,?,?,?,?,0,0,0,0,0,0,'',CTOD(''),CTOD(''))", con))
                {
                    cmd.Parameters.Add("nc", OleDbType.Char, 8).Value = (numConta ?? "").Trim();
                    cmd.Parameters.Add("gr", OleDbType.Char, 1).Value = (grau ?? "").Trim();
                    cmd.Parameters.Add("ds", OleDbType.Char, 40).Value = (descricao ?? "").Trim();
                    cmd.Parameters.Add("ap", OleDbType.Char, 25).Value = (apelido ?? "").Trim();
                    cmd.Parameters.Add("tx", OleDbType.Numeric).Value = taxa;
                    cmd.Parameters.Add("id", OleDbType.Numeric).Value = id + 1;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>Exclui uma conta do plano. (O chamador deve garantir que não tenha filhas.)</summary>
        public void Excluir(string numConta)
        {
            using (var con = ConexaoVfp.Abrir(_pasta))
            using (var cmd = new OleDbCommand($"DELETE FROM {Tabela} WHERE NUMCONTA=?", con))
            {
                cmd.Parameters.Add("nc", OleDbType.Char, 8).Value = (numConta ?? "").Trim();
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Altera só o cadastro (grau/descrição/apelido/taxa); preserva os saldos.</summary>
        public void Alterar(string numConta, string grau, string descricao, string apelido, decimal taxa)
        {
            using (var con = ConexaoVfp.Abrir(_pasta))
            using (var cmd = new OleDbCommand(
                $"UPDATE {Tabela} SET GRAU=?, DESCRICAO=?, DESC2=?, TAXA=? WHERE NUMCONTA=?", con))
            {
                cmd.Parameters.Add("gr", OleDbType.Char, 1).Value = (grau ?? "").Trim();
                cmd.Parameters.Add("ds", OleDbType.Char, 40).Value = (descricao ?? "").Trim();
                cmd.Parameters.Add("ap", OleDbType.Char, 25).Value = (apelido ?? "").Trim();
                cmd.Parameters.Add("tx", OleDbType.Numeric).Value = taxa;
                cmd.Parameters.Add("nc", OleDbType.Char, 8).Value = (numConta ?? "").Trim();
                cmd.ExecuteNonQuery();
            }
        }
    }
}
