using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Linq;
using Imobilizado.Core.Dominio;

namespace Imobilizado.Dados
{
    /// <summary>
    /// Grava lançamentos de depreciação no MOVFIN.DBF via VFPOLEDB.
    ///
    /// Espelha o padrão provado do ApoioContabil2020 (FrmTransfereContabilidade):
    /// MAX(MOV_ID)+1 sequencial e inserção via OleDbDataAdapter. Usar VFPOLEDB (e
    /// não escrita crua de bytes) é o que mantém o índice estrutural .CDX em dia —
    /// essencial porque o Clipper/Ju2 e o apoioparceiro leem a mesma tabela.
    ///
    /// AVISO: MOVFIN é dado contábil de produção. Por padrão NÃO grava (dry-run);
    /// a gravação efetiva exige <paramref name="gravarDeFato"/> = true em <see cref="Gravar"/>.
    /// </summary>
    public sealed class MovfinGravador
    {
        private readonly string _pastaDados;
        private const string Tabela = "MOVFIN";

        public MovfinGravador(string pastaDados)
        {
            _pastaDados = pastaDados ?? throw new ArgumentNullException(nameof(pastaDados));
        }

        private OleDbConnection AbrirConexao()
        {
            var cs = $"Provider=VFPOLEDB.1;Data Source={_pastaDados}";
            var con = new OleDbConnection(cs);
            con.Open();
            return con;
        }

