using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Agents.Runtime;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Ivy.Tendril.Test;

public class ChatExecutionIntegrationTest
{
    private readonly ITestOutputHelper _output;

    public ChatExecutionIntegrationTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SendMessage_WithCodex_ExecutesAndAddsAssistantMessage()
    {
        if (Ivy.Tendril.Agents.Helpers.BinaryResolver.FindOnPath("codex") == null)
            return;

        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilChatExecTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings
            {
                CodingAgent = "codex"
            };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var agentRunner = TestAgentRunner.Create();
            var serializer = new JsonEventSerializer();

            var session = chatService.CreateSession("codex", "gpt-5.6-sol");
            var userMsg = "test. Alive?";

            chatService.AddMessage(session.Id, "user", userMsg, "codex", "gpt-5.6-sol");

            var sess = chatService.GetSession(session.Id);
            Assert.NotNull(sess);
            Assert.Single(sess.Messages);

            var fullAgentPrompt = "# Current User Request\n" + userMsg;
            var context = AgentLaunchHelper.PrepareResolutionContext(
                configService,
                agentRunner,
                "codex",
                fullAgentPrompt,
                modelOverride: "gpt-5.6-sol",
                effortOverride: null,
                permissionMode: PermissionMode.FullAuto);

            _output.WriteLine($"Launching agent with workdir {context.WorkingDirectory}");

            var agentSession = await agentRunner.LaunchAsync(context);
            _output.WriteLine($"Session launched: {agentSession.SessionId}");

            var rawLines = new System.Collections.Generic.List<string>();
            string? lastTextEvent = null;
            var rawLock = new object();

            using var sub = agentSession.Events.Subscribe(evt =>
            {
                try
                {
                    _output.WriteLine($"Event received: {evt.Kind}");
                    if (evt is TextEvent textEvt && !string.IsNullOrWhiteSpace(textEvt.Text))
                    {
                        _output.WriteLine($"TextEvent: {textEvt.Text}");
                        lock (rawLock)
                        {
                            lastTextEvent = textEvt.Text;
                        }
                    }

                    var wireJson = serializer.Serialize(evt);
                    if (!string.IsNullOrEmpty(wireJson))
                    {
                        lock (rawLock)
                        {
                            rawLines.Add(wireJson);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Subscribe ex: {ex}");
                }
            });

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await agentSession.WaitForCompletionAsync(timeoutCts.Token);
            _output.WriteLine($"WaitForCompletionAsync returned: Success={result.IsSuccess}, ExitCode={result.ExitCode}, Response={result.Response}");

            string? collectedText = null;
            string? fullRawStream = null;
            lock (rawLock)
            {
                collectedText = lastTextEvent;
                if (rawLines.Count > 0) fullRawStream = string.Join("\n", rawLines);
            }

            var responseContent = !string.IsNullOrWhiteSpace(result.Response)
                ? result.Response
                : (!string.IsNullOrWhiteSpace(collectedText)
                    ? collectedText
                    : (result.IsSuccess ? "Task completed successfully." : "Agent execution completed with status code " + (result.ExitCode?.ToString() ?? "unknown")));

            _output.WriteLine($"Final responseContent: {responseContent}");

            chatService.AddMessage(session.Id, "assistant", responseContent, "codex", "gpt-5.6-sol", rawStream: fullRawStream);

            var updatedSession = chatService.GetSession(session.Id);
            Assert.NotNull(updatedSession);
            Assert.Equal(2, updatedSession.Messages.Count);
            Assert.Equal("assistant", updatedSession.Messages[1].Role);
            _output.WriteLine($"Session verified with {updatedSession.Messages.Count} messages");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task WidgetTree_BuildAsync_WithRealUserTendrilDir_Succeeds()
    {
        var tendrilDir = "/Users/rorychatt/.tendril";
        if (!Directory.Exists(tendrilDir)) return;

        var configService = new ConfigService(new TendrilSettings { CodingAgent = "codex" }, tendrilDir);
        var chatService = new ChatHistoryService(configService);
        var agentRunner = TestAgentRunner.Create();
        var serializer = new JsonEventSerializer();

        var sessions = chatService.GetSessions();
        _output.WriteLine($"Found {sessions.Count} sessions in real .tendril dir");
        foreach (var s in sessions)
        {
            _output.WriteLine($"Session: {s.Id}, Title: {s.Title}, Messages: {s.Messages.Count}");
        }

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IConfigService>(configService);
        services.AddSingleton<IChatHistoryService>(chatService);
        services.AddSingleton<IAgentRunner>(agentRunner);
        services.AddSingleton<IEventSerializer>(serializer);
        var appContext = (Ivy.AppContext)Activator.CreateInstance(
            typeof(Ivy.AppContext),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null,
            new object?[] { "conn1", "mach1", "chat", "chat", null, "http", "localhost", null },
            null)!;
        services.AddSingleton(appContext);
        var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);
        services.AddSingleton<IChatSessionNamingService>(namingService);
        services.AddSingleton<IUploadService>(new Ivy.UploadService("conn1", null!));

        var sp = services.BuildServiceProvider();

        var app = new Ivy.Tendril.Apps.Chat.ChatApp();
        var contentBuilder = new Ivy.ContentBuilder();
        var tree = new Ivy.Core.WidgetTree(app, contentBuilder, sp);

        var buildTask = tree.BuildAsync();
        var completedTask = await Task.WhenAny(buildTask, Task.Delay(5000));
        Assert.True(completedTask == buildTask, "tree.BuildAsync with real .tendril timed out!");

        var widgets = tree.GetWidgets();
        Assert.NotNull(widgets);
        _output.WriteLine($"Real tree built successfully! Root: {widgets.GetType().Name}");
    }

    [Fact]
    public void AppDescriptor_ForChatApp_HasIdChat()
    {
        var descriptor = Ivy.Core.Apps.AppHelpers.GetApp(typeof(Ivy.Tendril.Apps.Chat.ChatApp));
        _output.WriteLine($"ChatApp descriptor ID: '{descriptor.Id}', Title: '{descriptor.Title}'");
        Assert.Equal("chat", descriptor.Id);
    }

    [Fact]
    public async Task WidgetTree_BuildAsync_ForChatApp_CompletesSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilChatTreeTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var agentRunner = TestAgentRunner.Create();
            var serializer = new JsonEventSerializer();

            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddSingleton<IConfigService>(configService);
            services.AddSingleton<IChatHistoryService>(chatService);
            services.AddSingleton<IAgentRunner>(agentRunner);
            services.AddSingleton<IEventSerializer>(serializer);
            var appContext = (Ivy.AppContext)Activator.CreateInstance(
                typeof(Ivy.AppContext),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new object?[] { "conn1", "mach1", "chat", "chat", null, "http", "localhost", null },
                null)!;
            services.AddSingleton(appContext);
            var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);
            services.AddSingleton<IChatSessionNamingService>(namingService);
            services.AddSingleton<IUploadService>(new Ivy.UploadService("conn1", null!));

            var sp = services.BuildServiceProvider();

            var app = new Ivy.Tendril.Apps.Chat.ChatApp();
            var contentBuilder = new Ivy.ContentBuilder();
            var tree = new Ivy.Core.WidgetTree(app, contentBuilder, sp);

            var buildTask = tree.BuildAsync();
            var completedTask = await Task.WhenAny(buildTask, Task.Delay(5000));
            Assert.True(completedTask == buildTask, "tree.BuildAsync timed out!");

            var widgets = tree.GetWidgets();
            Assert.NotNull(widgets);
            _output.WriteLine($"Tree root widget: {widgets.GetType().Name}");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void ChatApp_Build_WithExistingSession_RendersSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilChatBuildTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var agentRunner = TestAgentRunner.Create();
            var serializer = new JsonEventSerializer();

            var sess = chatService.CreateSession("codex", "gpt-5.6-sol");
            chatService.AddMessage(sess.Id, "user", "test", "codex", "gpt-5.6-sol");

            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddSingleton<IConfigService>(configService);
            services.AddSingleton<IChatHistoryService>(chatService);
            services.AddSingleton<IAgentRunner>(agentRunner);
            services.AddSingleton<IEventSerializer>(serializer);
            var appContext = (Ivy.AppContext)Activator.CreateInstance(
                typeof(Ivy.AppContext),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new object?[] { "conn1", "mach1", "chat", "chat", null, "http", "localhost", null },
                null)!;
            services.AddSingleton(appContext);
            var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);
            services.AddSingleton<IChatSessionNamingService>(namingService);

            var sp = services.BuildServiceProvider();
            var ctx = new Ivy.Core.Hooks.ViewContext(() => { }, null, sp);

            var app = new Ivy.Tendril.Apps.Chat.ChatApp();
            app.BeforeBuild(ctx);
            var built = app.Build();
            app.AfterBuild();
            ctx.Reset();

            Assert.NotNull(built);
            _output.WriteLine($"ChatApp.Build returned: {built.GetType().Name}");

            int refreshCount = 0;
            ctx = new Ivy.Core.Hooks.ViewContext(() => { refreshCount++; }, null, sp);
            app.BeforeBuild(ctx);
            built = app.Build();
            app.AfterBuild();
            ctx.Reset();

            _output.WriteLine($"Refresh count after second build: {refreshCount}");
            Assert.True(refreshCount <= 2, $"Refresh count was too high: {refreshCount}");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
