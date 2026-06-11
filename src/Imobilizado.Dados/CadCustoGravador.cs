using System;
using System.Data.OleDb;

namespace Imobilizado.Dados
{
    /// <summary>
    /// CRUD do CADCUSTO.DBF (cadastro de produtos/custos da Apropriação) via VFPOLEDB.
    /// Estrutura: COD C(4) chave, DESC C(25), PRODUCAO/EMCURSO/RECEITA/CUSTOVENDA C(8),
    /// UNID C(12), ESTOQUE N(10,2), DATA D, PERC1..PERC4 N(6,2).
    /// DESC é palavra reservada do SQL do VFP — sempre qualificado (CADCUSTO.DESC).
    /// Excluir marca como deletado (soft delete padrão xBase — o PACK fica pro Clipper/VFP).
    /// </summary>
    public sealed class CadCustoGravador
    {
        private readonly string _pastaDados;
        private const string Tabela = "CADCUSTO";

        public sealed class ItemCadCusto
        {
            public string Cod, Desc, Producao, EmCurso, Receita, CustoVenda, Unid;
            public decimal Estoque, Perc1, Perc2, Perc3, Perc4;
            public DateTime? Data;
        }

        public CadCustoGravador(string pastaDados)
        {
            _pastaDados = pastaDados ?? throw new ArgumentNullException(nameof(pastaDados));
        }

        public bool Existe(string cod)
        {
            using (var con = ConexaoVfp.Abrir(_pastaDados))
            using (var cmd = new OleDbCommand($"SELECT COUNT(*) FROM {Tabela} WHERE COD=?", con))
            {
                cmd.Parameters.Add("cod", OleDbType.Char, 4).Value = (cod ?? "").Trim();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        public void Incluir(ItemCadCusto i)
        {
            using (var con = ConexaoVfp.Abrir(_pastaDados))
            using (var cmd = new OleDbCommand { Connection = con })
            {
                string dData = DataSql(cmd, "DATA", i.Data);
                Campos(cmd, i);
                P(cmd, "COD", 4, i.Cod);
                cmd.CommandText =
                    $"INSERT INTO {Tabela} (DATA, DESC, PRODUCAO, EMCURSO, RECEITA, CUSTOVENDA, UNID, " +
                    "ESTOQUE, PERC1, PERC2, PERC3, PERC4, COD) VALUES (" +
                    $"{dData}, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
                cmd.ExecuteNonQuery();
            }
        }

        public void Alterar(ItemCadCusto i)
        {
            using (var con = ConexaoVfp.Abrir(_pastaDados))
            using (var cmd = new OleDbCommand { Connection = con })
            {
                string dData = DataSql(cmd, "DATA", i.Data);
                Campos(cmd, i);
                P(cmd, "COD", 4, i.Cod);
                cmd.CommandText =
                    $"UPDATE {Tabela} SET DATA={dData}, {Tabela}.DESC=?, PRODUCAO=?, EMCURSO=?, RECEITA=?, " +
                    "CUSTOVENDA=?, UNID=?, ESTOQUE=?, PERC1=?, PERC2=?, PERC3=?, PERC4=? WHERE COD=?";
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Marca o registro como deletado (soft delete xBase).</summary>
        public void Excluir(string cod)
        {
            using (var con = ConexaoVfp.Abrir(_pastaDados))
            using (var cmd = new OleDbCommand($"DELETE FROM {Tabela} WHERE COD=?", con))
            {
                cmd.Parameters.Add("cod", OleDbType.Char, 4).Value = (cod ?? "").Trim();
                cmd.ExecuteNonQuery();
            }
        }

        // parâmetros na MESMA ordem dos ? de INSERT/UPDATE (DESC..PERC4; COD é adicionado depois)
        private static void Campos(OleDbCommand cmd, ItemCadCusto i)
        {
            P(cmd, "DESC", 25, i.Desc);
            P(cmd, "PRODUCAO", 8, i.Producao);
            P(cmd, "EMCURSO", 8, i.EmCurso);
            P(cmd, "RECEITA", 8, i.Receita);
            P(cmd, "CUSTOVENDA", 8, i.CustoVenda);
            P(cmd, "UNID", 12, i.Unid);
            N(cmd, "ESTOQUE", i.Estoque);
            N(cmd, "PERC1", i.Perc1);
            N(cmd, "PERC2", i.Perc2);
            N(cmd, "PERC3", i.Perc3);
            N(cmd, "PERC4", i.Perc4);
        }

        private static string DataSql(OleDbCommand cmd, string nome, DateTime? val)
        {
            if (!val.HasValue) return "CTOD('')";
            cmd.Parameters.Add(nome, OleDbType.Date).Value = val.Value;
            return "?";
        }

        private static void P(OleDbCommand cmd, string nome, int tam, string val)
            => cmd.Parameters.Add(nome, OleDbType.Char, tam).Value = (val ?? "").Trim();

        private static void N(OleDbCommand cmd, string nome, decimal val)
            => cmd.Parameters.Add(nome, OleDbType.Numeric).Value = val;
    }
}
