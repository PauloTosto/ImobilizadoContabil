using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Imobilizado.Dados;

namespace Imobilizado.App
{
    /// <summary>
    /// Teste headless do NOVO editor direto de composto (FrmGrupoComposto): edita o VALOR do
    /// principal de forma independente + o valor de um detalhe, salva por RECNO (UPDATE no
    /// lugar) e relê para conferir que cada valor foi gravado como digitado. Aponta para CÓPIA.
    /// </summary>
    internal static class TesteComposto
    {
        public static void Rodar(string pasta, decimal mestreMovId)
        {
            var caminho = System.IO.Path.Combine(pasta, "_teste_saida.txt");
            var log = new System.Text.StringBuilder();
            Action<string> P = s => { Console.WriteLine(s); log.AppendLine(s); System.IO.File.WriteAllText(caminho, log.ToString()); };
            try { RodarInterno(pasta, mestreMovId == 0 ? 74052m : mestreMovId, P); }
            catch (Exception ex) { P("EXCEÇÃO: " + ex); }
        }

        /// <summary>Valida a regra da ABSORÇÃO: p/ cada SIST_ABSOR existente, compara o valor com Val2−Val3 do GRUPO do crédito (mês vs ano-até), excluindo o próprio SIST_ABSOR.</summary>
        public static void TestaAbsorcao(string pasta, string anoMes)
        {
            try
            {
                int ano = int.Parse(anoMes.Substring(0, 4)), mes = int.Parse(anoMes.Substring(4, 2));
                string d1m = $"{ano:0000}{mes:00}01", d2 = $"{ano:0000}{mes:00}{DateTime.DaysInMonth(ano, mes):00}";
                string d1a = $"{ano:0000}0101";
                var ptpla = System.IO.Path.Combine(pasta, $"PTPLA{ano - 1}.DBF");
                var plano = Contabil.Core.PlanoContas.Carregar(System.IO.Path.Combine(pasta, "placon.DBF"), ptpla);
                var eng = new Contabil.Core.EngineSaldo(plano);
                var movfin = System.IO.Path.Combine(pasta, "MOVFIN.DBF");
                // exclui só o SIST_ABSOR do PRÓPRIO mês: as absorções de meses anteriores ficam
                // (creditam o grupo e abatem o que já foi absorvido — comportamento do Clipper,
                // onde o VAL2/VAL3 do PLACON vem do balancete que inclui tudo)
                Func<string, string, bool> exclAbsor = (doc, data) =>
                    doc == "SIST_ABSOR" && string.Compare(data, d1m, StringComparison.Ordinal) >= 0;
                var apMes = eng.ApurarPeriodoComRollup(movfin, d1m, d2, exclAbsor);
                var apAno = eng.ApurarPeriodoComRollup(movfin, d1a, d2, exclAbsor);

                var absor = new MovfinGravador(pasta).LerPeriodo(d1m, d2, null)
                    .Where(l => (l.Doc ?? "").Trim() == "SIST_ABSOR").ToList();
                Console.WriteLine($"=== {absor.Count} lançamentos SIST_ABSOR em {anoMes} ===");
                foreach (var l in absor.OrderBy(l => l.Credito))
                {
                    var grupo = (l.Credito ?? "").Trim();
                    grupo = grupo.Length == 8 ? grupo.Substring(0, 5) + "000" : grupo;
                    decimal vm = 0, va = 0;
                    if (apMes.TryGetValue(grupo, out var am)) vm = decimal.Round(am.Val2 - am.Val3, 2);
                    if (apAno.TryGetValue(grupo, out var aa)) va = decimal.Round(aa.Val2 - aa.Val3, 2);
                    string casa = l.Valor == vm ? "=MÊS" : l.Valor == va ? "=ANO" : "≠ambos";
                    Console.WriteLine($"  D={l.Debito} C={l.Credito} V={l.Valor,14:N2} | grupo {grupo}: mês={vm,14:N2} ano={va,14:N2}  {casa}");
                }
            }
            catch (Exception ex) { Console.WriteLine("ERRO: " + ex.Message); }
        }

        /// <summary>Valida o MotorAbsorcao reproduzindo a absorção real de um mês (RELAC sintetizado dos próprios lançamentos).</summary>
        public static void TestaMotorAbsorcao(string pasta, string anoMes)
        {
            try
            {
                int ano = int.Parse(anoMes.Substring(0, 4)), mes = int.Parse(anoMes.Substring(4, 2));
                string d2 = $"{ano:0000}{mes:00}{DateTime.DaysInMonth(ano, mes):00}";
                var plano = Contabil.Core.PlanoContas.Carregar(System.IO.Path.Combine(pasta, "placon.DBF"),
                    System.IO.Path.Combine(pasta, $"PTPLA{ano - 1}.DBF"));
                var eng = new Contabil.Core.EngineSaldo(plano);
                Func<string, string, bool> excl = (doc, data) => doc == "SIST_ABSOR" && data == d2;
                var ap = eng.ApurarPeriodoComRollup(System.IO.Path.Combine(pasta, "MOVFIN.DBF"), $"{ano:0000}0101", d2, excl);

                var reais = new MovfinGravador(pasta).LerPeriodo(d2, d2, null)
                    .Where(l => (l.Doc ?? "").Trim() == "SIST_ABSOR" && !(l.Debito ?? "").StartsWith("*")).ToList();
                // sintetiza o RELAC: % = valor/totalDoGrupo (linha única = cheio, QUANT1=0)
                var porGrupo = reais.GroupBy(l => l.Credito.Trim().Substring(0, 5)).ToDictionary(g => g.Key, g => g.Sum(x => x.Valor));
                var relac = reais.Select(l => new Contabil.Core.LinhaRelac
                {
                    Debito = l.Debito.Trim(),
                    Credito = l.Credito.Trim(),
                    Quant1 = porGrupo[l.Credito.Trim().Substring(0, 5)] == l.Valor ? 0m
                           : decimal.Round(l.Valor / porGrupo[l.Credito.Trim().Substring(0, 5)] * 100m, 0),
                }).ToList();

                var gerado = Contabil.Core.MotorAbsorcao.Gerar(relac,
                    g => ap.TryGetValue(g, out var a) ? a.Val2 - a.Val3 : 0m, "1", "3");

                int ok = 0, dif = 0;
                for (int i = 0; i < Math.Min(reais.Count, gerado.Count); i++)
                {
                    bool igual = gerado[i].Debito == reais[i].Debito.Trim() && gerado[i].Credito == reais[i].Credito.Trim()
                                 && gerado[i].Valor == reais[i].Valor;
                    if (igual) ok++;
                    else { dif++; Console.WriteLine($"  DIF: gerado D={gerado[i].Debito} C={gerado[i].Credito} V={gerado[i].Valor:N2} (q={gerado[i].Quant1}) | real V={reais[i].Valor:N2}"); }
                }
                Console.WriteLine($"Reais (ativos): {reais.Count} | gerados: {gerado.Count} | iguais: {ok} | diferentes: {dif}");
                Console.WriteLine(ok == reais.Count && gerado.Count == reais.Count ? "MOTOR ABSORCAO OK" : "DIVERGÊNCIA!");
            }
            catch (Exception ex) { Console.WriteLine("ERRO: " + ex); }
        }

