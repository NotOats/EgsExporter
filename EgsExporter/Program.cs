using EgsExporter.Commands;
using EgsLib.ConfigFiles;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.AddCommand<ExportTraders>("traders")
        .WithDescription("Exports TraderNPCConfig.ecf")
        .WithExample("traders", @"""C:\Path\To\Empyrion - Galactic Survival""", "--type=Csv")
        .WithExample("traders", @"""C:\Path\To\Empyrion - Galactic Survival""", "--type=Console", "--group-items=true");

#if DEBUG
    config.PropagateExceptions();
    config.ValidateExamples();
#endif
});

await app.RunAsync(args);