using EgsExporter.Exporters;
using EgsExporter.GameData;
using EgsLib;
using EgsLib.ConfigFiles;
using EgsLib.Playfields;
using EgsLib.Playfields.Files;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EgsExporter.Commands
{
    public class ExportTraderSpreadsheetSettings : BaseExportSettings
    {
        [CommandOption("--localization-file", IsHidden = true)]
        [Description("The localization file to use")]
        public string? LocalizationFilePath { get; set; }

        [CommandOption("--trader-file", IsHidden = true)]
        [Description("The trader file to use")]
        public string? TraderFilePath { get; set; }

        [CommandOption("--dialogue-file", IsHidden = true)]
        [Description("The dialogue file to use")]
        public string? DialogueFilePath { get; set; }

        [CommandOption("--blueprint-folder", IsHidden = true)]
        [Description("The blueprint folder to use")]
        public string? BlueprintFolder { get; set; }

        [CommandOption("---playfield-folder", IsHidden = true)]
        [Description("The playfield folder to use")]
        public string? PlayfieldFolder { get; set; }

        public override ValidationResult Validate()
        {
            var b = base.Validate();
            if (!b.Successful)
                return b;

            // Fix up paths
            LocalizationFilePath ??= Path.Combine(ScenarioPath!, @"Extras\Localization.csv");
            if (!File.Exists(LocalizationFilePath))
                return ValidationResult.Error("Localization file does not exist");

            TraderFilePath ??= Path.Combine(ScenarioPath!, @"Content\Configuration\TraderNPCConfig.ecf");
            if (!File.Exists(TraderFilePath))
                return ValidationResult.Error("Trader file does not exist");

            DialogueFilePath ??= Path.Combine(ScenarioPath!, @"Content\Configuration\Dialogues.ecf");
            if (!File.Exists(TraderFilePath))
                return ValidationResult.Error("Dialogue file does not exist");

            BlueprintFolder ??= Path.Combine(ScenarioPath!, @"Prefabs");
            if (!Directory.Exists(BlueprintFolder))
                return ValidationResult.Error("Blueprint folder does not exist");

            PlayfieldFolder ??= Path.Combine(ScenarioPath!, @"Playfields");
            if (!Directory.Exists(BlueprintFolder))
                return ValidationResult.Error("Playfield folder does not exist");

            // Done !
            return ValidationResult.Success();
        }
    }

    internal class ExportTraderSpreadsheet : Command<ExportTraderSpreadsheetSettings>
    {
        public override int Execute(CommandContext context, ExportTraderSpreadsheetSettings settings)
        {
            // Preload all relevant data
            //
            // Localization
            var localization = new Localization(settings.LocalizationFilePath!);

            // Configuration files
            IEnumerable<Dialogue> dialogues;
            IEnumerable<Trader> traders;
            using (new Timer(t => AnsiConsole.WriteLine($"Loaded configuration files in {t.TotalMilliseconds:n0}ms")))
            {
                dialogues = Dialogue.ReadFile(settings.DialogueFilePath);
                traders = Trader.ReadFile(settings.TraderFilePath);
            }

            // Blueprint & Playfield caches
            Dictionary<string, List<BlueprintEntity>> entityNameBlueprintMap;
            using (new Timer(t => AnsiConsole.WriteLine($"Loaded entityName blueprint map in {t.TotalMilliseconds:n0}ms")))
            {
                entityNameBlueprintMap = CreateEntityBlueprintCache(settings.BlueprintFolder!);
            }

            IReadOnlyDictionary<string, IList<Playfield>> groupNamePlayfieldMap;
            using (new Timer(t => AnsiConsole.WriteLine($"Loaded groupName playfield map in {t.TotalMilliseconds:n0}ms")))
            {
                groupNamePlayfieldMap = new ScenarioPlayfields(settings.ScenarioPath!).ReadGroupNamePlayfieldMap();
            }

            // Configure exporter
            //
            var exporter = ConfigureExporter(settings);
            if (exporter == null)
            {
                AnsiConsole.WriteLine($"Error: Unsupported data exporter {settings.ExportType}");
                return 1;
            }

            exporter.SetHeader(["Name", "PoIs", "Playfield", 
                "Trader Sells", "Trader Buys", 
                "Required Items", "Reputation", "Restock Time"]);


            // Well... time to get into it
            foreach (var trader in traders.OrderBy(x => x.Name)) // TODO: Add localization support for OrderBy
            {
                if (trader == null)
                    continue;

                if (!entityNameBlueprintMap.TryGetValue(trader.Name, out List<BlueprintEntity>? entities) || entities == null)
                    entities = [];

                // Load each column entry
                //
                var name = trader.Name;
                if (localization.TryLocalize(name, "English", out string? localizedName))
                    name = $"{localizedName}\n({name})";

                var poi = ParsePointsOfInterest(entities);
                var playfields = ParsePlayfields(entities, groupNamePlayfieldMap);
                var traderSells = ParseTraderSells(trader, localization);
                var traderBuys = ParseTraderBuys(trader, localization);
                var requiredItems = "Not Implemented Yet";
                var reputation = "Not Implemented Yet";
                var restockTime = ParseRestockTime(entities);

                exporter.ExportRow([name, poi, playfields, traderSells, traderBuys, requiredItems, reputation, restockTime]);
            }

            using (new Timer(t => AnsiConsole.WriteLine($"Export to {settings.ExportType} finished in {t.TotalMilliseconds:n0}ms")))
            {
                exporter.Flush();
            }

            return 0;
        }

        private static Dictionary<string, List<BlueprintEntity>> CreateEntityBlueprintCache(string folderName)
        {
            var cache = new Dictionary<string, List<BlueprintEntity>>();
            var blueprints = BlueprintSlim.ReadFolder(folderName);

            foreach (var ent in blueprints.SelectMany(x => x.Entities))
            {
                if (!cache.TryGetValue(ent.Type, out List<BlueprintEntity>? value) || value == null)
                    cache[ent.Type] = [ent];
                else
                    cache[ent.Type].Add(ent);
            }

            return cache;
        }

        private static IDataExporter? ConfigureExporter(ExportTraderSpreadsheetSettings settings)
        {
            if (settings.ExportType != ExportType.Console && !Directory.Exists(settings.OutputPath))
                Directory.CreateDirectory(settings.OutputPath);

            IDataExporter? exporter = settings.ExportType switch
            {
                ExportType.Console => new ConsoleExporter(),
                ExportType.Csv => new CsvExporter(Path.Combine(settings.OutputPath!, "TraderSpreadsheet.csv")),
                _ => null
            };

            return exporter;
        }

        private static string ParsePointsOfInterest(List<BlueprintEntity> entities)
        {
            var sortedPois = entities
                    .DistinctBy(ent => ent.FileName)
                    .OrderBy(ent => ent.DisplayName)
                    .Select(ent =>
                    {
                        var count = entities.Count(x => x.FileName == ent.FileName);

                        var sb = new StringBuilder();
                        sb.Append($"{ent.DisplayName} ({Path.GetFileNameWithoutExtension(ent.FileName)})");

                        if (count > 1)
                            sb.Append($" x{count}");

                        sb.Append($", {ent.Restock} restock");

                        return sb.ToString();
                    });

            return string.Join('\n', sortedPois);
        }

        private static string ParsePlayfields(List<BlueprintEntity> entities, IReadOnlyDictionary<string, IList<Playfield>> groupNamePlayfieldMap)
        {
            // TODO: Add PlanetType to EgsLib
            static string ReadPlanetType(Playfield playfield)
            {
                // Parse playfield_static.yaml
                var pfStatic = playfield.GetPlayfieldFile<PlayfieldStatic>()
                    ?.Contents?.PlanetType;
                if (pfStatic != null)
                {
                    return pfStatic;
                }

                // Parse space_dynamic.yaml
                var spaceDynamic = playfield.GetPlayfieldFile<SpaceDynamic>()
                    ?.Contents?.PlanetType;
                if (spaceDynamic != null)
                {
                    return spaceDynamic;
                }

                // Parse old style playfield.yaml
                var pfObsolete = playfield.GetPlayfieldFile<PlayfieldObsoleteFormat>()
                    ?.Contents?.PlanetType;
                if (pfObsolete != null)
                {
                    return pfObsolete;
                }

                // Default to playfield folder name
                return playfield.Name;
            }

            var sortedPlayfields = entities
                    .Where(ent => ent.GroupName != null)
                    .SelectMany(ent =>
                    {
                        if (ent.GroupName == null
                            || !groupNamePlayfieldMap.TryGetValue(ent.GroupName, out IList<Playfield>? playfields)
                            || playfields == null)
                            playfields = [];

                        return playfields;
                    })
                    .Select(pf => $"{ReadPlanetType(pf)} ({pf.PlayfieldType})")
                    .Distinct()
                    .OrderBy(x => x);

            return  string.Join('\n', sortedPlayfields);
        }

        private static string ParseTraderSells(Trader trader, Localization localization)
        {
            var sellsRaw = trader.Items
                .Where(item => item.SellValue != Range<float>.Default)
                .Where(item => item.SellAmount != Range<int>.Default)
                .Select(item =>
                {
                    var name = item.Name;
                    if (localization.TryLocalize(name, "English", out string? localizedName))
                        name = localizedName;

                    var sb = new StringBuilder();
                    sb.Append($"{name}: ");
                    if (item.SellMarketFactor)
                        sb.Append("mf=");

                    sb.Append($"{item.SellValue}, ");
                    sb.Append(item.SellAmount.ToString());

                    return sb.ToString();
                });

            return string.Join('\n', sellsRaw);
        }

        private static string ParseTraderBuys(Trader trader, Localization localization)
        {
            var sellsRaw = trader.Items
                .Where(item => item.BuyValue != Range<float>.Default)
                .Where(item => item.BuyAmount != Range<int>.Default)
                .Select(item =>
                {
                    var name = item.Name;
                    if (localization.TryLocalize(name, "English", out string? localizedName))
                        name = localizedName;

                    var sb = new StringBuilder();
                    sb.Append($"{name}: ");
                    if (item.BuyMarketFactor)
                        sb.Append("mf=");

                    sb.Append($"{item.BuyValue}, ");
                    sb.Append(item.BuyAmount.ToString());

                    return sb.ToString();
                });

            return string.Join('\n', sellsRaw);
        }

        private static string ParseRestockTime(List<BlueprintEntity> entities)
        {
            var min = entities.Min(bp => bp.Restock) ?? 0;
            var max = entities.Max(bp => bp.Restock) ?? 0;
            var avg = (int)(entities.Average(bp => bp.Restock) ?? 0);

            return $"Avg: {avg}\nMin: {min}\nMax: {max}";
        }
    }
}
