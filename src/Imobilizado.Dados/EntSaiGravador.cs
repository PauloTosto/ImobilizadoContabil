using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Globalization;

namespace Imobilizado.Dados
{
    /// <summary>
    /// CRUD do ENTSAI.DBF (movimentação física de produtos — kardex: entradas/saídas por código,
    /// editor Clipper FLT_ESTO). Estrutura: DATA D, COD C(4), ENT N(10,2), OBS_ENT C(15),
    /// SAI N(10,2), OBS_SAI C(15). Sem chave única — identidade = RECNO().
    /// </summary>
    public sealed class EntSaiGravador
    {
        private readonly string _pastaDados;
        private const string Tabela = "ENTSAI";

        public sealed class ItemEntSai
        {
            public int Recno;          // 0 = novo
            public string Data;        // "YYYYMMDD"
            public string Cod;
            public decimal Ent, Sai;
            public string ObsEnt, ObsSai;
        }

        public EntSaiGravador(string pastaDados)
        {
            _pastaDados = pastaDados ?? throw new ArgumentNullException(nameof(pastaDados));
        }

        public List<ItemEntSai> Ler()
        {
            var lista = new List<ItemEntSai>();
            using (var con = ConexaoVfp.Abrir(_pastaDados))
            using (var cmd = new OleDbCommand($"SELECT RECNO() AS NRECNO, DATA, COD, ENT, OBS_ENT, SAI, OBS_SAI FROM {Tabela}", con))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read())
                    lista.Add(new ItemEntSai
                    {
                        Recno = Convert.ToInt32(rd["NRECNO"], CultureInfo.InvariantCulture),
                        Data = rd["DATA"] == DBNull.Value ? "" : ((DateTime)rd["DATA"]).ToString("yyyyMMdd"),
                        Cod = (rd["COD"] as string ?? "").Trim(),
                        Ent = rd["ENT"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["ENT"], CultureInfo.InvariantCulture),
                        Sai = rd["SAI"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["SAI"], CultureInfo.InvariantCulture),
                        ObsEnt = (rd["OBS_ENT"] as string ?? "").Trim(),
                        ObsSai = (rd["OBS_SAI"] as string ?? "").Trim(),
                    });
            return lista;
        }

        public void Incluir(ItemEntSai i)
        {
            using (var con = ConexaoVfp.Abrir(_pastaDados))
            using (var cmd = new OleDbCommand(
                $"INSERT INTO {Tabela} (DATA, COD, ENT, OBS_ENT, SAI, OBS_SAI) VALUES (?,?,?,?,?,?)", con))
            {
                Parms(cmd, i);
                cmd.ExecuteNonQuery();
            }
        }

        public void Alterar(ItemEntSai i)
        {
            using (var con = ConexaoVfp.Abrir(_pastaDados))
            using (var cmd = new OleDbCommand(
                $"UPDATE {Tabela} SET DATA=?, COD=?, ENT=?, OBS_ENT=?, SAI=?, OBS_SAI=? WHERE RECNO()=?", con))
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

        private static void Parms(OleDbCommand cmd, ItemEntSai i)
        {
            cmd.Parameters.Add("dt", OleDbType.Date).Value = DateTime.ParseExact(i.Data, "yyyyMMdd", CultureInfo.InvariantCulture);
            cmd.Parameters.Add("cod", OleDbType.Char, 4).Value = (i.Cod ?? "").Trim();
            cmd.Parameters.Add("ent", OleDbType.Numeric).Value = i.Ent;
            cmd.Parameters.Add("oe", OleDbType.Char, 15).Value = (i.ObsEnt ?? "").Trim();
            cmd.Parameters.Add("sai", OleDbType.Numeric).Value = i.Sai;
            cmd.Parameters.Add("os", OleDbType.Char, 15).Value = (i.ObsSai ?? "").Trim();
        }
    }
}
