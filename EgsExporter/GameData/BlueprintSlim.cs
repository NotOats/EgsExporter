using EgsLib;
using EgsLib.Blueprints;
using EgsLib.Blueprints.NbtTags;
using Sylvan.Data.Csv;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EgsExporter.GameData
{
    internal record class BlueprintEntity(
        string FileName, string? BlueprintDisplayName, string? GroupName, 
        string Name, string Type, int? Pay, int? Restock, string? Dialog)
    {
        public override string ToString()
        {
            return DisplayName;
        }

        public string DisplayName => BlueprintDisplayName ?? Path.GetFileNameWithoutExtension(FileName);
    }

    internal readonly record struct LootContainer(Vector3<int> Location, int ContainerId);

    internal record class BlueprintLoot(
        string FileName, string? BlueprintDisplayName, string? GroupName, 
        IReadOnlyList<LootContainer> LootContainers)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(BlueprintDisplayName) 
            ? Path.GetFileNameWithoutExtension(FileName) : BlueprintDisplayName;
    }

    /// <summary>
    /// Lightweight version of Blueprint only holding what we need (skip most block data)
    /// </summary>
    internal class BlueprintSlim
    {
        public string FilePath { get; }
        public string? DisplayName { get; } = null;
        public string? GroupName { get; } = null;

        public ReadOnlyCollection<BlueprintEntity> Entities { get; }
        public ReadOnlyCollection<LootContainer>? LootContainers { get; }

        public BlueprintSlim(string file)
        {
            if (string.IsNullOrWhiteSpace(file))
                throw new ArgumentNullException(nameof(file), "blueprint file is null or white space");
            if (!File.Exists(file))
                throw new FileNotFoundException("blueprint file does not exist", file);

            FilePath = file;

            var bp = new Blueprint(file);

            if (bp.Header.GetProperty<string>(PropertyName.DisplayName, out var displayName))
                DisplayName = displayName;

            if (bp.Header.GetProperty<string>(PropertyName.GroupName, out var groupName))
                GroupName = groupName;

            Entities = ParseEntities(bp, DisplayName, GroupName);
            LootContainers = ParseLoot(bp, DisplayName, GroupName);
        }

        private static ReadOnlyCollection<BlueprintEntity> ParseEntities(Blueprint bp, string? displayName, string? groupName)
        {
            var entities = new List<BlueprintEntity>();
            if (bp.BlockData == null)
                return entities.AsReadOnly();

            foreach (var kvp in bp.BlockData.Entities)
            {
                var tags = kvp.Value;

                // Skip if we don't have Ent/Ents
                if (tags.All(x => x.Name != "Ent" && x.Name != "Ents"))
                    continue;

                // Require Type tag
                if (tags.FirstOrDefault(x => x.Name == "Type") is not NbtString type)
                    continue;

                var pay = tags.FirstOrDefault(t => t.Name == "Pay") as NbtInt32;
                var restock = tags.FirstOrDefault(t => t.Name == "Restock") as NbtInt32;
                var dialog = tags.FirstOrDefault(t => t.Name == "Dlg") as NbtString;

                // Loop through possible multiple names from "Ents" entries
                foreach (var name in ReadEntityTag(tags))
                {
                    entities.Add(new BlueprintEntity(bp.FileName, 
                        displayName, groupName, name, 
                        type.Value, pay?.Value, restock?.Value, dialog?.Value));
                }
            }

            return entities.AsReadOnly();
        }

        private static IEnumerable<string> ReadEntityTag(NbtList list)
        {
            // Handle single Ent
            var ent = list.FirstOrDefault(tag => tag.Name == "Ent");
            if (ent != null && ent is NbtString entity)
                yield return entity.Value;

            // Parse out Ents possible commas
            var ents = list.FirstOrDefault(x => x.Name == "Ents");
            if (ents != null && ents is NbtString entities)
            {
                foreach (var name in entities.Value.Split(','))
                {
                    yield return name;
                }
            }
        }

        private static ReadOnlyCollection<LootContainer> ParseLoot(Blueprint bp, string? displayName, string? groupName)
        {
            if (bp.BlockData == null)
                return Array.Empty<LootContainer>().AsReadOnly();

            var lootContainers = new List<LootContainer>();
            foreach (var kvp in bp.BlockData.Entities)
            {
                var location = kvp.Key;
                var tags = kvp.Value;

                if (tags.FirstOrDefault(x => x.Name == "LootId") is not NbtInt32 tag)
                    continue;

                lootContainers.Add(new LootContainer(location, tag.Value));
            }

            return lootContainers.AsReadOnly();
        }

        private static BlueprintSlim? CreateBlueprintSlim(string file)
        {
            try
            {
                return new BlueprintSlim(file);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Dict of EntityName:BlueprintEntity
        /// </summary>
        /// <param name="folderName"></param>
        /// <returns></returns>
        public static Dictionary<string, List<BlueprintEntity>> CreateEntityBlueprintCache(string folderName)
        {
            var blueprints = Directory.EnumerateFiles(folderName, "*.epb")
                .AsParallel()
                .Select(CreateBlueprintSlim)
                .Where(x => x != null)
                .Cast<BlueprintSlim>();

            var cache = new Dictionary<string, List<BlueprintEntity>>();
            foreach (var ent in blueprints.SelectMany(x => x.Entities))
            {
                if (!cache.TryGetValue(ent.Type, out List<BlueprintEntity>? value) || value == null)
                    cache[ent.Type] = [ent];
                else
                    cache[ent.Type].Add(ent);
            }

            return cache;
        }

        /// <summary>
        /// Dict of BlueprintName:BlueprintLoot
        /// </summary>
        /// <param name="folderName"></param>
        /// <returns></returns>
        public static List<BlueprintLoot> CreateBlueprintLootList(string folderName)
        {
            var blueprints = Directory.EnumerateFiles(folderName, "*.epb")
                .AsParallel()
                .Select(CreateBlueprintSlim)
                .Where(x => x != null)
                .Cast<BlueprintSlim>();

            var cache = new List<BlueprintLoot>();
            foreach (var bp in blueprints)
            {
                if (bp.LootContainers == null)
                    continue;

                var loot = new BlueprintLoot(Path.GetFileNameWithoutExtension(bp.FilePath), 
                    bp.DisplayName, bp.GroupName, bp.LootContainers);

                cache.Add(loot);
            }

            return cache;
        }
    }
}
