using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Contabil.Core;

namespace Contabil.Core.Apropriacao
{
    /// <summary>
    /// Motor de apropriação de custos — port do FLT_APRO (Clipper). Consome os saldos
    /// já apurados (EngineSaldo.ApurarPeriodo) + CADCUSTO + ENTSAI e gera os lançamentos
    /// SIST_APROP: custo de produção (transfere em-curso→produção por PERC1) e custo de
    /// venda (média ponderada por saída física, ou percentual da receita via PERC2/3/4).
    /// </summary>
    public sealed class MotorApropriacao
    {
        private struct Mov3 { public decimal EstoqueIni, Entradas, Saidas; }

        public IReadOnlyList<LancamentoApropriacao> Gerar(
            IReadOnlyList<ProdutoCusto> produtos,
            Dictionary<string, List<MovimentoEstoque>> movPorProduto,
            Dictionary<string, EngineSaldo.Apuracao> apuracao,
            string data1, string data2)
        {
            var lanc = new List<LancamentoApropriacao>();
            var dataLanc = data2;

            // ---------- Fase 1: levantamento do movimento físico ----------
            var mat = new Dictionary<string, Mov3>(StringComparer.OrdinalIgnoreCase); // por PRODUCAO
            var codComMovimento = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in produtos)
            {
                if (Vazio(p.Producao)) continue;
                var key = p.Producao;
                if (!mat.ContainsKey(key)) mat[key] = new Mov3();
                var m = mat[key];

                if (Antes(p.Data, data1) && !string.IsNullOrEmpty(p.Data))
                {
                    m.EstoqueIni += p.Estoque;
                    if (p.Estoque != 0) codComMovimento.Add(p.Cod);
                }
                if (movPorProduto.TryGetValue(p.Cod, out var movs))
                {
                    foreach (var mv in movs)
                    {
                        if (mv.Data.Length < 8) continue;
                        if (Antes(mv.Data, data1))
                        {
                            m.EstoqueIni += mv.Ent - mv.Sai;
                            codComMovimento.Add(p.Cod);
                        }
                        else if (!Depois(mv.Data, data2))
                        {
                            m.Entradas += mv.Ent;
                            m.Saidas += mv.Sai;
                            codComMovimento.Add(p.Cod);
                        }
                    }
                }
                mat[key] = m;
            }
            // M_Produto = contas de produção com algum movimento
            var mProduto = mat.Where(kv => kv.Value.EstoqueIni != 0 || kv.Value.Entradas != 0 || kv.Value.Saidas != 0)
                              .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            // ---------- Fase 2: custo de produção (PERC1, em-curso → produção) ----------
            foreach (var p in produtos)
                CustoProducao(p, codComMovimento, apuracao, lanc, dataLanc);

            // ---------- Fase 3: custo de venda ----------
            foreach (var p in produtos)
            {
                if (!Vazio(p.Producao) && mProduto.ContainsKey(p.Producao) && codComMovimento.Contains(p.Cod))
                    CustoVendaFisico(p, mProduto, apuracao, movPorProduto, lanc, data1, data2, dataLanc);
                else
                    CustoVendaPercentual(p, apuracao, lanc, dataLanc);
            }
            return lanc;
        }

        private void CustoProducao(ProdutoCusto p, HashSet<string> codComMovimento,
            Dictionary<string, EngineSaldo.Apuracao> ap, List<LancamentoApropriacao> lanc, string data)
        {
            if (Vazio(p.EmCurso) || Vazio(p.Producao)) return;
            if (!codComMovimento.Contains(p.Cod))
            {
                // sem movimento físico: só lança se houver receita no período
                if (!ap.TryGetValue(p.Receita ?? "", out var rec) || !rec.TeveMovimento) return;
            }
            if (!ap.TryGetValue(p.EmCurso, out var emc)) return;
            var tsdo = emc.SaldoFinal;
            var val = tsdo;
            if (p.Perc1 != 0) val = Math.Round(val * p.Perc1 / 100m, 2, MidpointRounding.AwayFromZero);
            val = DisponibCredito(p.EmCurso, tsdo, val, lanc);
            if (val > 0)
                lanc.Add(new LancamentoApropriacao { Data = data, Debito = p.Producao, Credito = p.EmCurso, Valor = val, Historico = "CUSTO DE PRODUCAO" });
        }

