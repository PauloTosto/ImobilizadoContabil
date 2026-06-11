using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Globalization;

namespace Imobilizado.Dados
{
    /// <summary>
    /// CRUD do RELAC.DBF (relações débito×crédito da absorção de custo — editor Clipper FLT_REL).
    /// Estrutura: DEBITO C(8), CREDITO C(8), FUNCAO C(1) ('A' = absorção), QUANT1 N(5,2).
    /// NÃO há chave única (o mesmo par pode repetir com %s diferentes) — identidade = RECNO().
    /// </summary>
    public sealed class RelacGravador
    {
        private readonly string _pastaDados;
        private const string Tabela = "RELAC";

        public sealed class ItemRelac
        {
            public int Recno;        // 0 = novo
            public string Debito, Credito, Funcao;
            public decimal Quant1;
        }

        public RelacGravador(string pastaDados)
        {
            _pastaDados = pastaDados ?? throw new ArgumentNullException(nameof(pastaDados));
        }

        public List<ItemRelac> Ler()
        {
            var lista = new List<ItemRelac>();
            using (var con = ConexaoVfp.Abrir(_pastaDados))
            using (var cmd = new OleDbCommand($"SELECT RECNO() AS NRECNO, DEBITO, CREDITO, FUNCAO, QUANT1 FROM {Tabela}", con))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read())
                    lista.Add(new ItemRelac
                    {
                        Recno = Convert.ToInt32(rd["NRECNO"], CultureInfo.InvariantCulture),
                        Debito = (rd["DEBITO"] as string ?? "").Trim(),
                        Credito = (rd["CREDITO"] as string ?? "").Trim(),
                        Funcao = (rd["FUNCAO"] as string ?? "").Trim(),
                        Quant1 = rd["QUANT1"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["QUANT1"], CultureInfo.InvariantCulture),
                    });
            return lista;
        }

        public void Incluir(ItemRelac i)
        {
            using (var con = ConexaoVfp.Abrir(_pastaDados))
            using (var cmd = new OleDbCommand(
                $"INSERT INTO {Tabela} (DEBITO, CREDITO, FUNCAO, QUANT1) VALUES (?,?,?,?)", con))
            {
                Parms(cmd, i);
                cmd.ExecuteNonQuery();
            }
        }

        public void Alterar(ItemRelac i)
        {
            using (var con = ConexaoVfp.Abrir(_pastaDados))
            using (var cmd = new OleDbCommand(
                $"UPDATE {Tabela} SET DEBITO=?, CREDITO=?, FUNCAO=?, QUANT1=? WHERE RECNO()=?", con))
            {
                Parms(cmd, i);
                cmd.Parameters.Add("rec", OleDbType.Integer).Value = i.Recno;
                cmd.ExecuteNonQuery();
            }
        }

        public void Excluir(int recno)
        {
            using (var con = ConexaoVfp.Abrir(_pastaDados))
            using (var cmd = new OleDbCommand($"DELETE FROM {Tabela} WHERE RECNO()=?", con))
            {
                cmd.Parameters.Add("rec", OleDbType.Integer).Value = recno;
                cmd.ExecuteNonQuery();
            }
        }

        private static void Parms(OleDbCommand cmd, ItemRelac i)
        {
            cmd.Parameters.Add("deb", OleDbType.Char, 8).Value = (i.Debito ?? "").Trim();
            cmd.Parameters.Add("cre", OleDbType.Char, 8).Value = (i.Credito ?? "").Trim();
            cmd.Parameters.Add("fun", OleDbType.Char, 1).Value = (i.Funcao ?? "").Trim();
            cmd.Parameters.Add("qua", OleDbType.Numeric).Value = i.Quant1;
        }
    }
}
