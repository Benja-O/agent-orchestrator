using Orchestrator.GeneratedAppVerification;

// The third part of block 5's exit criterion, made reproducible:
//
//   "output/ se genera de cero, la app compila, y el endpoint que intenta violar la invariante
//    de ADR-009 la rechaza."
//
// The first two parts are what the orchestrator and its LSP gate already answer. This one they
// cannot: the gate verifies compilation, not correctness (ROADMAP, R4), and RN-01 can be absent
// from an application that builds perfectly. So it is checked from outside, over HTTP, against
// the running application — the only vantage point the acceptance criteria are written from.
//
//   dotnet run --project src/Orchestrator.GeneratedAppVerification
//
// It costs no quota: no agent is invoked and no language server is started. It is not in the test
// suite all the same, because it starts a real application from output/, and that directory does
// not exist until a run has produced it.

if (ApiShapeParser.IsHelpRequest(args))
{
    Console.WriteLine(ApiShapeParser.Usage);
    return 2;
}

ApiShape shape;

try
{
    shape = ApiShapeParser.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(ApiShapeParser.Usage);
    return 2;
}

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

await using var application = shape.UseRunningApplication
    ? GeneratedApplication.AlreadyRunning()
    : await StartAsync(shape, cancellation.Token);

using var client = new TaskManagerClient(shape);
var verdict = await InvariantVerification.RunAsync(client, cancellation.Token);

Console.WriteLine();
Console.WriteLine(new string('─', 78));

foreach (var line in verdict.Lines)
{
    Console.WriteLine(line);
}

Console.WriteLine(new string('─', 78));

// Always printed, not only on failure. When a check fails it is usually because a route flag is
// wrong rather than because the invariant broke, and the difference between those two is visible
// in one glance here — while a report that hides its evidence on success is a report nobody
// learns to read.
Console.WriteLine("Intercambios HTTP:");

foreach (var exchange in client.Exchanges)
{
    Console.WriteLine($"  {exchange}");
}

if (!verdict.Passed)
{
    Console.WriteLine();
    Console.WriteLine(
        "Si las rutas de arriba dieron 404, la API eligió otras: pasalas con --tasks, --dependencies");
    Console.WriteLine("y --complete. El spec no nombra endpoints a propósito, así que las elige el agente.");
}

return verdict.Passed ? 0 : 1;

static async Task<GeneratedApplication> StartAsync(ApiShape shape, CancellationToken cancellationToken)
{
    Console.WriteLine($"Starting the generated application from '{shape.ApiProjectPath}' on {shape.BaseUrl}…");
    var application = await GeneratedApplication.StartAsync(shape, TimeSpan.FromMinutes(3), cancellationToken);
    Console.WriteLine("  it is listening.");
    return application;
}
