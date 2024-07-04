using EgsExporter.Exporters;
using EgsExporter.GameData;
using EgsLib;
using EgsLib.ConfigFiles;
using EgsLib.ConfigFiles.Ecf;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EgsExporter.Commands
{
    public class ExportPrefabLootSettings : BaseExportSettings
    {
        [CommandOption("--localization-file", IsHidden = true)]
        [Description("The localization file to use")]
        public string? LocalizationFilePath { get; set; }

        [CommandOption("--container-file", IsHidden = true)]
        [Description("The container file to use")]
        public string? ContainerFilePath { get; set; }

        [CommandOption("--loot-group-file", IsHidden = true)]
        [Description("The loot group file to use")]
        public string? LootGroupFilePath { get; set; }

        [CommandOption("--blueprint-folder", IsHidden = true)]
        [Description("The blueprint folder to use")]
        public string? BlueprintFolder { get; set; }

        public override ValidationResult Validate()
        {
            var b = base.Validate();
            if (!b.Successful)
                return b;

            // Fix up paths
            LocalizationFilePath ??= Path.Combine(ScenarioPath!, @"Extras\Localization.csv");
            if (!File.Exists(LocalizationFilePath))
                return ValidationResult.Error("Localization file does not exist");

            ContainerFilePath ??= Path.Combine(ScenarioPath!, @"Content\Configuration\Containers.ecf");
            if (!File.Exists(ContainerFilePath))
                return ValidationResult.Error("Trader file does not exist");

            LootGroupFilePath ??= Path.Combine(ScenarioPath!, @"Content\Configuration\LootGroups.ecf");
            if (!File.Exists(LootGroupFilePath))
                return ValidationResult.Error("Dialogue file does not exist");

            BlueprintFolder ??= Path.Combine(ScenarioPath!, @"Prefabs");
            if (!Directory.Exists(BlueprintFolder))
                return ValidationResult.Error("Blueprint folder does not exist");

            // Done !
            return ValidationResult.Success();
        }
    }


    internal class ExportPrefabLoot : Command<ExportPrefabLootSettings>
    {
        public override int Execute(CommandContext context, ExportPrefabLootSettings settings)
        {
            try
            {
                var data = new PrefabLootData(settings);
                data.Export();
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteLine($"Error: {ex.Message}");
                return 1;
            }

            return 0;
        }

        private class PrefabLootData
        {
            private readonly ExportPrefabLootSettings _settings;
            private readonly IDataExporter _exporter;

            private readonly Localization _localization;
            private readonly List<LootGroup> _lootGroups;
            private readonly List<BlueprintLoot> _blueprintLootList;
            private readonly ContainerCache _containers;

            public PrefabLootData(ExportPrefabLootSettings settings)
            {
                _settings = settings;
                _exporter = settings.CreateExporter("PrefabLoot")
                    ?? throw new Exception($"Unsupported data exporter {settings.ExportType}");

                // Preload all relevant data
                //
                // Localization
                using (new Timer(t => AnsiConsole.WriteLine($"Loaded localization in {t.TotalMilliseconds:n0}ms")))
                    _localization = new Localization(settings.LocalizationFilePath!);

                // Configuration files
                using (new Timer(t => AnsiConsole.WriteLine($"Loaded configuration files in {t.TotalMilliseconds:n0}ms")))
                {
                    _lootGroups = LootGroup.ReadFile(settings.LootGroupFilePath).ToList();
                }

                // Blueprint and config caches
                using (new Timer(t => AnsiConsole.WriteLine($"Loaded BlueprintLootList in {t.TotalMilliseconds:n0}ms")))
                    _blueprintLootList = BlueprintSlim.CreateBlueprintLootList(_settings.BlueprintFolder!);

                using (new Timer(t => AnsiConsole.WriteLine($"Loaded ContainerCache in {t.TotalMicroseconds:n0}ms")))
                    _containers = new ContainerCache(settings.ContainerFilePath!);

                // Force CG after all the cache loading
                /*
                long collected = 0;
                using (new Timer(t => AnsiConsole.WriteLine($"Forced GC - collected {collected:n0}mb in {t.TotalMicroseconds:n0}ms")))
                {
                    var before = GC.GetTotalMemory(false);
                    GC.Collect();
                    var after = GC.GetTotalMemory(false);

                    collected = (before - after) / 1048576;
                }
                */
            }

            public void Export()
            {
                _exporter.SetHeader(["Blueprint", "Container", "Items", "Groups"]);

                var blueprintLoots = _blueprintLootList.OrderBy(x => x.DisplayName).ThenBy(x => x.FileName);
                foreach (var bpLoot in blueprintLoots)
                {
                    var name = $"{bpLoot.DisplayName}{Environment.NewLine}({Path.GetFileNameWithoutExtension(bpLoot.FileName)})";

                    foreach (var lootContainer in bpLoot.LootContainers)
                    {
                        if (!_containers.TryFindById(lootContainer.ContainerId, out var containerDetails))
                            continue;
                        
                        var sb = new StringBuilder();
                        sb.AppendLine($"Id: {lootContainer.ContainerId}");
                        sb.AppendLine($"Pos: {lootContainer.Location}");
                        sb.Append($"Rolls: {containerDetails.Count}");

                        var container = $"{lootContainer.ContainerId} @ {lootContainer.Location}{Environment.NewLine}Count: {containerDetails.Count}";

                        var items = ParseContainerItems(containerDetails.Items, ContainerItemType.Item);
                        var groups = ParseContainerItems(containerDetails.Items, ContainerItemType.Group);

                        _exporter.ExportRow([name, sb.ToString(), items, groups]);
                    }
                }

                _exporter.Flush();
            }

            private string ParseContainerItems(ContainerItem[] items, ContainerItemType type)
            {
                var maxWeight = items.Sum(x => x.Probability);

                var sb = new StringBuilder();
                foreach (var item in items.Where(x => x.Type == type))
                {
                    // TODO: Different language support
                    var name = type == ContainerItemType.Item ? _localization.Localize(item.Name, "English") : item.Name;
                    var realProbability = item.Probability / maxWeight * 100;

                    sb.Append($"{item.Probability,4:f3} ({realProbability,3:f2}%) {name}");

                    if (item.Count != null)
                    {
                        var count = item.Count.Trim('"').Replace(",", " - ");

                        sb.Append($" x ({count})");
                    }

                    sb.AppendLine();
                }

                return sb.ToString().TrimEnd('\r', '\n');
            }
        }
    }
}
