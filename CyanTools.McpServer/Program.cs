using CyanTools.McpServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CyanTools.McpServer;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        if (args.Any(argument => string.Equals(argument, "--list", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(CyanToolsMcpTools.SerializeCatalog());
            return;
        }

        int describeIndex = Array.FindIndex(args, argument => string.Equals(argument, "--describe", StringComparison.OrdinalIgnoreCase));
        if (describeIndex >= 0)
        {
            if (describeIndex == args.Length - 1)
            {
                Console.Error.WriteLine("--describe requires an operation id.");
                Environment.ExitCode = 2;
                return;
            }

            Console.WriteLine(CyanToolsMcpTools.CyantoolsDescribeOperation(args[describeIndex + 1]));
            return;
        }

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<PipelineOperationExecutor>();
        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<CyanToolsMcpTools>();

        await builder.Build().RunAsync().ConfigureAwait(false);
    }
}