        /// <summary>Testa o CRUD do CADCUSTO numa CÓPIA: lista, inclui dummy, altera, exclui, confere contagens.</summary>
        public static void TestaCadCusto(string pasta)
        {
            try
            {
                var cad = System.IO.Path.Combine(pasta, "CADCUSTO.DBF");
                int n0 = Contabil.Core.Apropriacao.ProdutoCusto.Carregar(cad).Count;
                Console.WriteLine($"CADCUSTO: {n0} produtos.");
                var g = new CadCustoGravador(pasta);
                var dummy = new CadCustoGravador.ItemCadCusto
                {
                    Cod = "Z999", Desc = "TESTE CRUD", Producao = "11125001", EmCurso = "11126001",
                    Receita = "31110001", CustoVenda = "31330001", Unid = "KILOS",
                    Estoque = 12.34m, Data = new DateTime(2026, 1, 31), Perc1 = 10m,
                };
                if (g.Existe(dummy.Cod)) { Console.WriteLine("Z999 já existe."); return; }
                g.Incluir(dummy);
                var aposInc = Contabil.Core.Apropriacao.ProdutoCusto.Carregar(cad);
                var z = aposInc.Find(i => i.Cod == "Z999");
                Console.WriteLine($"Incluiu: {aposInc.Count} produtos | Z999 desc='{(z?.Desc ?? "").Trim()}' prod={z?.Producao} est={z?.Estoque} data={z?.Data} p1={z?.Perc1}");
                dummy.Desc = "TESTE ALTERADO"; dummy.Perc1 = 25m;
                g.Alterar(dummy);
                z = Contabil.Core.Apropriacao.ProdutoCusto.Carregar(cad).Find(i => i.Cod == "Z999");
                Console.WriteLine($"Alterou: desc='{(z?.Desc ?? "").Trim()}' p1={z?.Perc1}");
                g.Excluir("Z999");
                int nf = Contabil.Core.Apropriacao.ProdutoCusto.Carregar(cad).Count;
                Console.WriteLine($"Excluiu: {nf} produtos.");
                Console.WriteLine(nf == n0 && z != null && z.Desc.Trim() == "TESTE ALTERADO" && z.Perc1 == 25m ? "CRUD OK" : "DIVERGÊNCIA!");
            }
            catch (Exception ex) { Console.WriteLine("ERRO: " + ex.Message); }
        }

        /// <summary>Testa CopiarPeriodo do MOVFIN: copia o período p/ um DBF novo e confere a contagem relendo.</summary>
        public static void TestaCopiaMovfin(string pasta, string d1, string d2, string destinoSemExt)
        {
            try
            {
                int n = new MovfinGravador(pasta).CopiarPeriodo(destinoSemExt, d1, d2);
                Console.WriteLine($"CopiarPeriodo reportou {n} registros.");
                int lidos = 0, foraPeriodo = 0;
                foreach (var r in new Imobilizado.Core.Dbf.DbfReader(destinoSemExt + ".DBF").Registros())
                {
                    lidos++;
                    var dt = r["DATA"].Trim();
                    if (string.Compare(dt, d1, StringComparison.Ordinal) < 0 || string.Compare(dt, d2, StringComparison.Ordinal) > 0) foraPeriodo++;
                }
                Console.WriteLine($"Relido do DBF copiado: {lidos} registros | fora do período: {foraPeriodo}");
                // valida também via VFPOLEDB (o consumidor real)
                var dir = System.IO.Path.GetDirectoryName(destinoSemExt);
                var nome = System.IO.Path.GetFileName(destinoSemExt);
                using (var con = new System.Data.OleDb.OleDbConnection($"Provider=VFPOLEDB.1;Data Source={dir}"))
                {
                    con.Open();
                    using (var cmd = new System.Data.OleDb.OleDbCommand($"SELECT COUNT(*) FROM {nome}", con))
                        Console.WriteLine($"VFPOLEDB lê a cópia: {cmd.ExecuteScalar()} registros");
                }
                Console.WriteLine(lidos == n && foraPeriodo == 0 ? "COPIA OK" : "DIVERGÊNCIA!");
            }
            catch (Exception ex) { Console.WriteLine("ERRO: " + ex.Message); }
        }

        /// <summary>Reproduz o Alterar de um bem (em CÓPIA): lê, troca RESULTADO se pedido, grava e relê.</summary>
        public static void TestaAlteraBem(string pasta, string cod, string novoResultado)
        {
            try
            {
                var imobil = System.IO.Path.Combine(pasta, "IMOBIL.DBF");
                var antes = Imobilizado.Core.Dbf.CadastroDbf.CarregarBensEdicao(imobil).Find(b => b.Codigo == cod);
                if (antes == null) { Console.WriteLine($"Bem {cod} não encontrado."); return; }
                Console.WriteLine($"ANTES: cod={antes.Codigo} RESULTADO={antes.ContaResultado} DESC='{(antes.Descricao ?? "").Trim()}'");
                if (!string.IsNullOrWhiteSpace(novoResultado)) antes.ContaResultado = novoResultado;
                new ImobilGravador(pasta).Alterar(antes);
                var depois = Imobilizado.Core.Dbf.CadastroDbf.CarregarBensEdicao(imobil).Find(b => b.Codigo == cod);
                Console.WriteLine($"DEPOIS: cod={depois.Codigo} RESULTADO={depois.ContaResultado} DESC='{(depois.Descricao ?? "").Trim()}'");
                Console.WriteLine("ALTERAR OK");
            }
            catch (Exception ex) { Console.WriteLine("ERRO: " + ex.Message); }
        }

