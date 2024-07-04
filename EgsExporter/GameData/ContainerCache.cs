using EgsLib;
using EgsLib.ConfigFiles.Ecf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EgsExporter.GameData
{
    internal enum ContainerItemType : byte
    {
        Item,
        Group
    }

    internal readonly record struct ContainerItem(string Name, ContainerItemType Type, float Probability, string? Count);
    internal readonly record struct Container(int Id, Range<int> Count, ContainerItem[] Items);

    internal class ContainerCache
    {
        public IReadOnlyList<Container> Containers { get; }

        public ContainerCache(string containerFile)
        {
            var ecf = new EcfFile(containerFile);
            var objects = ecf.ParseObjects();

            if (objects != null)
                Containers = ParseContainers(objects).OrderBy(x => x.Id).ToList();
            else
                Containers = [];
        }

        public bool TryFindById(int id, out Container container)
        {
            foreach (var cont in Containers)
            {
                if (id != cont.Id)
                    continue;

                container = cont;
                return true;
            }

            container = default;
            return false;
        }

        // TODO: Move Container into EgsLib where it belongs
        private static IEnumerable<Container> ParseContainers(IEnumerable<IEcfObject> objects)
        {
            static Range<int> ParseCount(string count)
            {
                var parts = count.Trim('"').Split(',');
                // Minimum needed
                if (parts.Length < 1)
                    throw new Exception("failed to parse range: has less than two parts");

                if (!int.TryParse(parts[0], out int min))
                    throw new Exception("failed to parse range: min is not a int");

                // Parse max if available
                var max = min;
                if (parts.Length >= 2 && !int.TryParse(parts[1], out max))
                    throw new Exception("failed to parse range: max is not a int");

                return new Range<int>(min, max);
            }

            static KeyValuePair<string, List<string>> ParseParams(string value)
            {
                // TODO: regex...
                var parts = value.Split(", ");
                if (parts.Length < 1)
                    throw new Exception("failed to parse params: has less than one parts");

                var p = new List<string>();
                for (var i = 1; i < parts.Length; i++)
                {
                    var index = parts[i].IndexOf(':');
                    var str = parts[i].Substring(index + 1).Trim();
                    p.Add(str);
                }

                return new KeyValuePair<string, List<string>>(parts[0], p);
            }

            static ContainerItem[] ParseItems(IEcfChild child)
            {
                var i = 0;
                var max = child.Properties.Count;
                var result = new ContainerItem[max];

                foreach(var kvp in child.Properties)
                {
                    if (i >= max)
                        throw new Exception("child.Properties is longer than it's count");

                    // Read type
                    ContainerItemType type;
                    if (kvp.Key.StartsWith("Name_"))
                        type = ContainerItemType.Item;
                    else if (kvp.Key.StartsWith("Group_"))
                        type = ContainerItemType.Group;
                    else
                        throw new Exception("child.Properties contains an invalid key");

                    // Read parameters
                    var details = ParseParams(kvp.Value);
                    var name = details.Key;
                    var parameters = details.Value;

                    var probability = 0f;
                    if (parameters.Count >= 1 && !float.TryParse(parameters[0], out probability))
                        throw new Exception("failed to parse item probability");

                    var count = parameters.Count >= 2 ? parameters[1] : null;

                    result[i] = new ContainerItem(name, type, probability, count);

                    i++;
                }

                return result;
            }

            foreach (var obj in objects)
            {
                if (!obj.ReadField<int>("Id", out int id))
                    throw new Exception("container is missing field Id");
                
                if (!obj.ReadProperty("Count", out string count))
                    throw new Exception("container is missing field Count");

                var itemsEcf = obj.Children.FirstOrDefault(x => x.Name == "Items") 
                    ?? throw new Exception("container is missing items child");

                var items = ParseItems(itemsEcf);

                yield return new Container(id, ParseCount(count), items);
            }
        }
    }
}
