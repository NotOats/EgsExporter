using EgsExporter.Exporters;
using EgsLib.ConfigFiles;
using EgsLib.ConfigFiles.Ecf;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text;

namespace EgsExporter.Commands
{
    public class ExportTradersSettings : BaseExportSettings
    {
        [CommandOption("--trader-file", IsHidden = true)]
        [Description("The trader file to export's full path")]
        public string? TraderFilePath { get; set; }

        [CommandOption("--group-items")]
        [Description("Group items at each trader for a single export entry")]
        public bool GroupItems { get; set; } = false;

        public override ValidationResult Validate()
        {
            var b = base.Validate();
            if (!b.Successful)
                return b;

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
            if (settings.ExportType != ExportType.Console && !Directory.Exists(settings.OutputPath))
                Directory.CreateDirectory(settings.OutputPath);

            IDataExporter? exporter = settings.ExportType switch
            {
                ExportType.Console => new ConsoleExporter(),
                ExportType.Csv => new CsvExporter(Path.Combine(settings.OutputPath!, "Traders.csv")),
                _ => null
            };

            if (exporter == null)
            {
                AnsiConsole.WriteLine($"Error: Unsupported data exporter {settings.ExportType}");
                return 1;
            }

            AnsiConsole.WriteLine($"Exporting to {settings.ExportType}");

            exporter.SetHeader(settings.GroupItems ? 
                ["Name", "Discount", "Buy", "Sell"] : 
                ["Name", "Discount", "Type", "Item", "Price", "Quantity"]);

            var objects = new EcfFile(settings.TraderFilePath!).ParseObjects();
            if (objects == null)
            {
                AnsiConsole.WriteLine($"Error: Failed to parase traders file at {settings.TraderFilePath}");
                return 1;
            }

            foreach (var obj in objects)
            {
                var trader = new Trader(obj);

                if (settings.GroupItems)
                {
                    ExportGroupedItem(exporter, trader);
                }
                else
                {
                    ExportSingleItems(exporter, trader);
                }
            }

            exporter.Flush();

            return 0;
        }

        private static void ExportGroupedItem(IDataExporter exporter, Trader trader)
        {
            // Buyables
            var sb = new StringBuilder();
            foreach (var item in trader.Buys)
            {
                sb.Append($"{item.Name}: ");

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
                sb.Append($"{item.Name}: ");

                if (item.SellMarketFactor)
                    sb.Append("mf=");

                sb.Append($"{item.SellValue}, ");
                sb.AppendLine(item.SellAmount.ToString());
            }

            var sell = sb;

            exporter.ExportRow([trader.Name, trader.Discount ?? 1, buy, sell]);
        }

        private static void ExportSingleItems(IDataExporter exporter, Trader trader)
        {
            var name = trader.Name;
            var discount = trader.Discount ?? 1;

            // Buyables
            foreach (var buyable in trader.Buys)
            {
                var value = buyable.BuyMarketFactor ? $"mf={buyable.BuyValue}" : buyable.BuyValue.ToString();

                // TODO: Add localization support
                exporter.ExportRow([name, discount, "Buy", buyable.Name, value, buyable.BuyAmount]);
            }

            // Sellables
            foreach (var sellable in trader.Sells)
            {
                var value = sellable.SellMarketFactor ? $"mf={sellable.BuyValue}" : sellable.BuyValue.ToString();

                // TODO: Add localization support
                exporter.ExportRow([name, discount, "Sell", sellable.Name, value, sellable.SellAmount]);
            }
        }
    }
}