        /// <summary>Testa o Incluir de um bem dummy (em CÓPIA) — flagra erro de sintaxe (DESC reservado) no INSERT.</summary>
        public static void TestaIncluiBem(string pasta)
        {
            try
            {
                var g = new ImobilGravador(pasta);
                var dummy = new Imobilizado.Core.Dominio.BemEdicao
                {
                    Codigo = "ZZ999", Descricao = "TESTE INCLUIR (apagar)", ContaImobilizado = "12269001",
                    ContaDepAcumulada = "12278009", ContaResultado = "32170015",
                    ValorAquisicao = 1.23m, BaseDepreciavel = 1.23m, DepreciacaoInicial = 0m, ValorBaixa = 0m,
                    DataAquisicao = new DateTime(2026, 1, 1),
                };
                if (g.Existe(dummy.Codigo)) { Console.WriteLine("ZZ999 já existe (rode noutra cópia)."); return; }
                g.Incluir(dummy);
                var lido = Imobilizado.Core.Dbf.CadastroDbf.CarregarBensEdicao(System.IO.Path.Combine(pasta, "IMOBIL.DBF"))
                    .Find(b => b.Codigo == "ZZ999");
                Console.WriteLine(lido != null
                    ? $"INCLUIR OK: cod={lido.Codigo} DESC='{(lido.Descricao ?? "").Trim()}' RESULTADO={lido.ContaResultado}"
                    : "INSERIU mas não achou ao reler?!");
            }
            catch (Exception ex) { Console.WriteLine("ERRO: " + ex.Message); }
        }

        /// <summary>Dump dos bens do IMOBIL cuja conta dep.acumulada começa com o prefixo + lançamentos que o motor gera p/ elas.</summary>
        public static void DumpImobil(string pasta, string prefixoDepAcum, string anoMes)
        {
            var caminho = System.IO.Path.Combine(pasta, "_dump_imobil.txt");
            var log = new System.Text.StringBuilder();
            Action<string> P = s => { Console.WriteLine(s); log.AppendLine(s); System.IO.File.WriteAllText(caminho, log.ToString()); };
            try
            {
                var bensEd = Imobilizado.Core.Dbf.CadastroDbf.CarregarBensEdicao(System.IO.Path.Combine(pasta, "IMOBIL.DBF"));
                P($"=== IMOBIL: bens com DEP_ACUM começando '{prefixoDepAcum}' ===");
                foreach (var b in bensEd.Where(b => (b.ContaDepAcumulada ?? "").StartsWith(prefixoDepAcum)))
                    P($"   cod={b.Codigo} desc='{(b.Descricao ?? "").Trim()}' IMOB={b.ContaImobilizado} RESULTADO={b.ContaResultado} DEP_ACUM={b.ContaDepAcumulada} base={b.BaseDepreciavel:N2} depIni={b.DepreciacaoInicial:N2} baixa={b.DataBaixa}");

                var bens = bensEd.ConvertAll(be => be.ParaBem());
                var taxas = Imobilizado.Core.Dbf.CadastroDbf.CarregarTaxas(System.IO.Path.Combine(pasta, "placon.DBF"));
                var motor = new Imobilizado.Core.MotorDepreciacao(g => taxas.TryGetValue(g, out var t) ? t : 0m);
                var comp = new Imobilizado.Core.Dominio.AnoMes(int.Parse(anoMes.Substring(0, 4)), int.Parse(anoMes.Substring(4, 2)));
                var lancs = motor.GerarLancamentos(bens, comp);
                P($"\n=== MOTOR {comp}: lançamentos com crédito começando '{prefixoDepAcum}' ===");
                foreach (var l in lancs.Where(l => (l.Credito ?? "").StartsWith(prefixoDepAcum)))
                    P($"   D={l.Debito} C={l.Credito} V={l.Valor:N2} H='{l.Historico}'");
            }
            catch (Exception ex) { P("EXCEÇÃO: " + ex); }
        }

        /// <summary>Lista as transferências bancárias (espelho: um lado cód-banco, outro lado DESC2) e agrupa por par de bancos.</summary>
        public static void TestaTransf(string pasta, string d1, string d2)
        {
            var caminho = System.IO.Path.Combine(pasta, "_teste_transf.txt");
            var log = new System.Text.StringBuilder();
            Action<string> P = s => { Console.WriteLine(s); log.AppendLine(s); System.IO.File.WriteAllText(caminho, log.ToString()); };
            try
            {
                var plano = Contabil.Core.PlanoContas.Carregar(System.IO.Path.Combine(pasta, "placon.DBF"));
                var lanc = new MovfinGravador(pasta).LerPeriodo(d1, d2, null);
                bool Len2(string s) => (s ?? "").Trim().Length == 2;
                bool EhD2(string s) => plano.EhBancoContabilDesc2((s ?? "").Trim());
                // candidato de transferência (espelho): um lado é cód de 2 díg, o outro é DESC2 de banco contábil
                bool EhTransf(LancamentoMovfin l) => (Len2(l.Debito) && EhD2(l.Credito)) || (Len2(l.Credito) && EhD2(l.Debito));
                string Norm(string s) => Len2(s) ? plano.NBancoDesc2((s ?? "").Trim()).Trim() : (s ?? "").Trim();
                var transf = lanc.Where(EhTransf).ToList();
                // par espelho perfeito = 2 registros com mesmo (bancos, valor, data), representações trocadas
                var porPar = transf.GroupBy(l => (Norm(l.Debito), Norm(l.Credito), decimal.Round(l.Valor, 2), l.Data)).ToList();
                int casados = porPar.Count(g => g.Count() == 2 && g.Any(x => Len2(x.Debito)) && g.Any(x => Len2(x.Credito)));
                var problemas = porPar.Where(g => !(g.Count() == 2 && g.Any(x => Len2(x.Debito)) && g.Any(x => Len2(x.Credito)))).ToList();
                P($"=== Transferências bancárias {d1}..{d2}: {transf.Count} registros | {casados} pares espelho OK | {problemas.Sum(g => g.Count())} registros com PROBLEMA ===");
                foreach (var g in problemas)
                {
                    foreach (var l in g.OrderBy(l => l.Recno))
                        P($"   ⚠ rec={l.Recno} {l.Data} D=[{l.Debito}] C=[{l.Credito}] V={l.Valor,12:N2} H='{(l.Historico ?? "").Trim()}'");
                }
            }
            catch (Exception ex) { P("EXCEÇÃO: " + ex.Message); }
        }

