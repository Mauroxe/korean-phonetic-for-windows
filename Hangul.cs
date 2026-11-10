using System;
using System.Collections.Generic;

namespace KoPhonetic;

// Estado da sílaba em composição (L = inicial, V = vogal, T = final: 1 ou 2 jamo)
sealed class St
{
    public string Text = "", L = "", V = "", T = "";
    public St Clone() => (St)MemberwiseClone();
}

static class Hangul
{
    const string Ls = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
    const string Vs = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
    const string Ts = " ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";

    static readonly Dictionary<string, string> Combine = new()
    {
        ["ㅗㅏ"] = "ㅘ", ["ㅗㅐ"] = "ㅙ", ["ㅗㅣ"] = "ㅚ",
        ["ㅜㅓ"] = "ㅝ", ["ㅜㅔ"] = "ㅞ", ["ㅜㅣ"] = "ㅟ",
        ["ㅡㅣ"] = "ㅢ",
    };

    static readonly Dictionary<string, string> CF = new()
    {
        ["ㄱㅅ"] = "ㄳ", ["ㄴㅈ"] = "ㄵ", ["ㄴㅎ"] = "ㄶ", ["ㄹㄱ"] = "ㄺ",
        ["ㄹㅁ"] = "ㄻ", ["ㄹㅂ"] = "ㄼ", ["ㄹㅅ"] = "ㄽ", ["ㄹㅌ"] = "ㄾ",
        ["ㄹㅍ"] = "ㄿ", ["ㄹㅎ"] = "ㅀ", ["ㅂㅅ"] = "ㅄ",
    };

    public static bool IsVowel(string j) => Vs.IndexOf(j[0]) >= 0;

    public static string Comp(St s)
    {
        if (s.L == "" && s.V == "") return "";
        if (s.V == "") return s.L;
        if (s.L == "") return s.V;
        string tj = s.T.Length == 2 ? CF[s.T] : s.T;
        int ti = tj == "" ? 0 : Ts.IndexOf(tj[0]);
        int code = 0xAC00 + (Ls.IndexOf(s.L[0]) * 21 + Vs.IndexOf(s.V[0])) * 28 + ti;
        return ((char)code).ToString();
    }

    public static void Commit(St s)
    {
        s.Text += Comp(s);
        s.L = s.V = s.T = "";
    }

    public static void Put(St s, string ch)
    {
        Commit(s);
        s.Text += ch;
    }

    public static void Cons(St s, string c)
    {
        if (s.V == "")
        {
            if (s.L != "") Commit(s);
            s.L = c;
            return;
        }
        if (s.L != "")
        {
            if (s.T == "")
            {
                if (Ts.IndexOf(c[0]) > 0) { s.T = c; return; }   // ㄸ ㅃ ㅉ não podem ser final
            }
            else if (s.T.Length == 1 && CF.ContainsKey(s.T + c)) { s.T += c; return; }
        }
        Commit(s);
        s.L = c;
    }

    public static void Vow(St s, string v)
    {
        if (s.T != "")
        {
            // consoante final passa para a sílaba seguinte; ㄲ e ㅆ passam inteiros
            string m;
            if (s.T.Length == 2) { m = s.T.Substring(1); s.T = s.T.Substring(0, 1); }
            else { m = s.T; s.T = ""; }
            Commit(s);
            s.L = m;
            s.V = v;
            return;
        }
        if (s.V != "")
        {
            if (Combine.TryGetValue(s.V + v, out var c)) { s.V = c; return; }
            Commit(s);
        }
        s.V = v;
    }
}

// Traduz letras QWERTY em edições (apagar N caracteres + escrever texto)
sealed class Engine
{
    public Action<int, string> Emit = (b, t) => { };

    St s = new();
    readonly List<St> h = new();   // um estado por tecla => Backspace apaga um jamo de cada vez
    string shown = "";

    static readonly Dictionary<char, string> Map = new()
    {
        ['q'] = "ㅇ", ['w'] = "ㅡ", ['e'] = "ㅔ", ['E'] = "ㅖ", ['r'] = "ㄹ", ['t'] = "ㅌ",
        ['y'] = "ㅐ", ['Y'] = "ㅒ", ['u'] = "ㅜ", ['i'] = "ㅣ", ['o'] = "ㅗ", ['p'] = "ㅍ",
        ['a'] = "ㅏ", ['s'] = "ㅅ", ['S'] = "ㅆ", ['d'] = "ㄷ", ['D'] = "ㄸ", ['f'] = "ㅑ",
        ['g'] = "ㄱ", ['G'] = "ㄲ", ['h'] = "ㅎ", ['j'] = "ㅈ", ['J'] = "ㅉ", ['k'] = "ㅋ",
        ['l'] = "ㅛ", ['z'] = "ㅕ", ['x'] = "ㅠ", ['c'] = "ㅊ", ['v'] = "ㅓ", ['b'] = "ㅂ",
        ['B'] = "ㅃ", ['n'] = "ㄴ", ['m'] = "ㅁ",
    };

    public static string Jamo(char c) => Map.TryGetValue(c, out var j) ? j : "";

    public void Reset()
    {
        s = new St();
        h.Clear();
        shown = "";
    }

    void Push()
    {
        h.Add(s.Clone());
        if (h.Count > 400) h.RemoveAt(0);
    }

    void Sync()
    {
        string now = s.Text + Hangul.Comp(s);
        int c = 0;
        while (c < shown.Length && c < now.Length && shown[c] == now[c]) c++;
        int bs = shown.Length - c;
        string add = now.Substring(c);
        shown = now;
        if (bs > 0 || add.Length > 0) Emit(bs, add);
    }

    void Trim()
    {
        if (s.L == "" && s.V == "" && s.Text.Length > 300) Reset();
    }

    // 'upper' = Shift XOR Caps Lock. Maiúscula sem letra especial dá a letra normal (Shift+A -> ㅏ).
    public void Type(char letter, bool upper)
    {
        char c = upper ? char.ToUpperInvariant(letter) : char.ToLowerInvariant(letter);
        if (!Map.TryGetValue(c, out var j))
        {
            c = char.ToLowerInvariant(c);
            if (!Map.TryGetValue(c, out j)) return;
        }
        Push();
        if (Hangul.IsVowel(j)) Hangul.Vow(s, j);
        else Hangul.Cons(s, j);
        Sync();
        Trim();
    }

    // O espaço real passa para a aplicação; aqui só se regista no estado (sem emitir)
    public void Space()
    {
        Push();
        Hangul.Put(s, " ");
        shown = s.Text + Hangul.Comp(s);
        Trim();
    }

    // false = nada para desfazer (deixar passar o Backspace real)
    public bool Back()
    {
        if (h.Count == 0) return false;
        s = h[h.Count - 1];
        h.RemoveAt(h.Count - 1);
        Sync();
        return true;
    }
}
