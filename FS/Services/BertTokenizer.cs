using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BetterFileSys.Services
{
    /// <summary>
    /// A high-performance, self-contained BERT WordPiece Tokenizer for uncased models
    /// </summary>
    public class BertTokenizer
    {
        private readonly Dictionary<string, int> _vocab = new(StringComparer.Ordinal);
        private readonly int _unkId = 100;
        private readonly int _clsId = 101;
        private readonly int _sepId = 102;

        public BertTokenizer(string vocabPath)
        {
            if (!File.Exists(vocabPath))
                throw new FileNotFoundException("Vocabulary file not found", vocabPath);

            int id = 0;
            foreach (var line in File.ReadLines(vocabPath))
            {
                var token = line.Trim();
                if (!string.IsNullOrEmpty(token))
                {
                    _vocab[token] = id;
                }
                id++;
            }

            // Retrieve special token IDs if they exist in vocab, otherwise fallback to defaults
            if (_vocab.TryGetValue("[UNK]", out int unk)) _unkId = unk;
            if (_vocab.TryGetValue("[CLS]", out int cls)) _clsId = cls;
            if (_vocab.TryGetValue("[SEP]", out int sep)) _sepId = sep;
        }

        /// <summary>
        /// Tokenizes full text into WordPiece IDs including CLS and SEP tokens
        /// </summary>
        public int[] Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new[] { _clsId, _sepId };

            var basicTokens = BasicTokenize(text);
            var resultIds = new List<int> { _clsId };

            foreach (var token in basicTokens)
            {
                var wordPieces = WordPieceTokenize(token);
                resultIds.AddRange(wordPieces);
            }

            resultIds.Add(_sepId);
            return resultIds.ToArray();
        }

        /// <summary>
        /// Splits text on spaces and punctuation/symbols to produce standard basic tokens
        /// </summary>
        private List<string> BasicTokenize(string text)
        {
            text = text.ToLowerInvariant();
            var tokens = new List<string>();
            var currentWord = new StringBuilder();

            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (currentWord.Length > 0)
                    {
                        tokens.Add(currentWord.ToString());
                        currentWord.Clear();
                    }
                }
                else if (char.IsPunctuation(c) || IsSymbol(c))
                {
                    if (currentWord.Length > 0)
                    {
                        tokens.Add(currentWord.ToString());
                        currentWord.Clear();
                    }
                    tokens.Add(c.ToString());
                }
                else
                {
                    currentWord.Append(c);
                }
            }

            if (currentWord.Length > 0)
            {
                tokens.Add(currentWord.ToString());
            }

            return tokens;
        }

        /// <summary>
        /// Greedy WordPiece (MaxMatch) tokenization algorithm
        /// </summary>
        private List<int> WordPieceTokenize(string word)
        {
            var pieces = new List<int>();

            if (word.Length > 100)
            {
                pieces.Add(_unkId);
                return pieces;
            }

            int start = 0;
            while (start < word.Length)
            {
                int end = word.Length;
                int matchId = -1;

                while (start < end)
                {
                    string substr = word.Substring(start, end - start);
                    if (start > 0)
                    {
                        substr = "##" + substr;
                    }

                    if (_vocab.TryGetValue(substr, out int id))
                    {
                        matchId = id;
                        break;
                    }
                    end--;
                }

                if (matchId == -1)
                {
                    pieces.Clear();
                    pieces.Add(_unkId);
                    break;
                }

                pieces.Add(matchId);
                start = end;
            }

            return pieces;
        }

        private static bool IsSymbol(char c)
        {
            var category = char.GetUnicodeCategory(c);
            return category == System.Globalization.UnicodeCategory.MathSymbol ||
                   category == System.Globalization.UnicodeCategory.CurrencySymbol ||
                   category == System.Globalization.UnicodeCategory.ModifierSymbol ||
                   category == System.Globalization.UnicodeCategory.OtherSymbol;
        }
    }
}