        /// <summary>Testa a importação PESQUISA → RELACIONA.DBF numa pasta de DESTINO (cópia) e compara com uma pasta de REFERÊNCIA.</summary>
        public static void TestaImporta(string pastaDestino, string xlsx, string pastaRef)
        {
            var caminho = System.IO.Path.Combine(pastaDestino, "_teste_importa.txt");
            var log = new System.Text.StringBuilder();
            Action<string> P = s => { Console.WriteLine(s); log.AppendLine(s); System.IO.File.WriteAllText(caminho, log.ToString()); };
            try
            {
                P($"Lendo PESQUISA de: {xlsx}");
                var itens = PesquisaReader.Ler(xlsx);
                P($"  {itens.Count} linhas lidas | com NOVOCOD: {itens.Count(i => !string.IsNullOrWhiteSpace(i.NovoCod))}");

                var grav = new RelacionaGravador(pastaDestino);
                var dup = grav.NumcontasDuplicados(itens);
                P($"  NUMCONTA duplicados: {dup.Count}" + (dup.Count > 0 ? " -> " + string.Join(",", dup.Take(8)) : ""));

                var res = grav.Recriar(itens);
                P($"GRAVADO em {pastaDestino}: {res.Gravados} registros (pulados sem NOVOCOD: {res.PuladosSemNovocod}) | backup: {res.CaminhoBackup ?? "(não havia)"}");

                var mapNovo = new RelacionaReader(pastaDestino).Carregar(out var dupN);
                P($"Lido de volta: {mapNovo.Count} NUMCONTA únicos (duplicatas no DBF: {dupN?.Count ?? 0})");

                if (!string.IsNullOrWhiteSpace(pastaRef) && System.IO.File.Exists(System.IO.Path.Combine(pastaRef, "RELACIONA.DBF")))
                {
                    var mapRef = new RelacionaReader(pastaRef).Carregar(out _);
                    int iguais = 0, difRed = 0, difCod = 0;
                    foreach (var kv in mapNovo)
                        if (mapRef.TryGetValue(kv.Key, out var rf))
                        {
                            bool okR = rf.Reduzido == kv.Value.Reduzido;
                            bool okC = (rf.NovoCod ?? "").Trim() == (kv.Value.NovoCod ?? "").Trim();
                            if (okR && okC) iguais++; else { if (!okR) difRed++; if (!okC) difCod++; }
                        }
                    P($"=== COMPARAÇÃO com {pastaRef} ===");
                    P($"  ref: {mapRef.Count} | meu: {mapNovo.Count} | só meu: {mapNovo.Keys.Except(mapRef.Keys).Count()} | só ref: {mapRef.Keys.Except(mapNovo.Keys).Count()}");
                    P($"  iguais (REDUZIDO+NOVOCOD): {iguais} | difere REDUZIDO: {difRed} | difere NOVOCOD: {difCod}");
                }
            }
            catch (Exception ex) { P("EXCEÇÃO: " + ex.Message); }
        }

        /// <summary>Dumpa os registros (MOVFIN pareado) cujo débito OU crédito RESOLVE para a conta dada — p/ comparar com o PTMOVFIN.</summary>
        public static void DumpConta(string pasta, string conta, string d1, string d2)
        {
            var caminho = System.IO.Path.Combine(pasta, "_dump_conta.txt");
            var log = new System.Text.StringBuilder();
            Action<string> P = s => { Console.WriteLine(s); log.AppendLine(s); System.IO.File.WriteAllText(caminho, log.ToString()); };
            try
            {
                var plano = Contabil.Core.PlanoContas.Carregar(System.IO.Path.Combine(pasta, "placon.DBF"));
                var relMap = new RelacionaReader(pasta).Carregar(out _);
                var lanc = new MovfinGravador(pasta).LerPeriodo(d1, d2, null);
                var exp = new ExportadorAlterData(plano, relMap);
                var pareado = exp.ParearFolha(exp.ParearTransferencias(exp.ParearCompostos(lanc)));
                var alvo = conta.Trim();
                decimal somaD = 0, somaC = 0; int n = 0;
                foreach (var l in pareado.OrderBy(x => x.Data).ThenBy(x => x.Recno))
                {
                    var cd = plano.Resolver(l.Debito); var cc = plano.Resolver(l.Credito);
                    if (cd != alvo && cc != alvo) continue;
                    n++;
                    if (cd == alvo) somaD += l.Valor;
                    if (cc == alvo) somaC += l.Valor;
                    if (n <= 40) P($"   {l.Data} rec={l.Recno} D=[{l.Debito}]→{cd} C=[{l.Credito}]→{cc} V={l.Valor,12:N2}  H='{(l.Historico ?? "").Trim()}' doc='{(l.Doc ?? "").Trim()}'");
                }
                P($"=== conta {alvo}: {n} registros | ΣdébitoNaConta={somaD:N2} ΣcréditoNaConta={somaC:N2} ===");
            }
            catch (Exception ex) { P("EXCEÇÃO: " + ex); }
        }

        /// <summary>Lista as linhas de folha (DOC=SIST_RURAL NW) de um período, p/ entender a estrutura antes de parear.</summary>
        public static void DumpFolha(string pasta, string d1, string d2, string doc = "SIST_RURAL NW")
        {
            var caminho = System.IO.Path.Combine(pasta, "_dump_folha.txt");
            var log = new System.Text.StringBuilder();
            Action<string> P = s => { Console.WriteLine(s); log.AppendLine(s); System.IO.File.WriteAllText(caminho, log.ToString()); };
            try
            {
                var lanc = new MovfinGravador(pasta).LerPeriodo(d1, d2, null)
                    .Where(l => (l.Doc ?? "").Trim() == doc.Trim()).ToList();
                P($"=== Folha SIST_RURAL NW {d1}..{d2}: {lanc.Count} linhas ===");
                foreach (var dia in lanc.Select(l => l.Data).Distinct().OrderBy(x => x))
                {
                    var doDia = lanc.Where(l => l.Data == dia).ToList();
                    P($"\n--- {dia} ({doDia.Count} linhas) ---");
                    foreach (var l in doDia.OrderBy(l => l.Recno))
                        P($"   rec={l.Recno} mov={l.MovId} out={l.OutroId} D=[{l.Debito}] C=[{l.Credito}] V={l.Valor,12:N2}  H='{(l.Historico ?? "").Trim()}'");
                }
            }
            catch (Exception ex) { P("EXCEÇÃO: " + ex); }
        }

