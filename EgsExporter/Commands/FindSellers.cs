using EgsExporter.Exporters;
using EgsExporter.GameData;
using EgsLib.ConfigFiles;
using EgsLib;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EgsLib.Playfields;
using Sylvan.Data.Csv;

namespace EgsExporter.Commands
{
    public enum FindSellersTradeType
    {
        Buy,
        Sell,
        Both
    }

    public class FindSellersSettings : BaseExportSettings
    {
        [CommandArgument(1, "<item_name>")]
        [Description("The item to look up, in english or untranslated key")]
        public string ItemName { get; set; } = string.Empty;

        [CommandOption("--trader-type")]
        [Description("To search for buyerr, sellers, or both")]
        public FindSellersTradeType TraderType { get; set; } = FindSellersTradeType.Both;

        #region Hidden options, file overrides
        [CommandOption("--localization-file", IsHidden = true)]
        [Description("The localization file to use")]
        public string? LocalizationFilePath { get; set; }

        [CommandOption("--trader-file", IsHidden = true)]
        [Description("The trader file to use")]
        public string? TraderFilePath { get; set; }

        [CommandOption("--item-file", IsHidden = true)]
        [Description("The item file to use")]
        public string? ItemFilePath { get; set; }

        [CommandOption("--dialogue-file", IsHidden = true)]
        [Description("The dialogue file to use")]
        public string? DialogueFilePath { get; set; }

        [CommandOption("--blueprint-folder", IsHidden = true)]
        [Description("The blueprint folder to use")]
        public string? BlueprintFolder { get; set; }

        [CommandOption("---playfield-folder", IsHidden = true)]
        [Description("The playfield folder to use")]
        public string? PlayfieldFolder { get; set; }
        #endregion

        public override ValidationResult Validate()
        {
            var b = base.Validate();
            if (!b.Successful)
                return b;

            // Validate item name
            if (string.IsNullOrEmpty(ItemName))
                return ValidationResult.Error("ItemName was not specified");

            // Fix up paths
            #region File overrides
            LocalizationFilePath ??= Path.Combine(ScenarioPath!, @"Extras\Localization.csv");
            if (!File.Exists(LocalizationFilePath))
                return ValidationResult.Error("Localization file does not exist");

            TraderFilePath ??= Path.Combine(ScenarioPath!, @"Content\Configuration\TraderNPCConfig.ecf");
            if (!File.Exists(TraderFilePath))
                return ValidationResult.Error("Trader file does not exist");

            ItemFilePath ??= Path.Combine(ScenarioPath!, @"Content\Configuration\ItemsConfig.ecf");
            if (!File.Exists(ItemFilePath))
                return ValidationResult.Error("Item file does not exist");

            DialogueFilePath ??= Path.Combine(ScenarioPath!, @"Content\Configuration\Dialogues.ecf");
            if (!File.Exists(TraderFilePath))
                return ValidationResult.Error("Dialogue file does not exist");

            BlueprintFolder ??= Path.Combine(ScenarioPath!, @"Prefabs");
            if (!Directory.Exists(BlueprintFolder))
                return ValidationResult.Error("Blueprint folder does not exist");

            PlayfieldFolder ??= Path.Combine(ScenarioPath!, @"Playfields");
            if (!Directory.Exists(BlueprintFolder))
                return ValidationResult.Error("Playfield folder does not exist");
            #endregion

            return ValidationResult.Success();
        }
    }

    internal class FindSellers : Command<FindSellersSettings>
    {
        public override int Execute(CommandContext context, FindSellersSettings settings)
        {
            try
            {
                var sellerData = new FindSellersData(settings);
                sellerData.Export();
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteLine($"Error: {ex.Message}");
                return 1;
            }

            return 0;
        }

        public class FindSellersData
        {
            private readonly FindSellersSettings _settings;
            private readonly IDataExporter _exporter;

            // Spreadsheet raw data
            private readonly Localization _localization;
            private readonly List<Trader> _traders;
            private readonly List<Item> _items;
            private readonly List<Dialogue> _dialogues;
            private readonly DialogueCache _dialogueCache;
            private readonly Dictionary<string, List<BlueprintEntity>> _entityNameBlueprintMap;
            private readonly IReadOnlyDictionary<string, IList<Playfield>> _groupNamePlayfieldMap;

