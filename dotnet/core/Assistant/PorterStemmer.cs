namespace DinoSaurList.Core.Assistant;

// C# adaptation of NLTK 3.10.4 PorterStemmer, NLTK_EXTENSIONS mode.
// Changed from Python for this port; NLTK Apache-2.0 license and attribution are in Licenses/.
internal static class PorterStemmer
{
    private static readonly Dictionary<string, string> Irregular = new()
    {
        ["sky"] = "sky",
        ["skies"] = "sky",
        ["dying"] = "die",
        ["lying"] = "lie",
        ["tying"] = "tie",
        ["news"] = "news",
        ["innings"] = "inning",
        ["inning"] = "inning",
        ["outings"] = "outing",
        ["outing"] = "outing",
        ["cannings"] = "canning",
        ["canning"] = "canning",
        ["howe"] = "howe",
        ["proceed"] = "proceed",
        ["exceed"] = "exceed",
        ["succeed"] = "succeed"
    };
    private static bool Consonant(string word, int i)
    {
        if ("aeiou".Contains(word[i])) return false;
        if (word[i] != 'y') return true;
        var negate = false;
        while (i > 0 && word[i] == 'y') { negate = !negate; i--; }
        return !"aeiou".Contains(word[i]) != negate;
    }
    private static int Measure(string word)
    {
        var count = 0; var previous = true;
        for (var i = 0; i < word.Length; i++) { var current = Consonant(word, i); if (!previous && current) count++; previous = current; }
        return count;
    }
    private static bool Vowel(string word) => Enumerable.Range(0, word.Length).Any(i => !Consonant(word, i));
    private static bool Double(string word) => word.Length >= 2 && word[^1] == word[^2] && Consonant(word, word.Length - 1);
    private static bool Cvc(string w) => w.Length >= 3 && Consonant(w, w.Length - 3) && !Consonant(w, w.Length - 2) && Consonant(w, w.Length - 1) && !"wxy".Contains(w[^1])
        || w.Length == 2 && !Consonant(w, 0) && Consonant(w, 1);
    private static string Apply(string word, params (string Suffix, string Replacement, Func<string, bool>? Condition)[] rules)
    {
        foreach (var (suffix, replacement, condition) in rules)
        {
            if (suffix == "*d")
            {
                if (!Double(word)) continue;
                var stem = word[..^2]; return condition is null || condition(stem) ? stem + replacement : word;
            }
            if (!word.EndsWith(suffix, StringComparison.Ordinal)) continue;
            var result = suffix.Length == 0 ? word : word[..^suffix.Length];
            return condition is null || condition(result) ? result + replacement : word;
        }
        return word;
    }
    private static bool Positive(string s) => Measure(s) > 0;
    private static string Step2(string word)
    {
        if (word.EndsWith("alli", StringComparison.Ordinal) && Positive(word[..^4])) return Step2(word[..^4] + "al");
        return Apply(word, ("ational", "ate", Positive), ("tional", "tion", Positive), ("enci", "ence", Positive), ("anci", "ance", Positive), ("izer", "ize", Positive),
            ("bli", "ble", Positive), ("alli", "al", Positive), ("entli", "ent", Positive), ("eli", "e", Positive), ("ousli", "ous", Positive),
            ("ization", "ize", Positive), ("ation", "ate", Positive), ("ator", "ate", Positive), ("alism", "al", Positive), ("iveness", "ive", Positive),
            ("fulness", "ful", Positive), ("ousness", "ous", Positive), ("aliti", "al", Positive), ("iviti", "ive", Positive), ("biliti", "ble", Positive),
            ("fulli", "ful", Positive), ("logi", "log", _ => Positive(word[..^3])));
    }
    public static string Stem(string word)
    {
        word = word.ToLowerInvariant();
        if (Irregular.TryGetValue(word, out var special)) return special;
        if (word.Length <= 2) return word;
        word = word.EndsWith("ies", StringComparison.Ordinal) && word.Length == 4 ? word[..^3] + "ie"
            : Apply(word, ("sses", "ss", null), ("ies", "i", null), ("ss", "ss", null), ("s", "", null));
        if (word.EndsWith("ied", StringComparison.Ordinal)) word = word[..^3] + (word.Length == 4 ? "ie" : "i");
        else if (word.EndsWith("eed", StringComparison.Ordinal)) { if (Positive(word[..^3])) word = word[..^3] + "ee"; }
        else
        {
            var suffix = new[] { "ed", "ing" }.FirstOrDefault(s => word.EndsWith(s, StringComparison.Ordinal) && Vowel(word[..^s.Length]));
            if (suffix is not null)
            {
                var stem = word[..^suffix.Length];
                word = Apply(stem, ("at", "ate", null), ("bl", "ble", null), ("iz", "ize", null), ("*d", stem[^1].ToString(), _ => !"lsz".Contains(stem[^1])), ("", "e", s => Measure(s) == 1 && Cvc(s)));
            }
        }
        word = Apply(word, ("y", "i", s => s.Length > 1 && Consonant(s, s.Length - 1)));
        word = Step2(word);
        word = Apply(word, ("icate", "ic", Positive), ("ative", "", Positive), ("alize", "al", Positive), ("iciti", "ic", Positive), ("ical", "ic", Positive), ("ful", "", Positive), ("ness", "", Positive));
        Func<string, bool> greater = s => Measure(s) > 1;
        word = Apply(word, ("al", "", greater), ("ance", "", greater), ("ence", "", greater), ("er", "", greater), ("ic", "", greater), ("able", "", greater), ("ible", "", greater),
            ("ant", "", greater), ("ement", "", greater), ("ment", "", greater), ("ent", "", greater), ("ion", "", s => greater(s) && s.Length > 0 && "st".Contains(s[^1])),
            ("ou", "", greater), ("ism", "", greater), ("ate", "", greater), ("iti", "", greater), ("ous", "", greater), ("ive", "", greater), ("ize", "", greater));
        if (word.EndsWith('e')) { var stem = word[..^1]; if (Measure(stem) > 1 || Measure(stem) == 1 && !Cvc(stem)) word = stem; }
        var current = word; return Apply(word, ("ll", "l", _ => Measure(current[..^1]) > 1));
    }
}