        /// <summary>Investiga contas específicas: existência no placon, apelido, analítica, resolução e REDUZIDO no RELACIONA.</summary>
        public static void TestaConta(string pasta, string[] contas)
        {
            var caminho = System.IO.Path.Combine(pasta, "_teste_conta.txt");
            var log = new System.Text.StringBuilder();
            Action<string> P = s => { Console.WriteLine(s); log.AppendLine(s); System.IO.File.WriteAllText(caminho, log.ToString()); };
            try
            {
                var plano = Contabil.Core.PlanoContas.Carregar(System.IO.Path.Combine(pasta, "placon.DBF"));
                var reader = new RelacionaReader(pasta);
                P(reader.Diagnostico(contas[0]));
                var relMap = reader.Carregar(out _);
                P($"(RelacionaReader.Carregar trouxe {relMap.Count} NUMCONTA únicos)\n");
                foreach (var c in contas)
                {
                    var v = c.Trim();
                    if (v.StartsWith("red:"))
                    {
                        int alvo = int.Parse(v.Substring(4));
                        P($"Contas com REDUZIDO={alvo} no RELACIONA:");
                        foreach (var kv in relMap.Where(x => x.Value.Reduzido == alvo))
                        {
                            plano.Contas.TryGetValue(kv.Key, out var ct);
                            P($"   NUMCONTA={kv.Key}  DESC(rel)='{kv.Value.Descricao}'  placon='{ct?.Descricao}'");
                        }
                        P("");
                        continue;
                    }
                    bool noPlacon = plano.Contas.TryGetValue(v, out var conta);
                    bool analitica = Contabil.Core.PlanoContas.EhAnalitica(v);
                    string resolvido = plano.ResolverContabil(v) ?? "(null)";
                    bool noRelac = relMap.TryGetValue(v, out var ir);
                    P($"Conta [{v}]:");
                    P($"   no placon? {noPlacon}" + (noPlacon ? $"  desc='{conta.Descricao}' desc2='{conta.Desc2}' grau='{conta.Grau}'" : ""));
                    P($"   EhAnalitica(pos6-8≠000)? {analitica}");
                    P($"   ResolverContabil -> {resolvido}");
                    P($"   no RELACIONA? {noRelac}" + (noRelac ? $"  REDUZIDO={ir.Reduzido} NOVOCOD='{ir.NovoCod}'" : ""));
                    P("");
                }
            }
            catch (Exception ex) { P("EXCEÇÃO: " + ex); }
        }

        /// <summary>
        /// Valida o exportador AlterData contra os dados reais: lê MOVFIN do período, traduz para
        /// REDUZIDO via RELACIONA, gera o .xlsx no layout AlterData e imprime estatísticas
        /// (linhas, sem-mapeamento "-1", meias-entradas). Aponta para a CÓPIA.
        /// </summary>
        public static void TestaAlterData(string pasta, string d1, string d2)
        {
            var caminho = System.IO.Path.Combine(pasta, "_teste_alterdata.txt");
            var log = new System.Text.StringBuilder();
            Action<string> P = s => { Console.WriteLine(s); log.AppendLine(s); System.IO.File.WriteAllText(caminho, log.ToString()); };
            try
            {
                var plano = Contabil.Core.PlanoContas.Carregar(System.IO.Path.Combine(pasta, "placon.DBF"));
                var relMap = new RelacionaReader(pasta).Carregar(out var dups);
                P($"=== Export AlterData {d1}..{d2} ===");
                string dupTxt = dups.Count > 0 ? " -> " + string.Join(",", dups.Take(8)) : "";
                P($"RELACIONA: {relMap.Count} contas | duplicadas: {dups.Count}{dupTxt}");

                var lanc = new MovfinGravador(pasta).LerPeriodo(d1, d2, null);
                P($"MOVFIN no período: {lanc.Count} lançamentos");

                var exp = new ExportadorAlterData(plano, relMap);

                var preparados = exp.FiltrarPreparados(lanc, out var excluidos);
                P($"Filtro 'preparado p/ contabilidade' (FLT_1): {lanc.Count} -> {preparados.Count} " +
                  $"(excluídos {excluidos.Count}: não-preparados / '*' / banco sem CONTAB)");
                foreach (var l in excluidos.Take(8))
                    P($"     EXCLUÍDO rec={l.Recno} {l.Data} D=[{l.Debito}] C=[{l.Credito}] V={l.Valor:N2}");

                int meiaAntes = preparados.Count(l =>
                    (string.IsNullOrWhiteSpace(l.Debito) ^ string.IsNullOrWhiteSpace(l.Credito)));
                var pareado = exp.ParearCompostos(preparados);
                int meiaDepois = pareado.Count(l =>
                    (string.IsNullOrWhiteSpace(l.Debito) ^ string.IsNullOrWhiteSpace(l.Credito)));
                P($"Pareamento de compostos: {preparados.Count} -> {pareado.Count} linhas " +
                  $"(meias-entradas {meiaAntes} -> {meiaDepois})");

                var pareado2 = exp.ParearTransferencias(pareado);
                P($"Pareamento de transferências: {pareado.Count} -> {pareado2.Count} linhas");

                var pareado3 = exp.ParearFolha(pareado2);
                int meiaFolha = pareado3.Count(l => (string.IsNullOrWhiteSpace(l.Debito) ^ string.IsNullOrWhiteSpace(l.Credito)));
                P($"Pareamento de folha (SIST_RURAL): {pareado2.Count} -> {pareado3.Count} linhas (meias-entradas agora {meiaFolha})");

                var pareado4 = exp.ParearNotasFiscais(pareado3);
                int meiaNf = pareado4.Count(l => (string.IsNullOrWhiteSpace(l.Debito) ^ string.IsNullOrWhiteSpace(l.Credito)));
                P($"Pareamento de nota fiscal (DOC_FISC): {pareado3.Count} -> {pareado4.Count} linhas (meias-entradas agora {meiaNf})");

                var linhas = exp.MontarLinhas(pareado4, ModoExportAlterData.Reduzido);

                int semMap = linhas.Count(l => l.SemMapeamento);
                int meia = linhas.Count(l => l.MeiaEntrada);
                int debMenos = linhas.Count(l => l.Debito == "-1");
                int credMenos = linhas.Count(l => l.Credito == "-1");
                decimal soma = linhas.Sum(l => l.Valor);
                P($"Linhas no lote: {linhas.Count}");
                P($"  sem mapeamento (-1): {semMap}  (déb={debMenos}, créd={credMenos})");
                P($"  meias-entradas (um lado vazio): {meia}");
                P($"  soma dos valores: {soma:N2}");

                if (semMap > 0)
                {
                    P("\n  Exemplos SEM MAPEAMENTO (conta no MOVFIN sem REDUZIDO em RELACIONA):");
                    foreach (var l in linhas.Where(x => x.SemMapeamento).Take(12))
                        P($"     rec={l.Recno} {l.Data:dd/MM} D=[{l.Debito}] C=[{l.Credito}] V={l.Valor:N2} doc={l.NrDocumento}");
                }
                if (meia > 0)
                {
                    P("\n  Exemplos MEIA-ENTRADA (precisariam de pareamento se ainda existirem pós-corte):");
                    foreach (var l in linhas.Where(x => x.MeiaEntrada).Take(12))
                        P($"     rec={l.Recno} {l.Data:dd/MM} D=[{l.Debito}] C=[{l.Credito}] V={l.Valor:N2}");
                }

                var xlsx = System.IO.Path.Combine(pasta, "lote_teste_imobilizado.xlsx");
                ExportadorAlterData.GravarXlsx(linhas, xlsx);
                var fi = new System.IO.FileInfo(xlsx);
                P($"\n.xlsx gerado: {xlsx} (existe={fi.Exists}, {fi.Length} bytes)");
            }
            catch (Exception ex) { P("EXCEÇÃO: " + ex); }
        }