            public FindSellersData(FindSellersSettings settings)
            {
                _settings = settings;
                _exporter = settings.CreateExporter($"{settings.ItemName.Replace(' ', '_')}-Traders")
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
                    _items = Item.ReadFile(settings.ItemFilePath).ToList();
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
                // Item Name, Trader Name, Type [Buy/Sell/Both], PoIs[that have that specific trader & type]
                _exporter.SetHeader(["Item", "Trader", "Trader Type", "Price", "Quantity", "Points Of Interest"]);

                var itemName = Delocalize(_settings.ItemName);

                foreach (var trader in _traders.OrderBy(x => x.Name))
                {
                    var buy = trader.Buys.FirstOrDefault(x => x.Name == itemName);
                    var sell = trader.Sells.FirstOrDefault(x => x.Name == itemName);
                    
                    // Not found
                    if (buy == null && sell == null)
                        continue;

                    var translatedTraderName = _localization.Localize(trader.Name, "English");

                    if (buy != null && 
                        (_settings.TraderType == FindSellersTradeType.Buy 
                        || _settings.TraderType == FindSellersTradeType.Both))
                    {
                        var translatedItemName = _localization.Localize(buy.Name, "English");
                        var price = FindItemBuyPrice(buy, FindSellersTradeType.Buy);
                        var pois = FindPoiForTrader(trader);

                        _exporter.ExportRow([translatedItemName, translatedTraderName, "Buys", price, buy.BuyAmount, pois]);
                    }

                    if (sell != null &&
                        (_settings.TraderType == FindSellersTradeType.Sell
                        || _settings.TraderType == FindSellersTradeType.Both))
                    {
                        var translatedItemName = _localization.Localize(sell.Name, "English");
                        var price = FindItemBuyPrice(sell, FindSellersTradeType.Sell);
                        var pois = FindPoiForTrader(trader);

                        _exporter.ExportRow([translatedItemName, translatedTraderName, "Sells", price, sell.SellAmount, pois]);
                    }
                }

                _exporter.Flush();
            }

            private string Delocalize(string value)
            {
                // Going to be slow, not really setup to reverse localization in egslib....
                foreach (var outerKvp in _localization.LocalizationData)
                {
                    var key = outerKvp.Key;
                    var languageMap = outerKvp.Value;

                    foreach (var kvp in languageMap)
                    {
                        var language = kvp.Key;
                        var translated = kvp.Value;

                        if (value == translated)
                            return key;
                    }
                }

                return value;
            }

            private string FindItemBuyPrice(Trader.TraderItem traderItem, FindSellersTradeType tradeType)
            {
                var marketFactor = tradeType == FindSellersTradeType.Buy 
                    ? traderItem.BuyMarketFactor : traderItem.SellMarketFactor;

                var value = tradeType == FindSellersTradeType.Buy 
                    ? traderItem.BuyValue : traderItem.SellValue;

                var item = _items.FirstOrDefault(x => x.Name == traderItem.Name);
                if (item != null)
                {
                    if (!marketFactor)
                        return value.ToString();

                    var marketPrice = item.MarketPrice?.Value;

                    if (marketPrice != null)
                        return $"[{value.Minimum * marketPrice:n0} - {value.Maximum * marketPrice:n0}]";
                }

                return marketFactor ? $"mf={value}" : value.ToString();
            }

            private string FindPoiForTrader(Trader trader)
            {
                if (!_entityNameBlueprintMap.TryGetValue(trader.Name, out var entities) || entities == null)
                    return string.Empty;


                var sortedPois = entities
                    .DistinctBy(x => x.FileName)
                    .OrderBy(x => x.DisplayName)
                    .Select(x =>
                    {
                        var count = entities.Count(y => y.FileName == x.FileName);

                        var name = string.IsNullOrEmpty(x.DisplayName) ? "N/A" : x.DisplayName;

                        var sb = new StringBuilder();
                        sb.Append($"{name} ({Path.GetFileNameWithoutExtension(x.FileName)})");

                        if (count > 1)
                            sb.Append($" x{count}");

                        sb.Append($", {x.Restock} restock");

                        return sb.ToString();
                    });

                return string.Join('\n', sortedPois); 
            }
        }
    }
}
