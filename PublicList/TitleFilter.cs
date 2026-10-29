using System.Text;

namespace OpenKogama.PublicList;

static class TitleFilter
{
    static readonly string[] Stems =
    [
        "fuck", "bitch", "whore", "slut", "porn", "hentai", "xvideo", "onlyfans", "blowjob", "handjob", "dildo", "nsfw",
        "penis", "vagina", "pedophil", "pedofil", "hitler", "siegheil", "whitepower", "killyourself", "killurself",
        "behead", "decapitat", "dismember", "mutilat",
        "kurw", "chuj", "jeban", "jebac", "jebie", "jebn", "jebl", "pojeb", "zjeb", "wyjeb", "zajeb", "pierdol", "pierdal", "pizd",
        "dziwk", "cipk", "kutas", "ruchan",
        "caralh", "bucet", "arrombad", "xoxot", "fodase", "hijodeputa", "pendej", "chingad", "chingar", "maricon",
        "hurensohn", "schlampe", "orospu", "siktir",
        "blyat", "blyad", "nahui", "nahuy",
        "хуй", "хуе", "хуя", "пизд", "бляд", "блят", "ебан", "ебат", "ебал", "заеб",
    ];

    static readonly string[] Exact = ["nigg", "faggot", "pussy"];

    static readonly HashSet<string> Words =
    [
        "ass", "asses", "asshole", "assholes", "shit", "shits", "shitty", "bullshit", "shithead", "cunt", "cunts", "dick", "dicks", "cum", "tits", "titty", "titties", "boobs",
        "sex", "sexy", "sexo", "seks", "nude", "nudes", "naked", "anal", "rape", "raped", "rapist", "pedo", "pedos",
        "nazi", "nazis", "heil", "kkk", "fag", "fags", "retard", "retarded", "kys", "niga", "nigas", "gore", "gory",
        "puta", "putas", "puto", "porra", "foda", "viado", "piroca", "verga", "cabron", "culero",
        "fick", "ficken", "fickt", "fotze", "amk", "sikis", "huj", "cipa", "ciota", "ruchac", "suka", "cyka", "сука",
    ];

    static readonly Dictionary<char, char> Leet = new()
    {
        ['0'] = 'o', ['1'] = 'i', ['3'] = 'e', ['4'] = 'a', ['5'] = 's', ['7'] = 't', ['@'] = 'a', ['$'] = 's', ['!'] = 'i', ['|'] = 'i', ['€'] = 'e',
        ['ą'] = 'a', ['ć'] = 'c', ['ę'] = 'e', ['ł'] = 'l', ['ń'] = 'n', ['ó'] = 'o', ['ś'] = 's', ['ź'] = 'z', ['ż'] = 'z',
        ['á'] = 'a', ['à'] = 'a', ['â'] = 'a', ['ã'] = 'a', ['ä'] = 'a', ['ç'] = 'c', ['é'] = 'e', ['è'] = 'e', ['ê'] = 'e', ['ë'] = 'e',
        ['í'] = 'i', ['ì'] = 'i', ['î'] = 'i', ['ï'] = 'i', ['ı'] = 'i', ['ñ'] = 'n', ['ò'] = 'o', ['ô'] = 'o', ['õ'] = 'o', ['ö'] = 'o',
        ['ú'] = 'u', ['ù'] = 'u', ['û'] = 'u', ['ü'] = 'u', ['ş'] = 's', ['ğ'] = 'g', ['ß'] = 's',
    };

    static readonly Dictionary<char, char> Lookalikes = new()
    {
        ['а'] = 'a', ['в'] = 'b', ['е'] = 'e', ['ё'] = 'e', ['к'] = 'k', ['м'] = 'm', ['н'] = 'h', ['о'] = 'o', ['р'] = 'p',
        ['с'] = 'c', ['т'] = 't', ['у'] = 'y', ['х'] = 'x', ['і'] = 'i',
    };

    static readonly string[] CollapsedStems = [.. Stems.Select(stem => Collapse(stem, 1))];

    public static bool Blocks(string title)
    {
        string text = title.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        if (text.Contains("1488"))
            return true;
        foreach (bool lookalikes in new[] { false, true })
        {
            string plain = string.Concat(text.Select(letter => Plain(letter, lookalikes)));
            string squashed = string.Concat(plain.Where(char.IsLetter));
            string single = Collapse(squashed, 1), twice = Collapse(squashed, 2);
            if (CollapsedStems.Any(single.Contains) || Exact.Any(twice.Contains))
                return true;
            foreach (string word in plain.Split(letter => !char.IsLetter(letter)))
                if (word.Length > 0 && Words.Contains(Collapse(word, 2)))
                    return true;
        }
        return false;
    }

    static char Plain(char letter, bool lookalikes) =>
        Leet.TryGetValue(letter, out char swapped) ? swapped
        : lookalikes && Lookalikes.TryGetValue(letter, out char latin) ? latin
        : letter;

    static string Collapse(string text, int most)
    {
        var result = new StringBuilder(text.Length);
        int run = 0;
        for (int i = 0; i < text.Length; i++)
        {
            run = i > 0 && text[i] == text[i - 1] ? run + 1 : 1;
            if (run <= most)
                result.Append(text[i]);
        }
        return result.ToString();
    }

    static string[] Split(this string text, Func<char, bool> separator)
    {
        List<string> words = [];
        var word = new StringBuilder();
        foreach (char letter in text)
        {
            if (separator(letter))
            {
                words.Add(word.ToString());
                word.Clear();
            }
            else
                word.Append(letter);
        }
        words.Add(word.ToString());
        return [.. words];
    }
}
