using System.Globalization;
using System.Text.Json;

namespace Win2DS5.Core;

public sealed class WordMemory
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly HashSet<(char Left, char Right)> Neighbors = BuildNeighbors();
    private readonly Dictionary<string, int> _builtin;
    private readonly Dictionary<string, Dictionary<string, int>> _builtinPairs;
    private readonly Dictionary<string, int> _personal = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, int>> _personalPairs = new(StringComparer.Ordinal);
    private readonly string[] _common;
    private readonly Dictionary<char, string[]> _byFirst;
    private readonly Dictionary<int, string[]> _edits;
    private string? _path;

    public WordMemory()
    {
        var lexicon = Lexicon.Shared;
        _builtin = lexicon.Words;
        _builtinPairs = lexicon.Pairs;
        _common = lexicon.Common;
        _byFirst = lexicon.ByFirst;
        _edits = lexicon.Edits;
    }

    public WordMemory(Dictionary<string, int> words, Dictionary<string, Dictionary<string, int>>? pairs = null)
    {
        _builtin = new Dictionary<string, int>(words, StringComparer.Ordinal);
        _builtinPairs = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        if (pairs != null)
        {
            foreach (var left in pairs)
            {
                _builtinPairs[left.Key] = new Dictionary<string, int>(left.Value, StringComparer.Ordinal);
            }
        }

        var index = Lexicon.Index(_builtin);
        _common = index.Common;
        _byFirst = index.ByFirst;
        _edits = index.Edits;
    }

    public int KnownWords => _builtin.Count;

    public void UseStore(string path)
    {
        _path = path;
        try
        {
            if (!File.Exists(path))
                return;
            var file = JsonSerializer.Deserialize<MemoryFile>(File.ReadAllText(path), Json);
            if (file?.Words != null)
            {
                foreach (var pair in file.Words)
                {
                    var word = Norm(pair.Key);
                    if (Rememberable(word) && pair.Value > 0)
                        _personal[word] = pair.Value;
                }
            }

            if (file?.Pairs == null)
                return;
            foreach (var pair in file.Pairs)
            {
                var parts = pair.Key.Split('\u001f');
                if (parts.Length != 2 || pair.Value <= 0)
                    continue;
                var left = Norm(parts[0]);
                var right = Norm(parts[1]);
                if (!Rememberable(left) || !Rememberable(right))
                    continue;
                Add(_personalPairs, left, right, pair.Value, increment: false);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
    }

    public bool Learn(string previous, string word)
    {
        word = Norm(word);
        if (!Rememberable(word))
            return false;
        _personal[word] = Math.Min(100_000, Count(_personal, word) + 1);
        previous = Norm(previous);
        if (Rememberable(previous))
            Add(_personalPairs, previous, word, 1, increment: true);
        Trim();
        Save();
        return true;
    }

    public void Unlearn(string previous, string word)
    {
        word = Norm(word);
        if (_personal.TryGetValue(word, out var count))
        {
            if (count <= 1)
                _personal.Remove(word);
            else
                _personal[word] = count - 1;
        }

        previous = Norm(previous);
        if (previous.Length > 0 && _personalPairs.TryGetValue(previous, out var next) && next.TryGetValue(word, out var pair))
        {
            if (pair <= 1)
                next.Remove(word);
            else
                next[word] = pair - 1;
            if (next.Count == 0)
                _personalPairs.Remove(previous);
        }

        Save();
    }

    public List<string> Suggest(string prefix, string previous, int take)
    {
        prefix = Norm(prefix);
        previous = Norm(previous);
        if (take <= 0)
            return [];
        if (prefix.Length == 0 && previous.Length == 0)
            return [];

        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        if (prefix.Length == 0)
        {
            ConsiderPairs(previous, scores);
            if (scores.Count < take)
            {
                foreach (var word in _common)
                    Consider(scores, word, previous, 0, prefix);
            }
        }
        else
        {
            if (_byFirst.TryGetValue(prefix[0], out var bucket))
            {
                foreach (var word in bucket)
                {
                    if (word.StartsWith(prefix, StringComparison.Ordinal))
                        Consider(scores, word, previous, 0, prefix);
                }
            }

            foreach (var word in _personal.Keys)
            {
                if (word.StartsWith(prefix, StringComparison.Ordinal))
                    Consider(scores, word, previous, 0, prefix);
            }

            bool known = _builtin.ContainsKey(prefix) || _personal.ContainsKey(prefix);
            if (!known && prefix.Length >= 3 && scores.Count < 3)
                ConsiderEdits(prefix, previous, scores);
        }

        return scores
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key.Length)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(take)
            .Select(pair => pair.Key)
            .ToList();
    }

    private void ConsiderPairs(string previous, Dictionary<string, double> scores)
    {
        if (_personalPairs.TryGetValue(previous, out var personal))
        {
            foreach (var word in personal.Keys)
                Consider(scores, word, previous, 0, "");
        }

        if (_builtinPairs.TryGetValue(previous, out var builtin))
        {
            foreach (var word in builtin.Keys)
                Consider(scores, word, previous, 0, "");
        }
    }

    private void ConsiderEdits(string prefix, string previous, Dictionary<string, double> scores)
    {
        int length = prefix.Length;
        for (int size = Math.Max(1, length - 2); size <= length + 2; size++)
        {
            if (!_edits.TryGetValue(size, out var bucket))
                continue;
            foreach (var word in bucket)
            {
                if (word.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                int distance = Distance(prefix, word, 2);
                if (distance == 1 || (distance == 2 && length >= 6))
                    Consider(scores, word, previous, distance, prefix);
            }
        }

        foreach (var word in _personal.Keys)
        {
            if (Math.Abs(word.Length - length) > 2 || word.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            int distance = Distance(prefix, word, 2);
            if (distance == 1 || (distance == 2 && length >= 6))
                Consider(scores, word, previous, distance, prefix);
        }
    }

    private void Consider(Dictionary<string, double> scores, string word, string previous, int distance, string prefix)
    {
        if (word.Length == 0)
            return;
        double score = Score(word, previous, distance, prefix);
        if (!scores.TryGetValue(word, out var existing) || score > existing)
            scores[word] = score;
    }

    private double Score(string word, string previous, int distance, string prefix)
    {
        double score = -distance * 2.25;
        if (_personal.TryGetValue(word, out int personal))
            score += 6 + (personal * 2);
        if (_builtin.TryGetValue(word, out int builtin))
            score += Math.Log10(builtin + 1);
        if (previous.Length > 0)
        {
            int personalPair = Pair(_personalPairs, previous, word);
            if (personalPair > 0)
                score += 12 + (personalPair * 3);
            int builtinPair = Pair(_builtinPairs, previous, word);
            if (builtinPair > 0)
                score += 4 + (Math.Log10(builtinPair + 1) * 1.5);
        }

        if (prefix.Length > 0 && word == prefix)
            score += 3.5;
        if (distance == 1 && NeighborEdit(prefix, word))
            score += 0.8;
        return score;
    }

    private static int Pair(Dictionary<string, Dictionary<string, int>> table, string previous, string word) =>
        table.TryGetValue(previous, out var next) && next.TryGetValue(word, out var count) ? count : 0;

    private static int Count(Dictionary<string, int> table, string word) =>
        table.TryGetValue(word, out var count) ? count : 0;

    private static void Add(Dictionary<string, Dictionary<string, int>> table, string left, string right, int amount, bool increment)
    {
        if (!table.TryGetValue(left, out var next))
            table[left] = next = new Dictionary<string, int>(StringComparer.Ordinal);
        int current = next.TryGetValue(right, out var count) ? count : 0;
        next[right] = Math.Min(100_000, increment ? current + amount : amount);
    }

    private void Trim()
    {
        while (_personal.Count > 3000)
        {
            string? worst = null;
            int lowest = int.MaxValue;
            foreach (var pair in _personal)
            {
                if (pair.Value < lowest)
                {
                    lowest = pair.Value;
                    worst = pair.Key;
                }
            }

            if (worst == null)
                break;
            _personal.Remove(worst);
        }

        while (PairCount() > 8000)
        {
            string? left = null;
            string? right = null;
            int lowest = int.MaxValue;
            foreach (var outer in _personalPairs)
            {
                foreach (var inner in outer.Value)
                {
                    if (inner.Value < lowest)
                    {
                        lowest = inner.Value;
                        left = outer.Key;
                        right = inner.Key;
                    }
                }
            }

            if (left == null || right == null || !_personalPairs.TryGetValue(left, out var next))
                break;
            next.Remove(right);
            if (next.Count == 0)
                _personalPairs.Remove(left);
        }
    }

    private int PairCount()
    {
        int count = 0;
        foreach (var next in _personalPairs.Values)
            count += next.Count;
        return count;
    }

    private void Save()
    {
        if (string.IsNullOrEmpty(_path))
            return;
        try
        {
            var folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            var file = new MemoryFile();
            foreach (var pair in _personal)
                file.Words[pair.Key] = pair.Value;
            foreach (var left in _personalPairs)
            {
                foreach (var right in left.Value)
                    file.Pairs[left.Key + "\u001f" + right.Key] = right.Value;
            }

            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(file, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Norm(string word) => word.Trim().ToLowerInvariant();

    private static bool Rememberable(string word)
    {
        if (word is "a" or "i")
            return true;
        if (word.Length is < 2 or > 32 || !word.Any(char.IsLetter))
            return false;
        foreach (var letter in word)
        {
            if (!char.IsLetterOrDigit(letter) && letter is not '\'' and not '-')
                return false;
        }

        return true;
    }

    private static bool NeighborEdit(string typed, string word)
    {
        if (typed.Length != word.Length || typed.Length == 0)
            return false;
        int first = -1;
        for (int i = 0; i < typed.Length; i++)
        {
            if (typed[i] == word[i])
                continue;
            if (first >= 0)
            {
                return i == first + 1
                    && typed[first] == word[i]
                    && typed[i] == word[first]
                    && Neighbors.Contains((typed[first], typed[i]));
            }

            first = i;
        }

        return first >= 0 && Neighbors.Contains((typed[first], word[first]));
    }

    private static int Distance(string left, string right, int max)
    {
        int rows = left.Length;
        int columns = right.Length;
        if (Math.Abs(rows - columns) > max)
            return max + 1;
        var previous = new int[columns + 1];
        var before = new int[columns + 1];
        var current = new int[columns + 1];
        for (int column = 0; column <= columns; column++)
            previous[column] = column;
        for (int row = 1; row <= rows; row++)
        {
            current[0] = row;
            int best = current[0];
            for (int column = 1; column <= columns; column++)
            {
                int cost = left[row - 1] == right[column - 1] ? 0 : 1;
                int value = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), previous[column - 1] + cost);
                if (row > 1 && column > 1 && left[row - 1] == right[column - 2] && left[row - 2] == right[column - 1])
                    value = Math.Min(value, before[column - 2] + 1);
                current[column] = value;
                if (value < best)
                    best = value;
            }

            if (best > max)
                return max + 1;
            (before, previous, current) = (previous, current, before);
        }

        return previous[columns];
    }

    private static HashSet<(char Left, char Right)> BuildNeighbors()
    {
        var set = new HashSet<(char, char)>();
        string[] rows = ["`1234567890-=", "qwertyuiop[]\\", "asdfghjkl;'", "zxcvbnm,./"];
        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
            {
                void Link(int nextRow, int nextColumn)
                {
                    if (nextRow < 0 || nextRow >= rows.Length || nextColumn < 0 || nextColumn >= rows[nextRow].Length)
                        return;
                    char left = rows[row][column];
                    char right = rows[nextRow][nextColumn];
                    if (left != right)
                        set.Add((left, right));
                }

                Link(row, column - 1);
                Link(row, column + 1);
                Link(row - 1, column);
                Link(row - 1, column - 1);
                Link(row + 1, column);
                Link(row + 1, column + 1);
            }
        }

        return set;
    }

    private sealed class MemoryFile
    {
        public Dictionary<string, int> Words { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Pairs { get; set; } = new(StringComparer.Ordinal);
    }

    private static class Lexicon
    {
        private static Bundle? _shared;
        private static readonly object Gate = new();

        public static Bundle Shared
        {
            get
            {
                if (_shared != null)
                    return _shared;
                lock (Gate)
                {
                    _shared ??= Load();
                    return _shared;
                }
            }
        }

        public static Indexes Index(Dictionary<string, int> words)
        {
            var frequent = words.Where(pair => pair.Value >= 40).ToArray();
            return new Indexes
            {
                Common = words.OrderByDescending(pair => pair.Value).Take(8).Select(pair => pair.Key).ToArray(),
                ByFirst = words.GroupBy(pair => pair.Key[0]).ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).ToArray()),
                Edits = frequent.GroupBy(pair => pair.Key.Length).ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).ToArray()),
            };
        }

        private static Bundle Load()
        {
            var words = new Dictionary<string, int>(StringComparer.Ordinal);
            var pairs = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            var text = Read();
            if (!string.IsNullOrEmpty(text))
                Parse(text, words, pairs);
            if (words.Count < 100)
                Parse(Fallback, words, pairs);
            var index = Index(words);
            return new Bundle
            {
                Words = words,
                Pairs = pairs,
                Common = index.Common,
                ByFirst = index.ByFirst,
                Edits = index.Edits,
            };
        }

        private static string? Read()
        {
            using var stream = typeof(WordMemory).Assembly.GetManifestResourceStream("Win2DS5.Core.lexicon.txt");
            if (stream == null)
                return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static void Parse(string text, Dictionary<string, int> words, Dictionary<string, Dictionary<string, int>> pairs)
        {
            bool following = false;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;
                if (line == "---")
                {
                    following = true;
                    continue;
                }

                int tab = line.IndexOf('\t');
                if (tab <= 0 || !int.TryParse(line[(tab + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count <= 0)
                    continue;
                if (!following)
                {
                    var token = line[..tab];
                    if (Rememberable(token))
                        words[token] = count;
                    continue;
                }

                var phrase = line[..tab];
                int space = phrase.IndexOf(' ');
                if (space <= 0)
                    continue;
                var left = phrase[..space];
                var right = phrase[(space + 1)..];
                if (!Rememberable(left) || !Rememberable(right))
                    continue;
                Add(pairs, left, right, count, increment: false);
            }
        }

        private const string Fallback = """
            the	80000
            to	40000
            that	15000
            this	12000
            and	35000
            of	30000
            you	18000
            it	16000
            in	25000
            for	14000
            is	20000
            was	12000
            be	11000
            on	10000
            have	8000
            with	8000
            not	7000
            they	7000
            at	9000
            a	20000
            i	9000
            there	5000
            their	4000
            them	3000
            then	2500
            ten	800
            tea	400
            receive	900
            ---
            of the	9000
            to the	6000
            in the	6000
            on the	2000
            and the	2000
            for the	1500
            to be	1000
            """;
    }

    private sealed class Bundle
    {
        public Dictionary<string, int> Words { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, int>> Pairs { get; init; } = new(StringComparer.Ordinal);
        public string[] Common { get; init; } = [];
        public Dictionary<char, string[]> ByFirst { get; init; } = [];
        public Dictionary<int, string[]> Edits { get; init; } = [];
    }

    private sealed class Indexes
    {
        public string[] Common { get; init; } = [];
        public Dictionary<char, string[]> ByFirst { get; init; } = [];
        public Dictionary<int, string[]> Edits { get; init; } = [];
    }
}
