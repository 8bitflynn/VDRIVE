using System.Text;

namespace VDRIVE.Util
{
    // A local copy of the HVSC (its C64Music folder), indexed by each SID's own header: title, author, released -
    // the same three fields hvsc.c64.org's search API returns. @HVSC searches look here first and only ask the
    // web when nothing local matches (a tune newer than the copy); a web result that is in the copy loads from it.
    // Built once, in the background, on first use; until it is ready everything goes to the web as before.
    public static class HvscIndex
    {
        public class Entry
        {
            public string Path;
            public string Title;
            public string Author;
            public string Released;
            public string[] Words;      // title + author + released, upper case, split into words: what a search matches
        }

        // the index is saved next to VDRIVE: reading 55,000 headers took 5 minutes from a USB drive. A new LocalPath
        // (a new HVSC release in its own folder) builds it again; only an update in place needs the file deleted
        // (until then the new tunes still come, from the web).
        private static string CacheFile => Path.Combine(AppContext.BaseDirectory, "hvsc-index.txt");

        private static readonly object Gate = new object();
        private static Task<List<Entry>> Building;
        private static string BuiltFor;
        private static Dictionary<string, string> ByKey;    // Key(title, author, released) -> path

        // starts the index for this folder if it isn't there yet; null = not ready (or no folder)
        public static List<Entry> Get(string root, VDRIVE_Contracts.Interfaces.ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
            lock (Gate)
            {
                if (Building == null || BuiltFor != root)
                {
                    BuiltFor = root;
                    Building = Task.Run(() => Build(root, logger));
                }
                return Building.IsCompletedSuccessfully ? Building.Result : null;
            }
        }

        public static string Key(string title, string author, string released)
        {
            return (title ?? "").Trim().ToUpperInvariant() + "\u0001" + (author ?? "").Trim().ToUpperInvariant() + "\u0001" + (released ?? "").Trim().ToUpperInvariant();
        }

        // the local file for a web result, or null
        public static string Find(string root, string title, string author, string released, VDRIVE_Contracts.Interfaces.ILogger logger)
        {
            if (Get(root, logger) == null) return null;
            return ByKey != null && ByKey.TryGetValue(Key(title, author, released), out string path) ? path : null;
        }

        // every word of the term starts a word of the title, author or released (as the web API matches: "IN" doesn't
        // match "Theme"; a start, so a card's term cut off mid-word still matches). "-" also splits words, so a card's
        // "TITLE - COMPOSER" finds its tune here (the web API returns nothing for a "-")
        public static List<Entry> Search(List<Entry> index, string term)
        {
            string[] words = Words(term);
            if (words.Length == 0) return new List<Entry>();
            return index.Where(e => words.All(w => e.Words.Any(x => x.StartsWith(w, StringComparison.Ordinal)))).ToList();
        }

        private static string[] Words(string text)
        {
            return System.Text.RegularExpressions.Regex.Split(text.ToUpperInvariant(), @"[^\p{L}\p{N}]+").Where(w => w.Length > 0).ToArray();
        }

        private static List<Entry> Build(string root, VDRIVE_Contracts.Interfaces.ILogger logger)
        {
            var started = DateTime.Now;
            var list = new List<Entry>();
            var byKey = new Dictionary<string, string>();
            List<Entry> cached = Load(root);
            if (cached != null)
            {
                foreach (Entry e in cached) byKey[Key(e.Title, e.Author, e.Released)] = e.Path;
                ByKey = byKey;
                logger?.LogMessage($"HVSC: local index of {cached.Count} tunes from {CacheFile} in {(DateTime.Now - started).TotalSeconds:0.0}s");
                return cached;
            }
            Encoding latin1 = Encoding.Latin1;
            byte[] header = new byte[0x76];
            foreach (string path in Directory.EnumerateFiles(root, "*.sid", SearchOption.AllDirectories))
            {
                try
                {
                    using (FileStream fs = File.OpenRead(path))
                    {
                        if (fs.Read(header, 0, header.Length) < header.Length) continue;
                    }
                    if (!(header[1] == 'S' && header[2] == 'I' && header[3] == 'D' && (header[0] == 'P' || header[0] == 'R'))) continue;
                    Entry e = new Entry
                    {
                        Path = path,
                        Title = Field(header, 0x16, latin1),
                        Author = Field(header, 0x36, latin1),
                        Released = Field(header, 0x56, latin1)
                    };
                    e.Words = Words(e.Title + " " + e.Author + " " + e.Released);
                    list.Add(e);
                    byKey[Key(e.Title, e.Author, e.Released)] = path;
                }
                catch (Exception) { }   // an unreadable file is just not in the index
            }
            list.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            ByKey = byKey;
            logger?.LogMessage($"HVSC: local index of {list.Count} tunes from {root} in {(DateTime.Now - started).TotalSeconds:0.0}s");
            try { Save(root, list); } catch (Exception ex) { logger?.LogMessage($"HVSC: couldn't save the index to {CacheFile}: {ex.Message}"); }
            return list;
        }

        // the cache: first line the folder it was made from, then path, title, author, released per line (tab-separated)
        private static void Save(string root, List<Entry> list)
        {
            using (StreamWriter w = new StreamWriter(CacheFile, false, new UTF8Encoding(false)))
            {
                w.WriteLine(root);
                foreach (Entry e in list) w.WriteLine(e.Path + "\t" + e.Title + "\t" + e.Author + "\t" + e.Released);
            }
        }

        private static List<Entry> Load(string root)
        {
            try
            {
                if (!File.Exists(CacheFile)) return null;
                string[] lines = File.ReadAllLines(CacheFile, Encoding.UTF8);
                if (lines.Length < 2 || lines[0] != root) return null;
                var list = new List<Entry>(lines.Length);
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] f = lines[i].Split('\t');
                    if (f.Length != 4) continue;
                    list.Add(new Entry { Path = f[0], Title = f[1], Author = f[2], Released = f[3], Words = Words(f[1] + " " + f[2] + " " + f[3]) });
                }
                return list;
            }
            catch (Exception) { return null; }
        }

        private static string Field(byte[] header, int offset, Encoding latin1)
        {
            int n = 0;
            while (n < 32 && header[offset + n] != 0) n++;
            return latin1.GetString(header, offset, n).Trim();
        }
    }
}
