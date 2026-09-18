using App.Core.Infrastructure.Ipc;
using App.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// stdout은 MCP 프로토콜(JSON-RPC) 전용이라 로그는 반드시 stderr로 보내야 한다.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<CharacterIpcClient>();
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<CharacterTools>();

await builder.Build().RunAsync();