        /// <summary>Valida export Excel do balancete + detecção/inserção de contas novas no PTPLA (CÓPIA).</summary>
        public static void TestaExcelNovas(string pasta, string d1, string d2)
        {
            var caminho = System.IO.Path.Combine(pasta, "_teste_excel.txt");
            var log = new System.Text.StringBuilder();
            Action<string> P = s => { Console.WriteLine(s); log.AppendLine(s); System.IO.File.WriteAllText(caminho, log.ToString()); };
            try
            {
                var f = new FrmBalancete();
                var _h = f.Handle;
                var tp = typeof(FrmBalancete);
                var bf = BindingFlags.NonPublic | BindingFlags.Instance;
                T Campo<T>(string n) => (T)tp.GetField(n, bf).GetValue(f);
                Campo<TextBox>("txtPasta").Text = pasta;
                Campo<DateTimePicker>("dtDe").Value = new DateTime(int.Parse(d1.Substring(0, 4)), int.Parse(d1.Substring(4, 2)), int.Parse(d1.Substring(6, 2)));
                Campo<DateTimePicker>("dtAte").Value = new DateTime(int.Parse(d2.Substring(0, 4)), int.Parse(d2.Substring(4, 2)), int.Parse(d2.Substring(6, 2)));
                tp.GetMethod("Calcular", bf).Invoke(f, null);

                var novas = (System.Collections.Generic.List<string>)tp.GetMethod("NovasComMovimento", bf).Invoke(f, null);
                P($"=== Contas novas (placon, fora do PTPLA{int.Parse(d2.Substring(0, 4)) - 1}) COM movimento: {novas.Count} ===");
                foreach (var nc in novas.Take(10)) P("   " + nc);

                var linhas = tp.GetMethod("MontarLinhas", bf).Invoke(f, null);
                var xlsx = System.IO.Path.Combine(pasta, "balancete_teste.xlsx");
                tp.GetMethod("GravarExcel", bf).Invoke(f, new object[] { xlsx, linhas });
                var fi = new System.IO.FileInfo(xlsx);
                P($"\n.xlsx gerado: existe={fi.Exists}, {fi.Length} bytes");
                using (var pkg = new OfficeOpenXml.ExcelPackage(fi))
                {
                    var ws = pkg.Workbook.Worksheets["Balancete"];
                    P($"   Dimensão: {ws.Dimension?.Address}");
                    P($"   A1: {ws.Cells[1, 1].Value}");
                    P($"   Cabeçalhos: {string.Join(" | ", System.Linq.Enumerable.Range(1, 6).Select(c => ws.Cells[3, c].Value))}");
                    P($"   1ª linha: {string.Join(" | ", System.Linq.Enumerable.Range(1, 6).Select(c => ws.Cells[4, c].Value))}");
                }

                // testa o INSERT no PTPLA via VFPOLEDB (na CÓPIA) com uma conta fictícia que certamente não existe
                var pg = new PtplaGravador(pasta, int.Parse(d2.Substring(0, 4)) - 1);
                var alvo = novas.Count > 0 ? novas[0] : "19999991";
                bool antes = pg.Existe(alvo);
                bool ins = pg.InserirConta(alvo, "5", "TESTE CONTA NOVA", "TST # NOVA", d1);
                bool depois = pg.Existe(alvo);
                bool ins2 = pg.InserirConta(alvo, "5", "TESTE", "TST # NOVA", d1);   // 2ª vez deve recusar (já existe)
                P($"\nPtplaGravador.InserirConta({alvo}): existia_antes={antes}, inseriu={ins}, existe_agora={depois}, reinserir={ins2} (esperado false)");
                f.Dispose();
            }
            catch (Exception ex) { P("EXCEÇÃO: " + ex); }
        }

