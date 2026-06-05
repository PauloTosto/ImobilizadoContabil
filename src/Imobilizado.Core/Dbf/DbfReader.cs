using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Imobilizado.Core.Dbf
{
    /// <summary>
    /// Leitor mínimo de DBF (dBASE III/Clipper), só-leitura, sem dependências.
    /// Usado pelo reconciliador para ler IMOBIL/PLACON/MOVFIN diretamente do disco,
    /// sem precisar do driver VFPOLEDB. Faz streaming dos registros (MOVFIN tem 112MB).
    ///
    /// Observação: ignora o .CDX (índices). Lê campos C/N/D/L como string crua trimada;
    /// a conversão numérica/data fica a cargo de quem chama (cultura invariante).
    /// </summary>
    public sealed class DbfReader
    {
        public sealed class Campo
        {
            public string Nome;
            public char Tipo;
            public int Tamanho;
            public int Decimais;
            internal int Offset;
        }

        public IReadOnlyList<Campo> Campos { get; }
        public int TotalRegistros { get; }
        private readonly string _caminho;
        private readonly int _headerLen;
        private readonly int _recLen;
        private static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");

        public DbfReader(string caminho)
        {
            _caminho = caminho;
            using (var f = File.OpenRead(caminho))
            {
                var hdr = new byte[32];
                f.Read(hdr, 0, 32);
                TotalRegistros = BitConverter.ToInt32(hdr, 4);
                _headerLen = BitConverter.ToUInt16(hdr, 8);
                _recLen = BitConverter.ToUInt16(hdr, 10);

                var campos = new List<Campo>();
                int pos = 32, offset = 1; // offset 0 = flag de deleção
                var fd = new byte[32];
                while (true)
                {
                    f.Seek(pos, SeekOrigin.Begin);
                    f.Read(fd, 0, 32);
                    if (fd[0] == 0x0D || fd[0] == 0x00) break;
                    int z = Array.IndexOf(fd, (byte)0, 0, 11);
                    if (z < 0) z = 11;
                    var nome = Latin1.GetString(fd, 0, z);
                    var campo = new Campo
                    {
                        Nome = nome,
                        Tipo = (char)fd[11],
                        Tamanho = fd[16],
                        Decimais = fd[17],
                        Offset = offset,
                    };
                    campos.Add(campo);
                    offset += campo.Tamanho;
                    pos += 32;
                }
                Campos = campos;
            }
        }

        /// <summary>Itera os registros não-deletados como dicionário Nome→valor (string trimada).</summary>
        public IEnumerable<IReadOnlyDictionary<string, string>> Registros()
        {
            using (var f = File.OpenRead(_caminho))
            {
                f.Seek(_headerLen, SeekOrigin.Begin);
                var buf = new byte[_recLen];
                for (int i = 0; i < TotalRegistros; i++)
                {
                    int lido = f.Read(buf, 0, _recLen);
                    if (lido < _recLen) yield break;
                    if (buf[0] == (byte)'*') continue; // deletado
                    var rec = new Dictionary<string, string>(Campos.Count, StringComparer.OrdinalIgnoreCase);
                    foreach (var c in Campos)
                        rec[c.Nome] = Latin1.GetString(buf, c.Offset, c.Tamanho).Trim();
                    yield return rec;
                }
            }
        }
    }
}
