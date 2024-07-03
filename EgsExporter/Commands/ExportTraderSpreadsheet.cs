using EgsExporter.Exporters;
using EgsExporter.GameData;
using EgsLib;
using EgsLib.ConfigFiles;
using EgsLib.Playfields;
using EgsLib.Playfields.Files;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text;

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
            try
            {
                var spreadsheetData = new TradeSpreadsheetData(settings);
                spreadsheetData.Export();
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteLine($"Error: {ex.Message}");
                return 1;
            }

            return 0;
        }

        private class TradeSpreadsheetData
        {
            private readonly ExportTraderSpreadsheetSettings _settings;
            private readonly IDataExporter _exporter;

            // Spreadsheet raw data
            private readonly Localization _localization;
            private readonly List<Trader> _traders;
            private readonly List<Dialogue> _dialogues;
            private readonly DialogueCache _dialogueCache;
            private readonly Dictionary<string, List<BlueprintEntity>> _entityNameBlueprintMap;
            private readonly IReadOnlyDictionary<string, IList<Playfield>> _groupNamePlayfieldMap;

            public TradeSpreadsheetData(ExportTraderSpreadsheetSettings settings)
            {
                _settings = settings;

                // Configure exporter
                _exporter = settings.CreateExporter("TraderSpreadsheet")
                    ?? throw new Exception($"Unsupported data exporter {settings.ExportType}");

                // Preload all relevant data
                //
                // Localization
                using (new Timer(t => AnsiConsole.WriteLine($"Loaded localization in {t.TotalMilliseconds:n0}ms")))
                    _localization = new Localization(settings.LocalizationFilePath!);

                // Configuration files
                using (new Timer(t => AnsiConsole.WriteLine($"Loaded configuration files in {t.TotalMilliseconds:n0}ms")))
                {
                    _traders = Trader.ReadFile(settings.TraderFilePath).ToList();
                    _dialogues = Dialogue.ReadFile(settings.DialogueFilePath).ToList();
                    _dialogueCache = new DialogueCache(_dialogues);
                }

                // Blueprint & Playfield caches
                using (new Timer(t => AnsiConsole.WriteLine($"Loaded entityName blueprint map in {t.TotalMilliseconds:n0}ms")))
                    _entityNameBlueprintMap = BlueprintSlim.CreateEntityBlueprintCache(settings.BlueprintFolder!);

                using (new Timer(t => AnsiConsole.WriteLine($"Loaded groupName playfield map in {t.TotalMilliseconds:n0}ms")))
                    _groupNamePlayfieldMap = new ScenarioPlayfields(settings.ScenarioPath!).ReadGroupNamePlayfieldMap();

            }

            public void Export()
            {
                _exporter.SetHeader(["Name", "PoIs", "Playfield",
                    "Trader Sells", "Trader Buys",
                    "Required Items", "Reputation", "Restock Time"]);

                // Core list is every trader
                foreach (var trader in _traders.OrderBy(x => x.Name)) // TODO: Add localization support for OrderBy
                {
                    if (trader == null)
                        continue;

                    // Find which blueprints they're attached to
                    if (!_entityNameBlueprintMap.TryGetValue(trader.Name, out List<BlueprintEntity>? entities) || entities == null)
                        entities = [];

                    // Load each column entry
                    /*
                    var name = trader.Name;
                    if (_localization.TryLocalize(name, "English", out string? localizedName))
                        name = $"{localizedName}\n({name})";
                    */

                    var name = ParseName(trader, entities);
                    var poi = ParsePointsOfInterest(entities);
                    var playfields = ParsePlayfields(entities);
                    var traderSells = ParseTraderSells(trader);
                    var traderBuys = ParseTraderBuys(trader);
                    var requiredItems = ParseRequiredItems(entities);
                    var reputation = ParseReputation(entities);
                    var restockTime = ParseRestockTime(entities);

                    // Export
                    _exporter.ExportRow([name, poi, playfields, traderSells, traderBuys, requiredItems, reputation, restockTime]);
                }

                using (new Timer(t => AnsiConsole.WriteLine($"Export to {_settings.ExportType} finished in {t.TotalMilliseconds:n0}ms")))
                    _exporter.Flush();
            }

            #region Column data parsers
            private string ParseName(Trader trader, List<BlueprintEntity> entities)
            {
                var displayName = trader.Name;
                if (_localization.TryLocalize(trader.Name, "English", out string? localizedName))
                    displayName = localizedName;

                var sb = new StringBuilder();
                sb.AppendLine(displayName);
                sb.AppendLine($"+ Key: {trader.Name}");
                
                foreach(var entity in entities
                    .Where(x => x != null && !string.IsNullOrEmpty(x.Dialog))
                    .DistinctBy(x => x.Dialog))
                {
                    sb.AppendLine($"+ Dialogue: {entity.Dialog}");
                }

                return sb.ToString();
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

            private string ParsePlayfields(List<BlueprintEntity> entities)
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
                                || !_groupNamePlayfieldMap.TryGetValue(ent.GroupName, out IList<Playfield>? playfields)
                                || playfields == null)
                                playfields = [];

                            return playfields;
                        })
                        .Select(pf => $"{ReadPlanetType(pf)} ({pf.PlayfieldType})")
                        .Distinct()
                        .OrderBy(x => x);

                return string.Join('\n', sortedPlayfields);
            }

            private string ParseTraderSells(Trader trader)
            {
                var sellsRaw = trader.Items
                    .Where(item => item.SellValue != Range<float>.Default)
                    .Where(item => item.SellAmount != Range<int>.Default)
                    .Select(item =>
                    {
                        var name = item.Name;
                        if (_localization.TryLocalize(name, "English", out string? localizedName))
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

            private string ParseTraderBuys(Trader trader)
            {
                var sellsRaw = trader.Items
                    .Where(item => item.BuyValue != Range<float>.Default)
                    .Where(item => item.BuyAmount != Range<int>.Default)
                    .Select(item =>
                    {
                        var name = item.Name;
                        if (_localization.TryLocalize(name, "English", out string? localizedName))
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

            private string ParseRequiredItems(List<BlueprintEntity> entities)
            {
                var dialogues = entities
                    .Where(e => e.Dialog != null && e.Dialog.StartsWith("TD_") && e.Dialog.EndsWith("Start"))
                    .DistinctBy(e => e.Dialog)
                    .Select(e => _dialogueCache.RequiredItems(e.Dialog!))
                    .Where(x => x != null)
                    .SelectMany(x => x!.ToList())
                    .OrderBy(x => x.Key);

                var formatted = dialogues.Select(kvp =>
                {
                    var value = kvp.Value.Trim('"', '\r', '\n');
                    return CreateItemRequirementString(kvp.Key, kvp.Value);
                });

                return string.Join($"----------------------------------------{Environment.NewLine}", formatted);
            }

            private static string CreateItemRequirementString(string key, string requirements)
            {
                static string SplitString(string value, string delimiter, string replacement)
                {
                    var sb = new StringBuilder();
                    var split = value.Split(delimiter);
                    for (int i = 0; i < split.Length; i++)
                    {
                        if (i == 0)
                            sb.AppendLine($"  + {split[i]}");
                        else
                            sb.AppendLine($"  {replacement} {split[i]}");
                    }

                    return sb.ToString();
                }

                var sb = new StringBuilder();
                sb.AppendLine($"{key}");

                // Split these into multiple lines
                if (requirements.Contains(" && "))
                {
                    var formatted = SplitString(requirements, " && ", "&");
                    sb.Append(formatted);
                }
                else if(requirements.Contains(" || "))
                {
                    var formatted = SplitString(requirements, " || ", "||");
                    sb.Append(formatted);
                }
                else
                {
                    sb.AppendLine($"  + {requirements}");
                }

                return sb.ToString();
            }

            private string ParseReputation(List<BlueprintEntity> entities)
            {
                var reputations = entities
                    .Where(e => e.Dialog != null)
                    .DistinctBy(e => e.Dialog)
                    .Select(e => _dialogueCache.RequiredReputation(e.Dialog!))
                    .Where(x => x != null)
                    .SelectMany(x => x!.ToList())
                    .OrderBy(x => x.Key);

                var sb = new StringBuilder();
                foreach (var kvp in reputations)
                {
                    // key: Eden_TraderGreet
                    // value: GetReputation(Faction.Trader) >= Reputation.FriendlyMin

                    sb.AppendLine($"{kvp.Key}");

                    // Clean up value
                    // TODO: Clean this up with regex
                    var cleaned = kvp.Value
                        .Replace("GetReputation(Faction.", "")
                        .Replace(")", "")
                        .Replace("Reputation.", "");
                    sb.AppendLine($"  + {cleaned}");
                }

                return sb.ToString();
            }

            private static string ParseRestockTime(List<BlueprintEntity> entities)
            {
                var min = entities.Min(bp => bp.Restock) ?? 0;
                var max = entities.Max(bp => bp.Restock) ?? 0;
                var avg = (int)(entities.Average(bp => bp.Restock) ?? 0);

                return $"Avg: {avg}\nMin: {min}\nMax: {max}";
            }
            #endregion
        }
    }
}
