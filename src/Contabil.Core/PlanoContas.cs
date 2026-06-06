using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Imobilizado.Core.Dbf;

namespace Contabil.Core
{
    /// <summary>
    /// Plano de contas (PLACON) com o que o cálculo de saldo precisa: por conta, o
    /// último saldo apurado (SDO) e a data dessa apuração (DATA, a âncora), além do
    /// mapa de apelidos DESC2 → NUMCONTA usado para resolver os lançamentos do MOVFIN.
    /// </summary>
    public sealed class PlanoContas
    {
        public sealed class Conta
        {
            public string NumConta;
            public string Desc2;       // apelido (ou o próprio número)
            public string Descricao;
            public string Grau;        // nível hierárquico (p/ rollup das sintéticas)
            public decimal Sdo;        // último saldo apurado
            public string DataAncora;  // "YYYYMMDD" — data do último saldo (movimentos > esta entram no delta)
        }

        private readonly Dictionary<string, Conta> _porNumConta = new Dictionary<string, Conta>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _apelidoParaConta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, Conta> Contas => _porNumConta;

        /// <summary>Carrega de um único arquivo (estrutura e âncora vêm do mesmo placon).</summary>
        public static PlanoContas Carregar(string caminhoPlacon) => Carregar(caminhoPlacon, caminhoPlacon);

        /// <summary>
        /// Procedimento oficial: ESTRUTURA + APELIDOS vêm do PLACON master (sempre atual,
        /// com as contas novas); ÂNCORA de saldo (SDO/DATA) vem do PTPLA&lt;ano-1&gt;, casada
        /// por NUMCONTA. Conta que existe no master mas não no PTPLA (criada no ano) entra
        /// com SDO=0 e data-âncora = a data do snapshot (fim do ano anterior).
        /// </summary>
        public static PlanoContas Carregar(string caminhoMaster, string caminhoAncora)
        {
            // 1) âncora: NUMCONTA -> (SDO, DATA)
            var ancora = new Dictionary<string, (decimal sdo, string data)>(StringComparer.OrdinalIgnoreCase);
            var contagemData = new Dictionary<string, int>();
            foreach (var r in new DbfReader(caminhoAncora).Registros())
            {
                var nc = r["NUMCONTA"];
                if (string.IsNullOrEmpty(nc)) continue;
                var data = r["DATA"].Trim();
                ancora[nc] = (Num(r["SDO"]), data);
                if (data.Length == 8) contagemData[data] = contagemData.TryGetValue(data, out var k) ? k + 1 : 1;
            }
            // data dominante do snapshot, usada como âncora das contas novas (SDO=0)
            string dataAncoraPadrao = "";
            int max = -1;
            foreach (var kv in contagemData) if (kv.Value > max) { max = kv.Value; dataAncoraPadrao = kv.Key; }

            // 2) estrutura + apelidos do master; saldo da âncora (ou 0 p/ contas novas)
            var pc = new PlanoContas();
            foreach (var r in new DbfReader(caminhoMaster).Registros())
            {
                var nc = r["NUMCONTA"];
                if (string.IsNullOrEmpty(nc)) continue;
                var temAncora = ancora.TryGetValue(nc, out var a);
                var c = new Conta
                {
                    NumConta = nc,
                    Desc2 = r["DESC2"],
                    Descricao = r["DESCRICAO"],
                    Grau = r["GRAU"],
                    Sdo = temAncora ? a.sdo : 0m,
                    DataAncora = temAncora ? a.data : dataAncoraPadrao,
                };
                pc._porNumConta[nc] = c;
                if (!string.IsNullOrEmpty(c.Desc2) && !pc._apelidoParaConta.ContainsKey(c.Desc2))
                    pc._apelidoParaConta[c.Desc2] = nc;
            }

            // 3) contas financeiras: o MOVFIN referencia banco pelo código NBANCO de 2 dígitos
            //    (ex.: "04"). Mapeia esse código → CONTAB do banco. Só bancos COM CONTAB
            //    (financeiras oficiais); os demais ficam sem resolver e o check_conta os ignora,
            //    exatamente como o Fin_Oficial/Contas_Fin do Clipper.
            var bancos = Path.Combine(Path.GetDirectoryName(caminhoMaster) ?? "", "BANCOS.DBF");
            if (File.Exists(bancos))
            {
                foreach (var r in new DbfReader(bancos).Registros())
                {
                    var contab = r["CONTAB"].Trim();
                    if (contab.Length == 0) continue;
                    var nb = r["NBANCO"].Trim();
                    if (nb.Length == 1) nb = "0" + nb;
                    if (nb.Length > 0 && !pc._apelidoParaConta.ContainsKey(nb))
                        pc._apelidoParaConta[nb] = contab;
                }
            }
            return pc;
        }