        /// <summary>Valida o balancete: confere débito=crédito e mostra os grupos de 1º nível.</summary>
        public static void TestaBalancete(string pasta, string d1, string d2)
        {
            var caminho = System.IO.Path.Combine(pasta, "_teste_balancete.txt");
            var log = new System.Text.StringBuilder();
            Action<string> P = s => { Console.WriteLine(s); log.AppendLine(s); System.IO.File.WriteAllText(caminho, log.ToString()); };
            try
            {
                int ano = int.Parse(d2.Substring(0, 4));
                var ptpla = System.IO.Path.Combine(pasta, $"PTPLA{ano - 1}.DBF");
                var plano = Contabil.Core.PlanoContas.Carregar(System.IO.Path.Combine(pasta, "placon.DBF"), ptpla);
                var eng = new Contabil.Core.EngineSaldo(plano);
                var movfin = System.IO.Path.Combine(pasta, "MOVFIN.DBF");

                // mesma lógica do FrmBalancete: pareia a folha SIST_RURAL pré-corte e substitui os soltos
                const string corte = "20260501";
                var corteLeitura = string.Compare(d2, "20260430", StringComparison.Ordinal) <= 0 ? d2 : "20260430";
                var folhaRaw = new MovfinGravador(pasta).LerPeriodo("19000101", corteLeitura, null)
                    .Where(l => (l.Doc ?? "").Trim() == "SIST_RURAL NW").ToList();
                var folhaPareada = new PareadorFolha(plano).Parear(folhaRaw, corte);
                var extra = folhaPareada.Select(l => new Contabil.Core.EngineSaldo.Mov
                { Data = l.Data, Debito = l.Debito, Credito = l.Credito, Valor = l.Valor, Doc = l.Doc }).ToList();
                Func<string, string, bool> excluir = (doc, data) =>
                    doc == "SIST_RURAL NW" && string.Compare(data, corte, StringComparison.Ordinal) < 0;

                var apSem = eng.ApurarPeriodoComRollup(movfin, d1, d2);
                var ap = eng.ApurarPeriodoComRollup(movfin, d1, d2, excluir, extra);

                decimal td = 0, tc = 0;
                foreach (var kv in ap) if (Contabil.Core.HierarquiaContas.EhAnalitica(kv.Key)) { td += kv.Value.Val2; tc += kv.Value.Val3; }
                P($"=== Balancete {d1}..{d2} (âncora PTPLA{ano - 1}) — folha pareada pré-{corte} ===");
                P($"Folha SIST_RURAL pré-corte: {folhaRaw.Count} registros soltos -> {extra.Count} pareados");
                P($"Total Débitos:  {td:N2}");
                P($"Total Créditos: {tc:N2}");
                P($"Diferença: {td - tc:N2}  -> {(decimal.Round(td - tc, 2) == 0 ? "CONFERE" : "NÃO CONFERE")}");
                P("");

                // CSV de todas as contas com movimento p/ diff contra o Contabil2020
                var csv = new System.Text.StringBuilder("conta;descricao;anterior;debito;credito;atual\n");
                foreach (var nc in ap.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    var a = ap[nc];
                    if (a.Val1 == 0 && a.Val2 == 0 && a.Val3 == 0 && a.SaldoFinal == 0) continue;
                    plano.Contas.TryGetValue(nc, out var cc);
                    csv.Append($"{nc};{(cc?.Descricao ?? "").Trim()};{a.Val1:F2};{a.Val2:F2};{a.Val3:F2};{a.SaldoFinal:F2}\n");
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(pasta, "_balancete_dump.csv"), csv.ToString());

                var csvSem = new System.Text.StringBuilder("conta;descricao;anterior;debito;credito;atual\n");
                foreach (var nc in apSem.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    var a = apSem[nc];
                    if (a.Val1 == 0 && a.Val2 == 0 && a.Val3 == 0 && a.SaldoFinal == 0) continue;
                    plano.Contas.TryGetValue(nc, out var cc);
                    csvSem.Append($"{nc};{(cc?.Descricao ?? "").Trim()};{a.Val1:F2};{a.Val2:F2};{a.Val3:F2};{a.SaldoFinal:F2}\n");
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(pasta, "_balancete_dump_sem.csv"), csvSem.ToString());

                // contas ANALÍTICAS cujo saldo/débito/crédito MUDOU com o pareamento da folha
                P("Contas analíticas afetadas pelo pareamento da folha (SEM -> COM):");
                int nMud = 0;
                foreach (var nc in ap.Keys.Where(k => Contabil.Core.HierarquiaContas.EhAnalitica(k)).OrderBy(k => k, StringComparer.Ordinal))
                {
                    apSem.TryGetValue(nc, out var s0); ap.TryGetValue(nc, out var s1);
                    if (decimal.Round(s0.Val2, 2) == decimal.Round(s1.Val2, 2)
                        && decimal.Round(s0.Val3, 2) == decimal.Round(s1.Val3, 2)) continue;
                    nMud++;
                    if (nMud <= 25)
                    {
                        plano.Contas.TryGetValue(nc, out var cc);
                        P($"   {nc} {(cc?.Descricao ?? "").Trim(),-26} déb {s0.Val2,13:N2}->{s1.Val2,13:N2}  cré {s0.Val3,13:N2}->{s1.Val3,13:N2}");
                    }
                }
                P($"   ... total {nMud} contas analíticas afetadas.");
                P("");
                P("Grupos de 1º nível (sintéticos) com saldo:");
                foreach (var nc in ap.Keys.Where(k => Contabil.Core.HierarquiaContas.Nivel(k) == 1).OrderBy(k => k))
                {
                    var a = ap[nc];
                    if (a.Val1 == 0 && a.Val2 == 0 && a.Val3 == 0 && a.SaldoFinal == 0) continue;
                    plano.Contas.TryGetValue(nc, out var c);
                    P($"   {nc} {c?.Descricao,-28} ant={a.Val1,16:N2} déb={a.Val2,14:N2} cré={a.Val3,14:N2} atual={a.SaldoFinal,16:N2}");
                }
            }
            catch (Exception ex) { P("EXCEÇÃO: " + ex); }
        }

        /// <summary>Valida a regra "válido para a contabilidade" contra os dados reais de um período.</summary>
        public static void TestaContab(string pasta, string d1, string d2)
        {
            var caminho = System.IO.Path.Combine(pasta, "_teste_contab.txt");
            var log = new System.Text.StringBuilder();
            Action<string> P = s => { Console.WriteLine(s); log.AppendLine(s); System.IO.File.WriteAllText(caminho, log.ToString()); };
            try
            {
                var plano = Contabil.Core.PlanoContas.Carregar(System.IO.Path.Combine(pasta, "placon.DBF"));
                var lanc = new MovfinGravador(pasta).LerPeriodo(d1, d2, null);
                int validos = lanc.Count(l => plano.ValidoParaContabilidade(l.Debito, l.Credito));
                P($"=== Validação contábil {d1}..{d2} ===");
                P($"Total: {lanc.Count} | Válidos: {validos} | Pendentes: {lanc.Count - validos}");
                P("");
                P("Exemplos de PENDENTES (lado que não resolve):");
                foreach (var l in lanc.Where(l => !plano.ValidoParaContabilidade(l.Debito, l.Credito)).Take(12))
                {
                    string motivo = "";
                    if (!string.IsNullOrWhiteSpace(l.Debito) && plano.ResolverContabil(l.Debito) == null) motivo += $"DÉB [{l.Debito}] não resolve; ";
                    if (!string.IsNullOrWhiteSpace(l.Credito) && plano.ResolverContabil(l.Credito) == null) motivo += $"CRÉD [{l.Credito}] não resolve; ";
                    if (string.IsNullOrWhiteSpace(l.Debito) && string.IsNullOrWhiteSpace(l.Credito)) motivo = "sem débito nem crédito";
                    P($"   rec={l.Recno} {l.Data} D=[{l.Debito}] C=[{l.Credito}] V={l.Valor:N2} -> {motivo}");
                }
                P("");
                P("Exemplos de VÁLIDOS:");
                foreach (var l in lanc.Where(l => plano.ValidoParaContabilidade(l.Debito, l.Credito)).Take(6))
                    P($"   rec={l.Recno} D=[{l.Debito}]->{plano.ResolverContabil(l.Debito)} C=[{l.Credito}]->{plano.ResolverContabil(l.Credito)}");
            }
            catch (Exception ex) { P("EXCEÇÃO: " + ex); }
        }

