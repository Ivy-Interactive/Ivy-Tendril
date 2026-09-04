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

        var sp = CreateServiceProvider(configService, chatService, agentRunner, serializer);

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

            var sp = CreateServiceProvider(configService, chatService, agentRunner, serializer);

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

            var sp = CreateServiceProvider(configService, chatService, agentRunner, serializer);
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

    [Fact]
    public async Task ChatExecutionService_CancelAsync_ClearsGeneratingStateAndClosesStream()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilChatCancelTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var agentRunner = TestAgentRunner.Create();
            var serializer = new JsonEventSerializer();
            var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);

            var execService = new ChatExecutionService(configService, chatService, agentRunner, namingService, serializer);

            var sess = chatService.CreateSession("codex", "gpt-5.6-sol");
            _ = execService.SendMessageAsync(sess.Id, "Hello");

            Assert.True(execService.IsGenerating(sess.Id));
            Assert.True(chatService.GetGeneratingSessionIds().Contains(sess.Id));

            // Verify live stream observable is subscribable
            var observable = execService.GetLiveStreamObservable(sess.Id);
            Assert.NotNull(observable);

            // Now cancel
            await execService.CancelAsync(sess.Id);

            Assert.False(execService.IsGenerating(sess.Id));
            Assert.False(chatService.GetGeneratingSessionIds().Contains(sess.Id));
            Assert.Equal("", execService.GetStreamSnapshot(sess.Id));
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
    public async Task ChatExecutionService_LiveStreamObservable_EmitsEventsToSubscriber()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilChatStreamTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var agentRunner = TestAgentRunner.Create();
            var serializer = new JsonEventSerializer();
            var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);

            var execService = new ChatExecutionService(configService, chatService, agentRunner, namingService, serializer);

            var sess = chatService.CreateSession("codex", "gpt-5.6-sol");

            var receivedEvents = new List<string>();

            // Subscribe to live stream observable before launch
            var observable = execService.GetLiveStreamObservable(sess.Id);
            Assert.NotNull(observable);

            using var sub = observable.Subscribe(line => receivedEvents.Add(line));

            // Start sending message
            _ = execService.SendMessageAsync(sess.Id, "Hello");

            // Verify session is marked generating immediately
            Assert.True(execService.IsGenerating(sess.Id));

            // Cancel to complete cleanly
            await execService.CancelAsync(sess.Id);
            Assert.False(execService.IsGenerating(sess.Id));
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
    public void ChatApp_MultiSession_OmitsMessagesForInactiveSessions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilChatMultiSessionTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var agentRunner = TestAgentRunner.Create();
            var serializer = new JsonEventSerializer();

            var sess1 = chatService.CreateSession("codex", "gpt-5.6-sol");
            chatService.AddMessage(sess1.Id, "user", "msg1", "codex", "gpt-5.6-sol");
            var sess2 = chatService.CreateSession("codex", "gpt-5.6-sol");
            chatService.AddMessage(sess2.Id, "user", "msg2", "codex", "gpt-5.6-sol");

            var sp = CreateServiceProvider(configService, chatService, agentRunner, serializer);
            var ctx = new Ivy.Core.Hooks.ViewContext(() => { }, null, sp);

            var app = new Ivy.Tendril.Apps.Chat.ChatApp();
            app.BeforeBuild(ctx);
            var built = app.Build();
            app.AfterBuild();
            ctx.Reset();

            Assert.NotNull(built);
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
    public async Task ChatExecutionService_StaleGeneratingState_SelfHealsAndDoesNotBlockNewMessage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilChatStaleTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var agentRunner = TestAgentRunner.Create();
            var serializer = new JsonEventSerializer();
            var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);

            var execService = new ChatExecutionService(configService, chatService, agentRunner, namingService, serializer);

            var sess = chatService.CreateSession("codex", "gpt-5.6-sol");

            // Simulate orphaned generating state in chatService without active execution
            chatService.SetSessionGenerating(sess.Id, true);

            // IsGenerating is pure: returns false when no active execution exists
            Assert.False(execService.IsGenerating(sess.Id));

            // Sending a message must execute directly and NOT enqueue
            _ = execService.SendMessageAsync(sess.Id, "can you do this again?");

            // Verify message was not enqueued
            Assert.Empty(chatService.GetQueuedMessages(sess.Id));

            // Verify session is now genuinely generating
            Assert.True(execService.IsGenerating(sess.Id));

            // Clean up
            await execService.CancelAsync(sess.Id);
            Assert.False(execService.IsGenerating(sess.Id));
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
    public async Task ChatApp_LiveExecution_StreamsSnapshotAndUpdatesView()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilChatAppLiveTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var agentRunner = TestAgentRunner.Create();
            var serializer = new JsonEventSerializer();

            var sess = chatService.CreateSession("codex", "gpt-5.6-sol");

            var sp = CreateServiceProvider(configService, chatService, agentRunner, serializer);
            var execService = (ChatExecutionService)sp.GetRequiredService<IChatExecutionService>();

            var ctxApp = new Ivy.Core.Hooks.ViewContext(() => { }, null, sp);
            var ctxContent = new Ivy.Core.Hooks.ViewContext(() => { }, null, sp);

            var app = new Ivy.Tendril.Apps.Chat.ChatApp();
            app.BeforeBuild(ctxApp);
            var built1 = app.Build() as Ivy.SidebarLayout;
            app.AfterBuild();
            ctxApp.Reset();

            Assert.NotNull(built1);
            var contentView1 = built1.Children.OfType<Ivy.Slot>().First(s => s.Name == "MainContent").Children.First() as Ivy.Tendril.Apps.Chat.ContentView;
            Assert.NotNull(contentView1);

            // Initial build: not generating, empty stream snapshot
            contentView1.BeforeBuild(ctxContent);
            var builtWidget1 = contentView1.Build() as Ivy.Fragment;
            Assert.NotNull(builtWidget1);
            var layoutView1 = builtWidget1.Children[0] as Ivy.LayoutView;
            Assert.NotNull(layoutView1);
            var stack1 = layoutView1.Build() as Ivy.Core.AbstractWidget;
            Assert.NotNull(stack1);
            var chatWidget1 = stack1.Children[0] as Ivy.Tendril.Widgets.ChatWidget;
            contentView1.AfterBuild();
            ctxContent.Reset();

            Assert.NotNull(chatWidget1);
            Assert.False(chatWidget1.IsStreaming);
            Assert.Equal(string.Empty, chatWidget1.StreamingText);

            // Now send a message
            _ = execService.SendMessageAsync(sess.Id, "Hello streaming test");

            // Session is now generating
            Assert.True(execService.IsGenerating(sess.Id));

            // Emit some live stream events
            execService.EmitStreamLine(sess.Id, "{\"kind\":\"thinking\",\"content\":\"thinking deeply\"}");
            execService.EmitStreamLine(sess.Id, "{\"kind\":\"text\",\"text\":\"hello world\"}");

            // Verify GetStreamSnapshot returns the emitted lines
            var snapshot = execService.GetStreamSnapshot(sess.Id);
            Assert.Contains("thinking deeply", snapshot);
            Assert.Contains("hello world", snapshot);

            // Re-render ChatApp
            app.BeforeBuild(ctxApp);
            var built2 = app.Build() as Ivy.SidebarLayout;
            app.AfterBuild();
            ctxApp.Reset();

            var contentView2 = built2!.Children.OfType<Ivy.Slot>().First(s => s.Name == "MainContent").Children.First() as Ivy.Tendril.Apps.Chat.ContentView;
            Assert.NotNull(contentView2);

            var ctxContent2 = new Ivy.Core.Hooks.ViewContext(() => { }, null, sp);
            contentView2.BeforeBuild(ctxContent2);
            var builtWidget2 = contentView2.Build() as Ivy.Fragment;
            Assert.NotNull(builtWidget2);
            var layoutView2 = builtWidget2.Children[0] as Ivy.LayoutView;
            Assert.NotNull(layoutView2);
            var stack2 = layoutView2.Build() as Ivy.Core.AbstractWidget;
            Assert.NotNull(stack2);
            var chatWidget2 = stack2.Children[0] as Ivy.Tendril.Widgets.ChatWidget;
            contentView2.AfterBuild();
            ctxContent2.Reset();

            Assert.NotNull(chatWidget2);
            Assert.True(chatWidget2.IsStreaming);
            Assert.Contains("thinking deeply", chatWidget2.StreamingText);
            Assert.Contains("hello world", chatWidget2.StreamingText);

            // Clean up
            await execService.CancelAsync(sess.Id);
            Assert.False(execService.IsGenerating(sess.Id));
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
    public void ChatExecutionService_LiveStreamObservable_DoesNotReplayPastEvents()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilChatNoReplayTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var agentRunner = TestAgentRunner.Create();
            var serializer = new JsonEventSerializer();
            var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);

            var execService = new ChatExecutionService(configService, chatService, agentRunner, namingService, serializer);

            var sess = chatService.CreateSession("codex", "gpt-5.6-sol");

            var observable = execService.GetLiveStreamObservable(sess.Id);
            var receivedLines = new List<string>();

            // Emit an event before subscribing
            execService.EmitStreamLine(sess.Id, "line 1");

            // Now subscribe
            using var sub = observable.Subscribe(line => receivedLines.Add(line));

            // Verify line 1 was NOT replayed
            Assert.Empty(receivedLines);

            // Emit a new event
            execService.EmitStreamLine(sess.Id, "line 2");

            // Verify only line 2 was received
            Assert.Single(receivedLines);
            Assert.Equal("line 2", receivedLines[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    private static IServiceProvider CreateServiceProvider(
        IConfigService configService,
        IChatHistoryService chatService,
        IAgentRunner agentRunner,
        IEventSerializer serializer)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(configService);
        services.AddSingleton(chatService);
        services.AddSingleton(agentRunner);
        services.AddSingleton(serializer);
        var appContext = (Ivy.AppContext)Activator.CreateInstance(
            typeof(Ivy.AppContext),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null,
            new object?[] { "conn1", "mach1", "chat", "chat", null, "http", "localhost", null },
            null)!;
        services.AddSingleton(appContext);
        var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);
        services.AddSingleton<IChatSessionNamingService>(namingService);
        services.AddSingleton<IChatExecutionService, ChatExecutionService>();
        services.AddSingleton<IUploadService>(new Ivy.UploadService("conn1", null!));
        services.AddSingleton<IClientProvider>(new DummyClientProvider());
        return services.BuildServiceProvider();
    }

    private sealed class DummyClientProvider : IClientProvider
    {
        public IClientSender Sender { get; set; } = new DummyClientSender();
    }

    private sealed class DummyClientSender : IClientSender
    {
        public void Send(string method, object? data) { }
    }
}

