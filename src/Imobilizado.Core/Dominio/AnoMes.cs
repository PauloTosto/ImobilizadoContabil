using System;

namespace Imobilizado.Core.Dominio
{
    /// <summary>
    /// Competência (ano/mês). A depreciação do Ju2 raciocina sempre em meses
    /// fechados — dia não importa, só ano e mês.
    /// </summary>
    public readonly struct AnoMes : IComparable<AnoMes>, IEquatable<AnoMes>
    {
        public int Ano { get; }
        public int Mes { get; }

        public AnoMes(int ano, int mes)
        {
            if (mes < 1 || mes > 12) throw new ArgumentOutOfRangeException(nameof(mes));
            Ano = ano;
            Mes = mes;
        }

        /// <summary>
        /// Parse de data DBF no formato "YYYYMMDD". Retorna null se vazia/inválida.
        /// Datas com ano &lt; 1900 são tratadas como "sem data" — protege contra a data
        /// vazia do VFP gravada como 0001-01-01 (DateTime.MinValue) ser lida como baixa.
        /// </summary>
        public static AnoMes? DeDataDbf(string yyyymmdd)
        {
            if (string.IsNullOrWhiteSpace(yyyymmdd)) return null;
            var s = yyyymmdd.Trim();
            if (s.Length < 6) return null;
            if (!int.TryParse(s.Substring(0, 4), out var ano)) return null;
            if (!int.TryParse(s.Substring(4, 2), out var mes)) return null;
            if (mes < 1 || mes > 12) return null;
            if (ano < 1900) return null;
            return new AnoMes(ano, mes);
        }

        public static AnoMes De(DateTime d) => new AnoMes(d.Year, d.Month);

        public AnoMes MesAnterior() => Mes > 1 ? new AnoMes(Ano, Mes - 1) : new AnoMes(Ano - 1, 12);

        /// <summary>Quantidade de meses cheios de <paramref name="origem"/> até este (this - origem).</summary>
        public int MesesDesde(AnoMes origem) => (Ano - origem.Ano) * 12 + (Mes - origem.Mes);

        /// <summary>Último dia do mês, formato DBF "YYYYMMDD" — data que o Ju2 carimba no lançamento.</summary>
        public string UltimoDiaDbf()
        {
            int dia = DateTime.DaysInMonth(Ano, Mes);
            return $"{Ano:D4}{Mes:D2}{dia:D2}";
        }

        public bool EhAnteriorA(AnoMes outro) => CompareTo(outro) < 0;

        public int CompareTo(AnoMes o) => Ano != o.Ano ? Ano.CompareTo(o.Ano) : Mes.CompareTo(o.Mes);
        public bool Equals(AnoMes o) => Ano == o.Ano && Mes == o.Mes;
        public override bool Equals(object o) => o is AnoMes a && Equals(a);
        public override int GetHashCode() => Ano * 100 + Mes;
        public override string ToString() => $"{Ano:D4}{Mes:D2}";
    }
}