        private void CustoVendaFisico(ProdutoCusto p, Dictionary<string, Mov3> mProduto,
            Dictionary<string, EngineSaldo.Apuracao> ap, Dictionary<string, List<MovimentoEstoque>> movPorProduto,
            List<LancamentoApropriacao> lanc, string data1, string data2, string dataLanc)
        {
            ap.TryGetValue(p.Producao, out var prodAp);
            decimal tsdo = prodAp.SaldoAnterior;     // VAL1
            var m = mProduto[p.Producao];
            decimal tmed = 0, tmed2 = 0, tsdoCurso = 0;

            if (m.EstoqueIni >= m.Saidas)
            {
                tmed = Div(tsdo, m.EstoqueIni, 2);
            }
            else if (m.EstoqueIni + m.Entradas >= m.Saidas)
            {
                if (!Vazio(p.EmCurso) && ap.TryGetValue(p.EmCurso, out var emc))
                    tsdoCurso = Math.Round(emc.SaldoFinal * p.Perc1 / 100m, 2, MidpointRounding.AwayFromZero);

                if (m.EstoqueIni < 0)
                {
                    tmed2 = Div(tsdoCurso, m.Entradas, 4); tmed = tmed2;
                }
                else if (m.EstoqueIni + m.Entradas == m.Saidas)
                {
                    tmed2 = Div(tsdoCurso + tsdo, m.Entradas + m.EstoqueIni, 5); tmed = tmed2;
                }
                else
                {
                    tmed = Div(tsdo, m.EstoqueIni, 5);
                    tmed2 = Div(tsdoCurso, m.Entradas, 5);
                }
            }
            // else: erro estoque negativo (não calcula média)

            int iInicio = lanc.Count;
            decimal totEstoq = m.EstoqueIni;
            decimal tsdoZero = 0;
            var entradasRestante = m.Entradas;
            if (movPorProduto.TryGetValue(p.Cod, out var movs))
            {
                foreach (var mv in movs)
                {
                    if (mv.Data.Length < 8 || Antes(mv.Data, data1) || Depois(mv.Data, data2)) continue;
                    if (mv.Sai == 0) continue;
                    decimal valor;
                    if (mv.Sai > totEstoq && totEstoq > 0)
                    {
                        valor = Math.Round(totEstoq * tmed, 2, MidpointRounding.AwayFromZero)
                              + Math.Round((mv.Sai - totEstoq) * tmed2, 2, MidpointRounding.AwayFromZero);
                        totEstoq = 0;
                    }
                    else if (mv.Sai > totEstoq)
                    {
                        valor = Math.Round(tmed2 * mv.Sai, 2, MidpointRounding.AwayFromZero);
                        entradasRestante -= mv.Sai;
                    }
                    else
                    {
                        valor = Math.Round(tmed * mv.Sai, 2, MidpointRounding.AwayFromZero);
                        totEstoq -= mv.Sai;
                    }
                    var hist = "DB/CR " + (Vazio(mv.ObsSai)
                        ? "CUSTO DE VENDA" + mv.Sai.ToString("F2", CultureInfo.InvariantCulture).PadLeft(10) + p.Unid
                        : mv.ObsSai);
                    lanc.Add(new LancamentoApropriacao { Data = dataLanc, Debito = p.CustoVenda, Credito = p.Producao, Valor = valor, Historico = Trunc(hist, 40) });
                    tsdoZero += valor;
                }
            }
            // ajuste de arredondamento quando o consumo é exato
            if (iInicio < lanc.Count && (m.EstoqueIni + m.Entradas == m.Saidas) && tsdoZero != (tsdoCurso + tsdo))
                lanc[lanc.Count - 1].Valor += (tsdoCurso + tsdo) - tsdoZero;
        }

