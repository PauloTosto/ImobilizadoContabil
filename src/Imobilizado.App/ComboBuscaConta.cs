using System;
using System.Linq;
using System.Windows.Forms;

namespace Imobilizado.App
{
    /// <summary>
    /// ComboBox editável com BUSCA DINÂMICA: ao digitar, filtra a lista de contas por
    /// conteúdo (casa em qualquer parte — número/apelido OU descrição) e abre o dropdown.
    /// Os itens EXIBIDOS são só o DESC2 (o número + descrição da conta aparecem no rótulo
    /// abaixo do campo); a BUSCA continua casando no texto completo "DESC2 — Descrição".
    /// </summary>
    public sealed class ComboBuscaConta : ComboBox
    {
        private string[] _todos = new string[0];   // texto completo "DESC2 — Descrição" (p/ busca)
        private bool _reentrante;

        public ComboBuscaConta()
        {
            DropDownStyle = ComboBoxStyle.DropDown;
            MaxDropDownItems = 16;
        }

        /// <summary>Carrega a lista completa (formato "DESC2 — Descrição"; só o DESC2 é exibido).</summary>
        public void Carregar(System.Collections.Generic.IEnumerable<string> itens)
        {
            _todos = itens?.ToArray() ?? new string[0];
            DefinirItens(_todos);
        }

        /// <summary>Só a parte DESC2 (antes de " — ") de um item completo.</summary>
        private static string SoDesc2(string item)
        {
            int i = (item ?? "").IndexOf(" — ", StringComparison.Ordinal);
            return i >= 0 ? item.Substring(0, i).Trim() : (item ?? "").Trim();
        }

        private void DefinirItens(string[] completos)
        {
            BeginUpdate();
            Items.Clear();
            Items.AddRange(completos.Select(SoDesc2).Distinct(StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());
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
                // a busca casa no texto COMPLETO (apelido OU descrição); a exibição é só o DESC2
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

        /// <summary>O valor da conta (DESC2) — o texto exibido já é só o DESC2.</summary>
        public string Valor
        {
            get
            {
                var t = (Text ?? "").Trim();
                int i = t.IndexOf(" — ", StringComparison.Ordinal);   // tolera texto antigo completo
                return i >= 0 ? t.Substring(0, i).Trim() : t;
            }
            set
            {
                var v = (value ?? "").Trim();
                _reentrante = true;
                DefinirItens(_todos);
                Text = v;
                _reentrante = false;
            }
        }
    }
}