        /// <summary>Resolve um valor de DEBITO/CREDITO do MOVFIN para o número de conta (8 díg). Null se não resolver.</summary>
        public string Resolver(string debitoOuCredito)
        {
            var v = (debitoOuCredito ?? "").Trim();
            if (v.Length == 0) return null;
            if (_apelidoParaConta.TryGetValue(v, out var nc)) return nc;
            if (v.Length == 8 && EhDigitos(v)) return v;
            return null;
        }

        /// <summary>
        /// Resolve um lado (DEBITO/CREDITO) do MOVFIN para a conta **analítica** do PLACON, com a
        /// regra de "válido para a contabilidade" (espelha o Fin_Oficial/Contas_Fin do Clipper):
        ///  - apelido **DESC2 com correspondência PERFEITA** no PLACON, sendo conta **analítica**; ou
        ///  - **código de banco (2 dígitos)** que casa em BANCOS e cujo CONTAB é uma conta
        ///    **analítica existente** no PLACON.
        /// Retorna o NUMCONTA analítico, ou null se o lado não for válido para a contabilidade.
        /// (O mapa de apelidos já inclui banco-2-díg → CONTAB; aqui só exige que exista no PLACON
        /// e seja analítica — o que descarta bancos sem CONTAB no plano e contas sintéticas.)
        /// </summary>
        public string ResolverContabil(string debitoOuCredito)
        {
            var v = (debitoOuCredito ?? "").Trim();
            if (v.Length == 0) return null;
            string nc = null;
            if (_apelidoParaConta.TryGetValue(v, out var alias)) nc = alias;   // DESC2 exato OU banco-2-díg → CONTAB
            else if (v.Length == 8 && EhDigitos(v)) nc = v;                     // número de conta direto
            if (nc == null || !_porNumConta.ContainsKey(nc)) return null;      // tem que existir no PLACON
            return EhAnalitica(nc) ? nc : null;                                // e ser analítica
        }

        /// <summary>
        /// True se o lançamento (par débito/crédito) é válido para a contabilidade: cada lado
        /// PREENCHIDO resolve para uma conta analítica do PLACON (ver <see cref="ResolverContabil"/>).
        /// Meia-entrada (um lado só, típica do composto) é válida se o lado preenchido resolver.
        /// Linha sem débito nem crédito não é válida.
        /// </summary>
        public bool ValidoParaContabilidade(string debito, string credito)
        {
            bool temDeb = !string.IsNullOrWhiteSpace(debito);
            bool temCred = !string.IsNullOrWhiteSpace(credito);
            if (!temDeb && !temCred) return false;
            if (temDeb && ResolverContabil(debito) == null) return false;
            if (temCred && ResolverContabil(credito) == null) return false;
            return true;
        }

        /// <summary>Conta analítica = posições 6-8 do número diferentes de "000" (não é sintética/grupo).</summary>
        public static bool EhAnalitica(string numConta)
            => !string.IsNullOrEmpty(numConta) && numConta.Length >= 8 && numConta.Substring(5, 3) != "000";

        private static bool EhDigitos(string s)
        {
            foreach (var c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        private static decimal Num(string s)
            => decimal.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }
}