        private void CustoVendaPercentual(ProdutoCusto p, Dictionary<string, EngineSaldo.Apuracao> ap,
            List<LancamentoApropriacao> lanc, string data)
        {
            if (!ap.TryGetValue(p.Receita ?? "", out var rec) || !rec.TeveMovimento) return;
            decimal valLanc = rec.MovimentoLiquidoCredor; // VAL3 - VAL2
            string taprop = null;
            decimal tsdo = 0;

            if (p.Perc2 != 0 && valLanc != 0)
            {
                valLanc = valLanc * p.Perc2 / 100m;
                if (!Vazio(p.Producao))
                {
                    if (ap.TryGetValue(p.Producao, out var a)) tsdo = a.SaldoFinal;
                    tsdo += lanc.Where(l => l.Debito == p.Producao).Sum(l => l.Valor);
                    valLanc = DisponibCredito(p.Producao, tsdo, valLanc, lanc);
                    taprop = p.Producao;
                }
                else if (!Vazio(p.EmCurso))
                {
                    if (ap.TryGetValue(p.EmCurso, out var a)) tsdo = a.SaldoFinal;
                    valLanc = DisponibCredito(p.EmCurso, tsdo, valLanc, lanc);
                    taprop = p.EmCurso;
                }
            }
            else if (p.Perc3 != 0 && valLanc != 0)
            {
                valLanc = 0;
                if (!Vazio(p.EmCurso) && ap.TryGetValue(p.EmCurso, out var a))
                {
                    tsdo = a.SaldoFinal;
                    valLanc = Math.Round(tsdo * p.Perc3 / 100m, 2, MidpointRounding.AwayFromZero);
                    valLanc = DisponibCredito(p.EmCurso, tsdo, valLanc, lanc);
                    taprop = p.EmCurso;
                }
            }
            else if (p.Perc4 != 0 && valLanc != 0)
            {
                valLanc = 0;
                if (!Vazio(p.Producao) && ap.TryGetValue(p.Producao, out var a))
                {
                    tsdo = a.SaldoFinal;
                    tsdo += lanc.Where(l => l.Debito == p.Producao).Sum(l => l.Valor);
                    valLanc = Math.Round(tsdo * p.Perc4 / 100m, 2, MidpointRounding.AwayFromZero);
                    valLanc = DisponibCredito(p.Producao, tsdo, valLanc, lanc);
                    taprop = p.Producao;
                }
            }
            else return;

            if (valLanc > 0 && taprop != null)
                lanc.Add(new LancamentoApropriacao { Data = data, Debito = p.CustoVenda, Credito = taprop, Valor = valLanc, Historico = "DB/CR CUSTO DE VENDA" });
        }

        /// <summary>Limita o débito a uma conta ao seu saldo: total já creditado nela neste lote + val não pode passar de tsdo.</summary>
        private static decimal DisponibCredito(string conta, decimal tsdo, decimal valLanc, List<LancamentoApropriacao> lanc)
        {
            var sdoUtil = lanc.Where(l => l.Credito == conta).Sum(l => l.Valor);
            if (valLanc + sdoUtil > tsdo) valLanc = tsdo - sdoUtil;
            return valLanc;
        }

        private static bool Vazio(string s) => string.IsNullOrWhiteSpace(s);
        private static bool Antes(string data, string lim) => string.Compare(data, lim, StringComparison.Ordinal) < 0;
        private static bool Depois(string data, string lim) => string.Compare(data, lim, StringComparison.Ordinal) > 0;
        private static decimal Div(decimal num, decimal den, int casas) => den == 0 ? 0 : Math.Round(num / den, casas, MidpointRounding.AwayFromZero);
        private static string Trunc(string s, int n) => s.Length <= n ? s : s.Substring(0, n);
    }
}
