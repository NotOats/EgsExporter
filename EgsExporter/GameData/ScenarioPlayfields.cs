using EgsLib.Playfields;
using EgsLib.Playfields.Files;

namespace EgsExporter.GameData
{
    internal class ScenarioPlayfields
    {
        public string Folder { get; }

        public IEnumerable<Playfield> Playfields => ReadPlayfields(Folder);

        public ScenarioPlayfields(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentNullException(nameof(folder), "folder is null or white space");

            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException("scenario playfield folder does not exist");

            Folder = folder;
        }

        public IReadOnlyDictionary<string, IList<Playfield>> ReadGroupNamePlayfieldMap()
        {
            static Dictionary<string, IList<Playfield>> MapGroupNames<TPoiType>(
                Dictionary<string, IList<Playfield>> seed,
                Playfield playfield,
                IEnumerable<TPoiType> pois,
                Func<TPoiType, string> getGroupName)
            {
                return pois.Aggregate(seed, (total, next) =>
                {
                    var groupName = getGroupName(next);
                    if (string.IsNullOrWhiteSpace(groupName))
                        return total;

                    if (!total.TryGetValue(groupName, out IList<Playfield>? list) || list == null)
                        total[groupName] = [playfield];
                    else
                        total[groupName].Add(playfield);

                    return total;
                }).ToDictionary(x => x.Key, x => x.Value);
            }

            var result = new Dictionary<string, IList<Playfield>>();

            foreach (var playfield in Playfields)
            {
                // Parse playfield_static.yaml
                var pfStatic = playfield.GetPlayfieldFile<PlayfieldStatic>()
                    ?.Contents?.PointsOfInterest?.Random;
                if (pfStatic != null)
                {
                    result = MapGroupNames(result, playfield, pfStatic, poi => poi.GroupName);
                }

                // Parse space_dynamic.yaml
                var spaceDynamic = playfield.GetPlayfieldFile<SpaceDynamic>()
                    ?.Contents?.PointsOfInterest;
                if (spaceDynamic != null)
                {
                    result = MapGroupNames(result, playfield, spaceDynamic, poi => poi.GroupName);
                }

                // Parse old style playfield.yaml
                var pfObsolete = playfield.GetPlayfieldFile<PlayfieldObsoleteFormat>()
                    ?.Contents?.PointsOfInterest?.Random;
                if (pfObsolete != null)
                {
                    result = MapGroupNames(result, playfield, pfObsolete, poi => poi.GroupName);
                }
            }

            return result;
        }

        private static IEnumerable<Playfield> ReadPlayfields(string playfieldsFolder)
        {
            static Playfield? CreatePlayfield(string directory)
            {
                try
                {
                    return new Playfield(directory);
                }
                catch (Exception)
                {
                    // TODO: Error handling
                    //Console.WriteLine($"Failed to load playfield file: {file}");
                    return null;
                }
            }

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 3,
            };

            return Directory.EnumerateDirectories(playfieldsFolder, "*", options)
                .AsParallel()
                .Select(CreatePlayfield)
                .Where(pf => pf != null)
                .Cast<Playfield>();
        }
    }
}
