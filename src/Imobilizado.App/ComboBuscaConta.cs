using System;
using System.Linq;
using System.Windows.Forms;

namespace Imobilizado.App
{
    /// <summary>
    /// ComboBox editável com BUSCA DINÂMICA: ao digitar, filtra a lista de contas por
    /// conteúdo (casa em qualquer parte — número/apelido OU descrição) e abre o dropdown
    /// com os resultados. Os itens são "DESC2 — Descrição"; <see cref="Valor"/> devolve só o DESC2.
    /// </summary>
    public sealed class ComboBuscaConta : ComboBox
    {
        private string[] _todos = new string[0];
        private bool _reentrante;

        public ComboBuscaConta()
        {
            DropDownStyle = ComboBoxStyle.DropDown;
            MaxDropDownItems = 16;
        }

        /// <summary>Carrega a lista completa (formato "DESC2 — Descrição").</summary>
        public void Carregar(System.Collections.Generic.IEnumerable<string> itens)
        {
            _todos = itens?.ToArray() ?? new string[0];
            DefinirItens(_todos);
        }

        private void DefinirItens(string[] itens)
        {
            BeginUpdate();
            Items.Clear();
            Items.AddRange(itens);
            EndUpdate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (_reentrante) return;
            Filtrar();
        }

        private void Filtrar()
        {
            _reentrante = true;
            try
            {
                var texto = Text;
                var f = texto.Trim();
                var matches = (f.Length == 0
                    ? _todos
                    : _todos.Where(x => x.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0).ToArray());
                if (matches.Length > 500) matches = matches.Take(500).ToArray();

                int sel = SelectionStart;
                DefinirItens(matches);
                Text = texto;                                   // mantém o que o usuário digitou
                SelectionStart = Math.Min(sel, (Text ?? "").Length);
                SelectionLength = 0;

                if (Focused && f.Length > 0 && matches.Length > 0)
                {
                    DroppedDown = true;
                    Cursor.Current = Cursors.Default;           // abrir o dropdown troca o cursor
                }
            }
            finally { _reentrante = false; }
        }

        /// <summary>O valor da conta (DESC2) — a parte antes de " — ".</summary>
        public string Valor
        {
            get
            {
                var t = (Text ?? "").Trim();
                int i = t.IndexOf(" — ", StringComparison.Ordinal);
                return i >= 0 ? t.Substring(0, i).Trim() : t;
            }
            set
            {
                var v = (value ?? "").Trim();
                var item = Array.Find(_todos, x => x.StartsWith(v + " — ", StringComparison.OrdinalIgnoreCase));
                _reentrante = true;
                DefinirItens(_todos);
                Text = item ?? v;
                _reentrante = false;
            }
        }
    }
}
