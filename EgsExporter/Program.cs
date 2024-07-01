using EgsExporter.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(c =>
{
    c.AddCommand<ExportTraders>("traders");

#if DEBUG
    c.PropagateExceptions();
    c.ValidateExamples();
#endif
});

await app.RunAsync(args);