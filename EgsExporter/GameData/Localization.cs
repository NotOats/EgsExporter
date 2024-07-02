using Sylvan.Data.Csv;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EgsExporter.GameData
{
    public class Localization
    {
        private readonly string _file;

        /// <summary>
        /// List of supported languages.
        /// Note: Not all languages have full support.
        /// </summary>
        public IReadOnlyList<string> Languages =>
            LocalizationData.FirstOrDefault().Value?.Select(x => x.Key)?.ToArray() ?? Array.Empty<string>();

        /// <summary>
        /// Raw localization data, the outer dictionary is a map of translation keys used in game to the inner dictionary.
        /// The inner dictionary is a map of language to the appropriate translation.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LocalizationData { get; private set; }
            = new Dictionary<string, IReadOnlyDictionary<string, string>>();

        /// <summary>
        /// Parses a Localization.csv typically found in the "[scenario]\Extras" folder
        /// </summary>
        /// <param name="localizationFile">the localization file to parse</param>
        /// <exception cref="ArgumentNullException">thrown if <paramref name="localizationFile"/> is null or white space</exception>
        /// <exception cref="FileNotFoundException">thrown if <paramref name="localizationFile"/> does not exist</exception>
        public Localization(string localizationFile)
        {
            if (string.IsNullOrWhiteSpace(localizationFile))
                throw new ArgumentNullException(nameof(localizationFile), "localizationFile is null or white space");

            if (!File.Exists(localizationFile))
                throw new FileNotFoundException("localizationFile does not exist", localizationFile);

            _file = localizationFile;

#if DEBUG
            var sw = Stopwatch.StartNew();
#endif

            Reload();

#if DEBUG
            sw.Stop();

            // TODO: Add better logging somewhere
            Console.WriteLine($"Localization: Loaded {LocalizationData?.Count ?? 0} entries with {Languages.Count} languages in {sw.ElapsedMilliseconds}ms");
#endif
        }

        /// <summary>
        /// Localizes a given key to the specified language.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="language"></param>
        /// <returns>returns in order of existance: specified language, key, null</returns>
        public string? Localize(string key, string language)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            if (!LocalizationData.TryGetValue(key, out IReadOnlyDictionary<string, string>? map) || map == null)
                return null;

            if (map.TryGetValue(language, out string? text))
                return text;

            return key;
        }

        public bool TryLocalize(string key, string language, out string? value)
        {
            value = Localize(key, language);
            return value != null;
        }

        /// <summary>
        /// Reloads the localization data from the originally specified file
        /// </summary>
        public void Reload()
        {
            var contents = ReadFile(_file);
            LocalizationData = ParseData(contents);
        }

        private static string ReadFile(string file)
        {
            if (!File.Exists(file))
                throw new FileNotFoundException("Failed to find localization file", file);

            // Read & replace formats ([c], [rrggbb], etc)
            var contents = File.ReadAllText(file);
            return Regex.Replace(contents, @"\[.*?\]", "");
        }

        private static Dictionary<string, IReadOnlyDictionary<string, string>> ParseData(string contents)
        {
            var result = new Dictionary<string, IReadOnlyDictionary<string, string>>();

            var options = new CsvDataReaderOptions
            {
                ColumnStringFactory = new StringPool(64).GetString,
            };

            using var reader = new StringReader(contents);
            using var csv = CsvDataReader.Create(reader, options);
            
            while(csv.Read())
            {
                var key = csv.GetString(0);
                var map = new Dictionary<string, string>();

                for (var i = 0; i < csv.FieldCount; i++)
                {
                    var language = csv.GetName(i);
                    var value = csv.GetString(i);

                    map[language] = !string.IsNullOrEmpty(value) ? value : key;
                }

                result[key] = map;
            }

            return result;
        }
    }
}