        /// <summary>Maior MOV_ID atual da tabela (0 se vazia).</summary>
        public decimal MaiorMovId()
        {
            using (var con = AbrirConexao())
            using (var cmd = new OleDbCommand($"SELECT MAX(MOV_ID) FROM {Tabela}", con))
            {
                var r = cmd.ExecuteScalar();
                return (r == null || r == DBNull.Value) ? 0m : Convert.ToDecimal(r, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Conta os lançamentos SIST_IMOB já existentes na competência.</summary>
        public int ContarDepreciacaoExistente(AnoMes competencia)
        {
            using (var con = AbrirConexao())
            using (var cmd = new OleDbCommand(
                $"SELECT COUNT(*) FROM {Tabela} WHERE DOC=? AND YEAR(DATA)=? AND MONTH(DATA)=?", con))
            {
                cmd.Parameters.AddWithValue("doc", LancamentoDepreciacao.Doc);
                cmd.Parameters.AddWithValue("ano", competencia.Ano);
                cmd.Parameters.AddWithValue("mes", competencia.Mes);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        /// <summary>Marca como deletados os SIST_IMOB da competência (para reprocessar o mês).</summary>
        public int ExcluirDepreciacao(AnoMes competencia)
        {
            using (var con = AbrirConexao())
            using (var cmd = new OleDbCommand(
                $"DELETE FROM {Tabela} WHERE DOC=? AND YEAR(DATA)=? AND MONTH(DATA)=?", con))
            {
                cmd.Parameters.AddWithValue("doc", LancamentoDepreciacao.Doc);
                cmd.Parameters.AddWithValue("ano", competencia.Ano);
                cmd.Parameters.AddWithValue("mes", competencia.Mes);
                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Grava os lançamentos no MOVFIN. Retorna quantas linhas seriam/foram inseridas.
        /// </summary>
        /// <param name="competencia">Competência (usada na exclusão ao substituir).</param>
        /// <param name="lancamentos">Lançamentos já calculados pelo motor.</param>
        /// <param name="gravarDeFato">false = dry-run (não escreve nada). true = insere.</param>
        /// <param name="substituirExistentes">true = exclui os SIST_IMOB do mês antes de inserir.</param>
        public int Gravar(AnoMes competencia, IReadOnlyList<LancamentoDepreciacao> lancamentos,
                          bool gravarDeFato, bool substituirExistentes)
        {
            if (lancamentos == null || lancamentos.Count == 0) return 0;
            if (!gravarDeFato) return lancamentos.Count; // dry-run

            var linhas = lancamentos.Select(l => new LinhaMovfin
            {
                Data = l.Data, Debito = l.Debito, Credito = l.Credito,
                Valor = l.Valor, Historico = l.Historico, Doc = LancamentoDepreciacao.Doc,
            }).ToList();

            using (var con = AbrirConexao())
            {
                if (substituirExistentes)
                {
                    using (var del = new OleDbCommand(
                        $"DELETE FROM {Tabela} WHERE DOC=? AND YEAR(DATA)=? AND MONTH(DATA)=?", con))
                    {
                        del.Parameters.AddWithValue("doc", LancamentoDepreciacao.Doc);
                        del.Parameters.AddWithValue("ano", competencia.Ano);
                        del.Parameters.AddWithValue("mes", competencia.Mes);
                        del.ExecuteNonQuery();
                    }
                }
                return InserirLote(con, linhas);
            }
        }

        // ---------- caminho genérico (usado por depreciação e apropriação) ----------

        /// <summary>Conta lançamentos de um DOC num ano.</summary>
        public int ContarPorDocAno(string doc, int ano)
        {
            using (var con = AbrirConexao())
            using (var cmd = new OleDbCommand($"SELECT COUNT(*) FROM {Tabela} WHERE DOC=? AND YEAR(DATA)=?", con))
            {
                cmd.Parameters.AddWithValue("doc", doc);
                cmd.Parameters.AddWithValue("ano", ano);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// Grava um lote de lançamentos no MOVFIN (genérico — DOC vem de cada linha).
        /// Se <paramref name="substituirDocAno"/> != null, exclui antes os lançamentos
        /// daquele DOC/ano. Retorna quantas linhas foram (ou seriam) inseridas.
        /// </summary>
        public int GravarLote(IReadOnlyList<LinhaMovfin> linhas, bool gravarDeFato,
                              (string doc, int ano)? substituirDocAno = null)
        {
            if (linhas == null || linhas.Count == 0) return 0;
            if (!gravarDeFato) return linhas.Count; // dry-run

            using (var con = AbrirConexao())
            {
                if (substituirDocAno is (string doc, int ano))
                {
                    using (var del = new OleDbCommand($"DELETE FROM {Tabela} WHERE DOC=? AND YEAR(DATA)=?", con))
                    {
                        del.Parameters.AddWithValue("doc", doc);
                        del.Parameters.AddWithValue("ano", ano);
                        del.ExecuteNonQuery();
                    }
                }
                return InserirLote(con, linhas);
            }
        }

        /// <summary>
        /// Insere as linhas via OleDbDataAdapter (padrão ApoioContabil2020), com MAX(MOV_ID)+1
        /// sequencial. Todas as colunas recebem default não-nulo (datas = DateTime.MinValue).
        /// </summary>
        private int InserirLote(OleDbConnection con, IReadOnlyList<LinhaMovfin> linhas)
        {
            decimal movId;
            using (var cmdMax = new OleDbCommand($"SELECT MAX(MOV_ID) FROM {Tabela}", con))
            {
                var r = cmdMax.ExecuteScalar();
                movId = (r == null || r == DBNull.Value) ? 0m : Convert.ToDecimal(r, CultureInfo.InvariantCulture);
            }

            var adapter = new OleDbDataAdapter($"SELECT * FROM {Tabela} WHERE MOV_ID = -999999999", con);
            using (var builder = new OleDbCommandBuilder(adapter))
            {
                adapter.InsertCommand = builder.GetInsertCommand();
                var ds = new DataSet();
                adapter.Fill(ds, Tabela);
                var dt = ds.Tables[Tabela];

                foreach (var l in linhas)
                {
                    var row = dt.NewRow();
                    InicializarDefaults(row, dt);
                    row["DATA"] = DateTime.ParseExact(l.Data, "yyyyMMdd", CultureInfo.InvariantCulture);
                    row["VALOR"] = l.Valor;
                    row["DEBITO"] = l.Debito;
                    row["CREDITO"] = l.Credito;
                    row["DOC"] = l.Doc;
                    row["HIST"] = l.Historico ?? "";
                    row["TP_FIN"] = false;       // depreciação/apropriação não são movimento financeiro
                    row["MOV_ID"] = (movId += 1);
                    dt.Rows.Add(row);
                }
                return adapter.Update(dt);
            }
        }

        // ---------- edição manual de lançamentos (partida simples) ----------

        /// <summary>Lê os lançamentos de um período (datas "YYYYMMDD"); filtra por TP_FIN se informado.</summary>
        public System.Collections.Generic.List<LancamentoMovfin> LerPeriodo(string data1, string data2, bool? tpFin)
        {
            var lista = new System.Collections.Generic.List<LancamentoMovfin>();
            var sql = $"SELECT RECNO() AS NRECNO, MOV_ID, OUTRO_ID, DATA, VALOR, DEBITO, CREDITO, HIST, DOC, FORN, TIPO, TP_FIN, VENC, DOC_FISC, EMISSOR, DATA_EMI " +
                      $"FROM {Tabela} WHERE DATA >= ? AND DATA <= ?" + (tpFin.HasValue ? " AND TP_FIN = ?" : "");
            using (var con = AbrirConexao())
            using (var cmd = new OleDbCommand(sql, con))
            {
                cmd.Parameters.Add("d1", OleDbType.Date).Value = ParseData(data1);
                cmd.Parameters.Add("d2", OleDbType.Date).Value = ParseData(data2);
                if (tpFin.HasValue) cmd.Parameters.Add("tf", OleDbType.Boolean).Value = tpFin.Value;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        lista.Add(new LancamentoMovfin
                        {
                            Recno = rd["NRECNO"] == DBNull.Value ? 0 : Convert.ToInt32(rd["NRECNO"], CultureInfo.InvariantCulture),
                            MovId = rd["MOV_ID"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["MOV_ID"], CultureInfo.InvariantCulture),
                            OutroId = rd["OUTRO_ID"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["OUTRO_ID"], CultureInfo.InvariantCulture),
                            Data = FmtData(rd["DATA"]),
                            Valor = rd["VALOR"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["VALOR"], CultureInfo.InvariantCulture),
                            Debito = (rd["DEBITO"] as string ?? "").Trim(),
                            Credito = (rd["CREDITO"] as string ?? "").Trim(),
                            Historico = (rd["HIST"] as string ?? "").Trim(),
                            Doc = (rd["DOC"] as string ?? "").Trim(),
                            Forn = (rd["FORN"] as string ?? "").Trim(),
                            Tipo = (rd["TIPO"] as string ?? "").Trim(),
                            TpFin = rd["TP_FIN"] != DBNull.Value && Convert.ToBoolean(rd["TP_FIN"]),
                            Venc = FmtData(rd["VENC"]),
                            DocFisc = (rd["DOC_FISC"] as string ?? "").Trim(),
                            Emissor = (rd["EMISSOR"] as string ?? "").Trim(),
                            DataEmi = FmtData(rd["DATA_EMI"]),
                        });
            }
            return lista;
        }

        /// <summary>Inclui um lançamento manual com MAX(MOV_ID)+1.</summary>
        public decimal InserirLancamento(LancamentoMovfin l)
        {
            using (var con = AbrirConexao())
            {
                decimal movId;
                using (var cmdMax = new OleDbCommand($"SELECT MAX(MOV_ID) FROM {Tabela}", con))
                {
                    var r = cmdMax.ExecuteScalar();
                    movId = (r == null || r == DBNull.Value) ? 0m : Convert.ToDecimal(r, CultureInfo.InvariantCulture);
                }
                movId += 1;
                var adapter = new OleDbDataAdapter($"SELECT * FROM {Tabela} WHERE MOV_ID = -999999999", con);
                using (var builder = new OleDbCommandBuilder(adapter))
                {
                    adapter.InsertCommand = builder.GetInsertCommand();
                    var ds = new DataSet(); adapter.Fill(ds, Tabela);
                    var dt = ds.Tables[Tabela];
                    var row = dt.NewRow();
                    InicializarDefaults(row, dt);
                    AplicarCampos(row, l);
                    row["MOV_ID"] = movId;
                    dt.Rows.Add(row);
                    adapter.Update(dt);
                }
                return movId;
            }
        }

        /// <summary>Altera um lançamento existente (por MOV_ID). Preserva a identidade.</summary>
        public void AlterarLancamento(LancamentoMovfin l)
        {
            bool temVenc = !string.IsNullOrWhiteSpace(l.Venc);
            bool temEmi = !string.IsNullOrWhiteSpace(l.DataEmi);
            var sql = $"UPDATE {Tabela} SET DATA=?, VALOR=?, DEBITO=?, CREDITO=?, DOC=?, HIST=?, TIPO=?, " +
                      $"TP_FIN=?, FORN=?, DOC_FISC=?, EMISSOR=?, VENC={(temVenc ? "?" : "CTOD('')")}, " +
                      $"DATA_EMI={(temEmi ? "?" : "CTOD('')")} WHERE RECNO()=?";
            using (var con = AbrirConexao())
            using (var cmd = new OleDbCommand(sql, con))
            {
                cmd.Parameters.Add("dt", OleDbType.Date).Value = ParseData(l.Data);
                cmd.Parameters.Add("vl", OleDbType.Numeric).Value = l.Valor;
                cmd.Parameters.Add("db", OleDbType.Char, 25).Value = (l.Debito ?? "").Trim();
                cmd.Parameters.Add("cr", OleDbType.Char, 25).Value = (l.Credito ?? "").Trim();
                cmd.Parameters.Add("dc", OleDbType.Char, 13).Value = (l.Doc ?? "").Trim();
                cmd.Parameters.Add("hi", OleDbType.Char, 40).Value = (l.Historico ?? "").Trim();
                cmd.Parameters.Add("tp", OleDbType.Char, 1).Value = (l.Tipo ?? "").Trim();
                cmd.Parameters.Add("tf", OleDbType.Boolean).Value = l.TpFin;
                cmd.Parameters.Add("fo", OleDbType.Char, 35).Value = (l.Forn ?? "").Trim();
                cmd.Parameters.Add("df", OleDbType.Char, 13).Value = (l.DocFisc ?? "").Trim();
                cmd.Parameters.Add("em", OleDbType.Char, 8).Value = (l.Emissor ?? "").Trim();
                if (temVenc) cmd.Parameters.Add("vc", OleDbType.Date).Value = ParseData(l.Venc);
                if (temEmi) cmd.Parameters.Add("de", OleDbType.Date).Value = ParseData(l.DataEmi);
                cmd.Parameters.Add("rec", OleDbType.Integer).Value = l.Recno;   // RECNO é a identidade única
                cmd.ExecuteNonQuery();
            }
        }

        public void ExcluirLancamento(int recno)
        {
            using (var con = AbrirConexao())
            using (var cmd = new OleDbCommand($"DELETE FROM {Tabela} WHERE RECNO()=?", con))
            {
                cmd.Parameters.Add("rec", OleDbType.Integer).Value = recno;
                cmd.ExecuteNonQuery();
            }
        }

        private static void AplicarCampos(DataRow row, LancamentoMovfin l)
        {
            row["DATA"] = ParseData(l.Data);
            row["VALOR"] = l.Valor;
            row["DEBITO"] = (l.Debito ?? "").Trim();
            row["CREDITO"] = (l.Credito ?? "").Trim();
            row["DOC"] = (l.Doc ?? "").Trim();
            row["HIST"] = (l.Historico ?? "").Trim();
            row["TIPO"] = (l.Tipo ?? "").Trim();
            row["TP_FIN"] = l.TpFin;
            row["FORN"] = (l.Forn ?? "").Trim();
            row["DOC_FISC"] = (l.DocFisc ?? "").Trim();
            row["EMISSOR"] = (l.Emissor ?? "").Trim();
            row["OUTRO_ID"] = l.OutroId;
            if (!string.IsNullOrWhiteSpace(l.Venc)) row["VENC"] = ParseData(l.Venc);
            if (!string.IsNullOrWhiteSpace(l.DataEmi)) row["DATA_EMI"] = ParseData(l.DataEmi);
        }

        // ---------- partida dobrada (lançamento composto: 1 mestre + N detalhes ligados por OUTRO_ID) ----------

        /// <summary>
        /// Inclui um lançamento composto: o mestre (uma conta, valor total, OUTRO_ID=0) e os
        /// detalhes (contrapartidas, cada um com OUTRO_ID = MOV_ID do mestre). MOV_IDs sequenciais.
        /// </summary>
        public decimal InserirComposto(LancamentoMovfin mestre, System.Collections.Generic.IReadOnlyList<LancamentoMovfin> detalhes)
        {
            using (var con = AbrirConexao())
            {
                decimal movId;
                using (var cmdMax = new OleDbCommand($"SELECT MAX(MOV_ID) FROM {Tabela}", con))
                {
                    var r = cmdMax.ExecuteScalar();
                    movId = (r == null || r == DBNull.Value) ? 0m : Convert.ToDecimal(r, CultureInfo.InvariantCulture);
                }
                decimal masterId = movId + 1;
                movId = masterId;   // o mestre consome este ID; detalhes começam em masterId+1
                var adapter = new OleDbDataAdapter($"SELECT * FROM {Tabela} WHERE MOV_ID = -999999999", con);
                using (var builder = new OleDbCommandBuilder(adapter))
                {
                    adapter.InsertCommand = builder.GetInsertCommand();
                    var ds = new DataSet(); adapter.Fill(ds, Tabela);
                    var dt = ds.Tables[Tabela];

                    mestre.OutroId = 0;
                    var rm = dt.NewRow(); InicializarDefaults(rm, dt); AplicarCampos(rm, mestre); rm["MOV_ID"] = masterId; dt.Rows.Add(rm);
                    foreach (var d in detalhes)
                    {
                        d.OutroId = masterId;
                        var rd = dt.NewRow(); InicializarDefaults(rd, dt); AplicarCampos(rd, d); rd["MOV_ID"] = (movId += 1); dt.Rows.Add(rd);
                    }
                    adapter.Update(dt);
                }
                return masterId;
            }
        }

        /// <summary>
        /// Lê um grupo composto (mestre + detalhes) por MOV_ID do mestre, ANCORADO na DATA.
        /// Regra do domínio (confirmada pelo Paulo): um composto é SEMPRE de um único dia —
        /// grupo multi-dia não existe. A âncora de data é essencial porque o MOV_ID do Clipper
        /// REINICIA a cada exercício (o mesmo valor reaparece em anos diferentes); sem ela a
        /// busca puxaria grupos homônimos de outros dias/anos (lixo de colisão). O DOC_FISC é a
        /// referência fiscal compartilhada pelas linhas, mas NÃO entra no filtro (nem sempre
        /// preenchido) — data + vínculo MOV_ID/OUTRO_ID já identificam o grupo unicamente no dia.
        /// </summary>
        public System.Collections.Generic.List<LancamentoMovfin> LerGrupo(decimal mestreMovId, string dataAnchor)
        {
            var lista = new System.Collections.Generic.List<LancamentoMovfin>();
            using (var con = AbrirConexao())
            using (var cmd = new OleDbCommand(
                $"SELECT RECNO() AS NRECNO, MOV_ID, OUTRO_ID, DATA, VALOR, DEBITO, CREDITO, HIST, DOC, FORN, TIPO, TP_FIN, VENC, DOC_FISC, EMISSOR, DATA_EMI " +
                $"FROM {Tabela} WHERE (MOV_ID=? OR OUTRO_ID=?) AND DATA=?", con))
            {
                cmd.Parameters.Add("a", OleDbType.Numeric).Value = mestreMovId;
                cmd.Parameters.Add("b", OleDbType.Numeric).Value = mestreMovId;
                cmd.Parameters.Add("dt", OleDbType.Date).Value = ParseData(dataAnchor);
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        lista.Add(new LancamentoMovfin
                        {
                            Recno = rd["NRECNO"] == DBNull.Value ? 0 : Convert.ToInt32(rd["NRECNO"], CultureInfo.InvariantCulture),
                            MovId = rd["MOV_ID"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["MOV_ID"], CultureInfo.InvariantCulture),
                            OutroId = rd["OUTRO_ID"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["OUTRO_ID"], CultureInfo.InvariantCulture),
                            Data = FmtData(rd["DATA"]), Valor = rd["VALOR"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["VALOR"], CultureInfo.InvariantCulture),
                            Debito = (rd["DEBITO"] as string ?? "").Trim(), Credito = (rd["CREDITO"] as string ?? "").Trim(),
                            Historico = (rd["HIST"] as string ?? "").Trim(), Doc = (rd["DOC"] as string ?? "").Trim(),
                            Forn = (rd["FORN"] as string ?? "").Trim(), Tipo = (rd["TIPO"] as string ?? "").Trim(),
                            TpFin = rd["TP_FIN"] != DBNull.Value && Convert.ToBoolean(rd["TP_FIN"]),
                            Venc = FmtData(rd["VENC"]), DocFisc = (rd["DOC_FISC"] as string ?? "").Trim(),
                            Emissor = (rd["EMISSOR"] as string ?? "").Trim(), DataEmi = FmtData(rd["DATA_EMI"]),
                        });
            }
            return lista;
        }

        /// <summary>
        /// True se algum lançamento NO MESMO DIA aponta (OUTRO_ID) para este MOV_ID — ou seja,
        /// é mestre de um grupo composto (que é sempre de um único dia). A âncora de data evita
        /// falso-positivo por um detalhe homônimo de outro dia/exercício (MOV_ID reinicia por ano).
        /// </summary>
        public bool TemDetalhes(decimal movId, string dataAnchor)
        {
            using (var con = AbrirConexao())
            using (var cmd = new OleDbCommand($"SELECT COUNT(*) FROM {Tabela} WHERE OUTRO_ID=? AND DATA=?", con))
            {
                cmd.Parameters.Add("o", OleDbType.Numeric).Value = movId;
                cmd.Parameters.Add("dt", OleDbType.Date).Value = ParseData(dataAnchor);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        /// <summary>
        /// True se existe um MESTRE (OUTRO_ID=0) com este MOV_ID neste DIA. Usado para confirmar
        /// que um detalhe tem um mestre coerente no mesmo dia antes de abrir o editor composto —
        /// se não tiver (detalhe órfão/artefato), o chamador trata como lançamento avulso.
        /// </summary>
        public bool MestreExiste(decimal movId, string dataAnchor)
        {
            using (var con = AbrirConexao())
            using (var cmd = new OleDbCommand($"SELECT COUNT(*) FROM {Tabela} WHERE MOV_ID=? AND OUTRO_ID=0 AND DATA=?", con))
            {
                cmd.Parameters.Add("m", OleDbType.Numeric).Value = movId;
                cmd.Parameters.Add("dt", OleDbType.Date).Value = ParseData(dataAnchor);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        /// <summary>
        /// Exclui um grupo composto inteiro (mestre + detalhes) ANCORADO na DATA. A âncora é
        /// crítica: sem ela, o DELETE por MOV_ID/OUTRO_ID apagaria grupos homônimos de outros
        /// dias/exercícios (MOV_ID reinicia por ano). No dia, o MOV_ID do mestre é único.
        /// </summary>
        public void ExcluirComposto(decimal mestreMovId, string dataAnchor)
        {
            using (var con = AbrirConexao())
            using (var cmd = new OleDbCommand($"DELETE FROM {Tabela} WHERE (MOV_ID=? OR OUTRO_ID=?) AND DATA=?", con))
            {
                cmd.Parameters.Add("a", OleDbType.Numeric).Value = mestreMovId;
                cmd.Parameters.Add("b", OleDbType.Numeric).Value = mestreMovId;
                cmd.Parameters.Add("dt", OleDbType.Date).Value = ParseData(dataAnchor);
                cmd.ExecuteNonQuery();
            }
        }

        private static DateTime ParseData(string yyyymmdd)
            => DateTime.ParseExact(yyyymmdd, "yyyyMMdd", CultureInfo.InvariantCulture);

        private static string FmtData(object dbVal)
        {
            if (dbVal == null || dbVal == DBNull.Value) return "";
            if (dbVal is DateTime d) return d.Year < 1900 ? "" : d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            return "";
        }

        /// <summary>
        /// Default não-nulo por tipo: char→"", numérico→0, lógico→false, data→DateTime.MinValue
        /// (data vazia do VFP). Espelha o PonhaValoresDefault do ApoioContabil2020 — campos
        /// como TIPO/VENC são NOT NULL e não aceitam DBNull.
        /// </summary>
        private static void InicializarDefaults(DataRow row, DataTable dt)
        {
            foreach (DataColumn col in dt.Columns)
            {
                var t = col.DataType;
                if (t == typeof(string)) row[col] = "";
                else if (t == typeof(DateTime)) row[col] = DateTime.MinValue;
                else if (t == typeof(bool)) row[col] = false;
                else if (t == typeof(decimal) || t == typeof(double) || t == typeof(float) ||
                         t == typeof(int) || t == typeof(short) || t == typeof(long) || t == typeof(byte))
                    row[col] = Convert.ChangeType(0, t, CultureInfo.InvariantCulture);
            }
        }
    }
}