        /// <summary>Simula a digitação na máscara de centavos do FrmGrupoComposto e imprime o resultado.</summary>
        public static void TestaMascara()
        {
            var f = new FrmGrupoComposto(new System.Collections.Generic.List<LancamentoMovfin>(), null);
            var _ = f.Handle;
            var tb = new TextBox();
            var mKey = typeof(FrmGrupoComposto).GetMethod("ValorKeyPress", BindingFlags.NonPublic | BindingFlags.Instance);
            var fReset = typeof(FrmGrupoComposto).GetField("_valorReset", BindingFlags.NonPublic | BindingFlags.Instance);

            void Press(char c) => mKey.Invoke(f, new object[] { tb, new KeyPressEventArgs(c) });
            void Seq(string label, string teclas)
            {
                tb.Text = ""; fReset.SetValue(f, true);
                foreach (var c in teclas) Press(c);
                Console.WriteLine($"   digitar \"{teclas}\"  ->  [{tb.Text}]   ({label})");
            }
            Console.WriteLine("=== Máscara de centavos (estilo calculadora) ===");
            Seq("um dígito", "1");
            Seq("dois", "12");
            Seq("três", "123");
            Seq("quatro", "1234");
            Seq("seis", "123456");
            // backspace (\b) depois de 1234
            tb.Text = ""; fReset.SetValue(f, true);
            foreach (var c in "1234") Press(c);
            Press('\b');
            Console.WriteLine($"   \"1234\" + backspace  ->  [{tb.Text}]   (esperado 1,23)");
            // ignora letra e vírgula
            tb.Text = ""; fReset.SetValue(f, true);
            foreach (var c in "9a,9") Press(c);
            Console.WriteLine($"   \"9a,9\" (letra/vírgula ignoradas)  ->  [{tb.Text}]   (esperado 0,99)");
            f.Dispose();
        }

        private static void RodarInterno(string pasta, decimal masterId, Action<string> P)
        {
            var g = new MovfinGravador(pasta);
            var plano = Contabil.Core.PlanoContas.Carregar(System.IO.Path.Combine(pasta, "placon.DBF"));

            // acha a data-âncora pelo mestre
            var amostra = g.LerPeriodo("20200101", "20301231", null).FirstOrDefault(l => l.MovId == masterId && l.OutroId == 0);
            if (amostra == null) { P($"Mestre {masterId} não achado."); return; }
            var data = amostra.Data;
            var grupo = g.LerGrupo(masterId, data);
            var mestreRec = grupo.First(l => l.OutroId == 0).Recno;
            var primDetRec = grupo.Where(l => l.OutroId == masterId).OrderBy(l => l.Recno).First().Recno;
            P($"=== Grupo {masterId} ({data}): {grupo.Count} linhas ===");
            foreach (var l in grupo.OrderBy(l => l.OutroId == 0 ? 0 : 1).ThenBy(l => l.Recno))
                P($"   rec={l.Recno} {(l.OutroId == 0 ? "MESTRE" : "det   ")} D=[{l.Debito}] C=[{l.Credito}] V={l.Valor}");

            // abre o NOVO editor
            var dlg = new FrmGrupoComposto(grupo, plano);
            var _h = dlg.Handle;
            Application.DoEvents();
            var dgv = (DataGridView)typeof(FrmGrupoComposto).GetField("dgv", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dlg);

            // edita DIRETO: valor do MESTRE -> 7777,77 (independente!) e valor do 1º detalhe -> 111,11
            foreach (DataGridViewRow r in dgv.Rows)
            {
                if (r.IsNewRow) continue;
                var o = r.Tag as LancamentoMovfin;
                if (o != null && o.Recno == mestreRec) r.Cells["Valor"].Value = "7777,77";
                if (o != null && o.Recno == primDetRec) r.Cells["Valor"].Value = "111,11";
            }
            P("\n   Editei DIRETO: VALOR do mestre -> 7777,77 ; VALOR do 1º detalhe -> 111,11");

            typeof(FrmGrupoComposto).GetMethod("Confirmar", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dlg, null);
            P($"   DialogResult = {dlg.DialogResult}");
            if (dlg.DialogResult != DialogResult.OK) { P("   >>> Confirmar bloqueou — ver Aviso <<<"); dlg.Dispose(); return; }

            // aplica como o FrmLancamentos faz: UPDATE por RECNO
            foreach (var rec in dlg.Excluidos) g.ExcluirLancamento(rec);
            foreach (var l in dlg.Linhas) { if (l.Recno != 0) g.AlterarLancamento(l); else g.InserirLancamento(l); }
            dlg.Dispose();
            P($"   Aplicado: {dlg.Linhas.Count(l => l.Recno != 0)} UPDATE por RECNO, {dlg.Linhas.Count(l => l.Recno == 0)} insert, {dlg.Excluidos.Count} delete.");

            // relê e confere
            var grupo2 = g.LerGrupo(masterId, data);
            var mestre2 = grupo2.First(l => l.Recno == mestreRec);
            var det2 = grupo2.First(l => l.Recno == primDetRec);
            P($"\n=== Releitura ===");
            P($"   Mestre rec={mestre2.Recno} VALOR={mestre2.Valor} (esperado 7777,77)");
            P($"   1º detalhe rec={det2.Recno} VALOR={det2.Valor} (esperado 111,11)");
            bool ok = mestre2.Valor == 7777.77m && det2.Valor == 111.11m;
            P($"   >>> {(ok ? "EDIÇÃO DIRETA OK — valores gravados como digitados, RECNOs preservados" : "FALHOU")} <<<");
        }
    }
}
