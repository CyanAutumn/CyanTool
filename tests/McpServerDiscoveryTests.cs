using ModelContextProtocol.Client;
using Xunit;

namespace CyanTools.Mcp.Tests;

public sealed class McpServerDiscoveryTests
{
    [Fact]
    public async Task StdioServer_ExposesTheDocumentedTopLevelTools()
    {
        string repositoryRoot = FindRepositoryRoot();
        string serverProject = Path.Combine(repositoryRoot, "CyanTools.McpServer", "CyanTools.McpServer.csproj");
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = ["run", "--project", serverProject, "--no-launch-profile"],
            WorkingDirectory = repositoryRoot,
            ShutdownTimeout = TimeSpan.FromSeconds(10)
        });

        await using McpClient client = await McpClient.CreateAsync(transport);
        IList<McpClientTool> tools = await client.ListToolsAsync();
        string[] names = tools.Select(tool => tool.Name).ToArray();

        Assert.Contains("cyantools_list_pages", names);
        Assert.Contains("cyantools_describe_operation", names);
        Assert.Contains("cyantools_run_pipeline_operation", names);
        Assert.Contains("cyantools_list_workflows", names);
        Assert.Contains("cyantools_describe_workflow", names);
        Assert.Contains("cyantools_run_workflow", names);
        Assert.Contains("cyantools_get_safety_guide", names);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CyanTools.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot locate the CyanTools repository root for the MCP integration test.");
    }
}
