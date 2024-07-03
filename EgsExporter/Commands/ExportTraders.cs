using EgsExporter.Exporters;
using EgsLib;
using EgsLib.ConfigFiles;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text;

namespace EgsExporter.Commands
{
    public class ExportTradersSettings : BaseExportSettings
    {
        [CommandOption("--localization-file", IsHidden = true)]
        [Description("The localization file to use")]
        public string? LocalizationFilePath { get; set; }

        [CommandOption("--trader-file", IsHidden = true)]
        [Description("The trader file to export's full path")]
        public string? TraderFilePath { get; set; }

        [CommandOption("--group-items")]
        [Description("Group items at each trader for a single export entry")]
        public bool GroupItems { get; set; } = false;

        [CommandOption("-l|--localize-names")]
        [Description("Localize all names to English or use their key values")]
        public bool LocalizeNames { get; set; } = false;

        public override ValidationResult Validate()
        {
            var b = base.Validate();
            if (!b.Successful)
                return b;

            LocalizationFilePath ??= Path.Combine(ScenarioPath!, @"Extras\Localization.csv");
            if (!File.Exists(LocalizationFilePath))
                return ValidationResult.Error("Localization file does not exist");

            TraderFilePath ??= Path.Combine(ScenarioPath!, @"Content\Configuration\TraderNPCConfig.ecf");
            if (!File.Exists(TraderFilePath))
                return ValidationResult.Error("Trader file does not exist");

            return ValidationResult.Success();
        }
    }

    public class ExportTraders : Command<ExportTradersSettings>
    {
        public override int Execute(CommandContext context, ExportTradersSettings settings)
        {
            try
            {
                var data = new TraderData(settings);
                data.Export();
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteLine($"Error: {ex.Message}");
                return 1;
            }

            return 0;
        }

        private class TraderData
        {
            private readonly ExportTradersSettings _settings;
            private readonly IDataExporter _exporter;

            private readonly Localization _localization;
            private readonly List<Trader> _traders;

            public TraderData(ExportTradersSettings settings)
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
                    _traders = Trader.ReadFile(settings.TraderFilePath).ToList();
            }
            public void Export()
            {
                AnsiConsole.WriteLine($"Exporting to {_settings.ExportType}");

                _exporter.SetHeader(_settings.GroupItems ?
                    ["Name", "Discount", "Buy", "Sell"] :
                    ["Name", "Discount", "Type", "Item", "Price", "Quantity"]);

                foreach (var trader in _traders)
                {
                    if (_settings.GroupItems)
                        ExportGroupedItem(trader);
                    else
                        ExportSingleItems(trader);
                }

                _exporter.Flush();
            }

            private void ExportGroupedItem(Trader trader)
            {
                // Buyables
                var sb = new StringBuilder();
                foreach (var item in trader.Buys)
                {
                    sb.Append($"{Localize(item.Name)}: ");

                    if (item.BuyMarketFactor)
                        sb.Append("mf=");

                    sb.Append($"{item.BuyValue}, ");
                    sb.AppendLine(item.BuyAmount.ToString());
                }

                var buy = sb;

                // Sellables
                sb = new StringBuilder();
                foreach (var item in trader.Sells)
                {
                    sb.Append($"{Localize(item.Name)}: ");

                    if (item.SellMarketFactor)
                        sb.Append("mf=");

                    sb.Append($"{item.SellValue}, ");
                    sb.AppendLine(item.SellAmount.ToString());
                }

                var sell = sb;

                _exporter.ExportRow([trader.Name, trader.Discount ?? 1, buy, sell]);
            }

            private void ExportSingleItems(Trader trader)
            {
                var name = trader.Name;
                var discount = trader.Discount ?? 1;

                // Buyables
                foreach (var buyable in trader.Buys)
                {
                    var value = buyable.BuyMarketFactor ? $"mf={buyable.BuyValue}" : buyable.BuyValue.ToString();

                    _exporter.ExportRow([Localize(name), discount, "Buy", Localize(buyable.Name), value, buyable.BuyAmount]);
                }

                // Sellables
                foreach (var sellable in trader.Sells)
                {
                    var value = sellable.SellMarketFactor ? $"mf={sellable.BuyValue}" : sellable.BuyValue.ToString();

                    _exporter.ExportRow([Localize(name), discount, "Sell", Localize(sellable.Name), value, sellable.SellAmount]);
                }
            }

            /// <summary>
            /// Localizes a string if needed (--localize-names)
            /// </summary>
            /// <param name="key"></param>
            /// <returns></returns>
            private string Localize(string key)
            {
                if (!_settings.LocalizeNames)
                    return key;

                // TODO: Multiple language support
                return _localization.Localize(key, "English");
            }
        }
    }
}
