﻿#region License

// https://github.com/TheBerkin/Rant
// 
// Copyright (c) 2017 Nicholas Fleck
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in the
// Software without restriction, including without limitation the rights to use, copy,
// modify, merge, publish, distribute, sublicense, and/or sell copies of the Software,
// and to permit persons to whom the Software is furnished to do so, subject to the
// following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
// PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
// CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE
// OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Rant.Core.Utilities;
using Rant.Vocabulary.Utilities;

namespace Rant.Vocabulary
{
    public sealed partial class RantDictionaryTable
    {
        /// <summary>
        /// Loads a RantDictionary from the file at the specified path.
        /// </summary>
        /// <param name="path">The path to the file to load.</param>
        /// <returns></returns>
        public static RantDictionaryTable FromLegacyFile(string path)
        {
            string name = "";
            string[] subtypes = { "default" };
            bool header = true;

            var scopedClassSet = new HashSet<string>();
            RantDictionaryEntry entry = null;
            var entries = new List<RantDictionaryEntry>();
            var entryStringes = new List<string>();
            var types = new Dictionary<string, EntryTypeDef>();
            var hiddenClasses = new HashSet<string> { "nsfw" };

            foreach (var token in DicLexer.Tokenize(path, File.ReadAllText(path)))
            {
                switch (token.Type)
                {
                    case DicTokenType.Directive:
                    {
                        var parts = VocabUtils.GetArgs(token.Value).ToArray();
                        if (!parts.Any()) continue;
                        string dirName = parts.First().ToLower();
                        var args = parts.Skip(1).ToArray();

                        switch (dirName)
                        {
                            case "name":
                                if (!header) LoadError(path, token, "The #name directive may only be used in the file header.");
                                if (args.Length != 1) LoadError(path, token, "#name directive expected one word:\r\n\r\n" + token.Value);
                                if (!Util.ValidateName(args[0])) LoadError(path, token, $"Invalid #name value: '{args[1]}'");
                                name = args[0].ToLower();
                                break;
                            case "subs":
                                if (!header) LoadError(path, token, "The #subs directive may only be used in the file header.");
                                subtypes = args.Select(s => s.Trim().ToLower()).ToArray();
                                break;
                            case "version": // Kept here for backwards-compatability
                                if (!header) LoadError(path, token, "The #version directive may only be used in the file header.");
                                break;
                            case "hidden":
                                if (!header) LoadError(path, token, "The #hidden directive may only be used in the file header.");
                                if (Util.ValidateName(args[0])) hiddenClasses.Add(args[0]);
                                break;
                            // Deprecated, remove in Rant 3
                            case "nsfw":
                                scopedClassSet.Add("nsfw");
                                break;
                            // Deprecated, remove in Rant 3
                            case "sfw":
                                scopedClassSet.Remove("nsfw");
                                break;
                            case "class":
                            {
                                if (args.Length < 2) LoadError(path, token, "The #class directive expects an operation and at least one value.");
                                switch (args[0].ToLower())
                                {
                                    case "add":
                                        foreach (string cl in args.Skip(1))
                                            scopedClassSet.Add(cl.ToLower());
                                        break;
                                    case "remove":
                                        foreach (string cl in args.Skip(1))
                                            scopedClassSet.Remove(cl.ToLower());
                                        break;
                                }
                            }
                                break;
                            case "type":
                            {
                                if (!header) LoadError(path, token, "The #type directive may only be used in the file header.");
                                if (args.Length != 3) LoadError(path, token, "#type directive requires 3 arguments.");
                                types.Add(args[0], new EntryTypeDef(args[0], args[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                                    Util.IsNullOrWhiteSpace(args[2]) ? null : new EntryTypeDefFilter(args[2])));
                            }
                                break;
                        }
                    }
                        break;
                    case DicTokenType.Entry:
                    {
                        if (Util.IsNullOrWhiteSpace(name))
                            LoadError(path, token, "Missing table name before entry list.");
                        if (Util.IsNullOrWhiteSpace(token.Value))
                            LoadError(path, token, "Encountered empty entry.");
                        header = false;
                        entry = new RantDictionaryEntry(token.Value.Split('/').Select(s => s.Trim()).ToArray(), scopedClassSet);
                        entries.Add(entry);
                        entryStringes.Add(token);
                    }
                        break;
                    case DicTokenType.DiffEntry:
                    {
                        if (Util.IsNullOrWhiteSpace(name))
                            LoadError(path, token, "Missing table name before entry list.");
                        if (Util.IsNullOrWhiteSpace(token.Value))
                            LoadError(path, token, "Encountered empty entry.");
                        header = false;
                        string first = null;
                        entry = new RantDictionaryEntry(token.Value.Split('/')
                            .Select((s, i) =>
                            {
                                if (i > 0) return Diff.Mark(first, s);
                                return first = s.Trim();
                            }).ToArray(), scopedClassSet);
                        entries.Add(entry);
                        entryStringes.Add(token);
                    }
                        break;
                    case DicTokenType.Property:
                    {
                        var parts = token.Value.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (!parts.Any()) LoadError(path, token, "Empty property field.");
                        switch (parts[0].ToLower())
                        {
                            case "class":
                            {
                                if (parts.Length < 2) continue;
                                foreach (string cl in parts[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    bool opt = cl.EndsWith("?");
                                    entry.AddClass(VocabUtils.GetString(opt ? cl.Substring(0, cl.Length - 1) : cl), opt);
                                }
                            }
                                break;
                            case "weight":
                            {
                                if (parts.Length != 2) LoadError(path, token, "'weight' property expected a value.");
                                int weight;
                                if (!Int32.TryParse(parts[1], out weight))
                                    LoadError(path, token, "Invalid weight value: '" + parts[1] + "'");
                                entry.Weight = weight;
                            }
                                break;
                            case "pron":
                            {
                                if (parts.Length != 2) LoadError(path, token, "'" + parts[0] + "' property expected a value.");
                                var pron =
                                    parts[1].Split('/')
                                        .Select(s => s.Trim())
                                        .ToArray();
                                if (subtypes.Length == pron.Length)
                                {
                                    for (int i = 0; i < entry.TermCount; i++)
                                        entry[i].Pronunciation = pron[i];
                                }
                            }
                                break;
                            default:
                            {
                                EntryTypeDef typeDef;
                                if (!types.TryGetValue(parts[0], out typeDef))
                                    LoadError(path, token, $"Unknown property name '{parts[0]}'.");
                                // Okay, it's a type.
                                if (parts.Length != 2) LoadError(path, token, "Missing type value.");
                                entry.AddClass(VocabUtils.GetString(parts[1]));
                                if (!typeDef.IsValidValue(parts[1]))
                                    LoadError(path, token, $"'{parts[1]}' is not a valid value for type '{typeDef.Name}'.");
                                break;
                            }
                        }
                    }
                        break;
                }
            }

            if (types.Any())
            {
                var eEntries = entries.GetEnumerator();
                var eEntryStringes = entryStringes.GetEnumerator();
                while (eEntries.MoveNext() && eEntryStringes.MoveNext())
                {
                    foreach (var type in types.Values)
                    {
                        if (!type.Test(eEntries.Current))
                        {
                            // TODO: Find a way to output multiple non-fatal table load errors without making a gigantic exception message.
                            LoadError(path, eEntryStringes.Current, $"Entry '{eEntries.Current}' does not satisfy type '{type.Name}'.");
                        }
                    }
                }
            }

            var table = new RantDictionaryTable(name, subtypes.Length, hiddenClasses);
            for (int i = 0; i < subtypes.Length; i++)
                table.AddSubtype(subtypes[i], i);

            for (int i = 0; i < entries.Count; i++)
                table.AddEntry(entries[i]);

            table.Commit();

            return table;
        }

        private static void LoadError(string file, DicToken data, string message)
        {
            throw new RantLegacyTableLoadException(file, data, message);
        }
    }

    internal class RantLegacyTableLoadException : Exception
    {
        public RantLegacyTableLoadException(string file, DicToken data, string message)
            : base($"{file}: (Line {data.Line}) {message}")
        {
        }
    }

    internal class EntryTypeDefFilter
    {
        private readonly _<string, bool>[] _filterParts;
        private readonly Regex _filterRegex = new Regex(@"!?\w+");

        public EntryTypeDefFilter(string filter)
        {
            if (filter.Trim() == "*") return;
            _filterParts = filter
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(s => _filterRegex.IsMatch(s))
                .Select(s => _.Create(s.TrimStart('!'), s.StartsWith("!")))
                .ToArray();
        }

        private bool DoTest(RantDictionaryEntry entry)
        {
            return _filterParts == null || _filterParts.All(f => entry.ContainsClass(f.Item1) == f.Item2);
        }

        /// <summary>
        /// Determines whether a type should apply to the specifed entry according to the specified filter.
        /// </summary>
        /// <param name="filter">The filter to test with.</param>
        /// <param name="entry">The entry to test.</param>
        /// <returns></returns>
        public static bool Test(EntryTypeDefFilter filter, RantDictionaryEntry entry) => filter?.DoTest(entry) ?? false;
    }

    internal class EntryTypeDef
    {
        private readonly HashSet<string> _classes;

        public EntryTypeDef(string name, IEnumerable<string> classes, EntryTypeDefFilter filter)
        {
            Name = name;
            _classes = new HashSet<string>();
            foreach (string c in classes) _classes.Add(c);
            Filter = filter;
        }

        public string Name { get; }

        public EntryTypeDefFilter Filter { get; }

        public IEnumerator<string> GetTypeClasses() => _classes.AsEnumerable().GetEnumerator();

        public bool IsValidValue(string value) => _classes.Contains(value);

        public bool Test(RantDictionaryEntry entry)
        {
            if (!EntryTypeDefFilter.Test(Filter, entry)) return true;
            return entry.GetClasses().Where(IsValidValue).Count() == 1;
        }
    }
}