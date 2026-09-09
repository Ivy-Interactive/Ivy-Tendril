using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core;
using Ivy.Core.Server;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;
using Ivy.Tendril.Apps.Chat;
using Ivy.Tendril.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Chat;

public class ChatModelPreferenceTests
{
    private static IServiceProvider CreateServiceProvider(
        IConfigService configService,
        IChatHistoryService chatService,
        IAgentRunner agentRunner)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configService);
        services.AddSingleton(chatService);
        services.AddSingleton(agentRunner);
        services.AddSingleton<IEventSerializer>(new JsonEventSerializer());

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
        services.AddSingleton<IUploadService>(new UploadService("conn1", null!));
        services.AddSingleton<IClientProvider>(new DummyClientProvider());

        var sessionStore = new AppSessionStore();
        services.AddSingleton(sessionStore);
        services.AddSingleton(new SignalRouter(sessionStore));
        services.AddSingleton<Ivy.Core.Apps.IAppRepository>(new Ivy.Core.Apps.AppRepository());

        return services.BuildServiceProvider();
    }

    private static (ChatApp App, Ivy.Core.WidgetTree Tree) CreateChatHost(IServiceProvider sp)
    {
        var app = new ChatApp();
        var contentBuilder = new ContentBuilder();
        var tree = new Ivy.Core.WidgetTree(app, contentBuilder, sp);
        var store = sp.GetRequiredService<AppSessionStore>();
        store.Sessions["conn1"] = new Ivy.Core.Apps.AppSession
        {
            ConnectionId = "conn1",
            AppId = "chat",
            MachineId = "mach1",
            ParentId = null,
            WidgetTree = tree,
            AppDescriptor = Ivy.Core.Apps.AppHelpers.GetApp(typeof(ChatApp)),
            App = app,
            ContentBuilder = contentBuilder,
            AppServices = sp,
            LastInteraction = DateTime.UtcNow,
        };
        return (app, tree);
    }

    [Fact]
    public void ChatApp_WhenNoExistingSessions_DefaultsToRememberedModelAndAgent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilChatModelPref_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var agentRunner = TestAgentRunner.Create();
            var codexModels = ChatApp.GetModelsForAgent(agentRunner, "codex");
            Assert.NotEmpty(codexModels);
            var targetModel = codexModels[0].Id;

            var config = new TendrilSettings
            {
                CodingAgent = "claude",
                LastChatAgent = "codex",
                LastChatModel = targetModel,
                LastChatEffort = "high"
            };

            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);

            var sp = CreateServiceProvider(configService, chatService, agentRunner);
            var ctx = new Ivy.Core.Hooks.ViewContext(() => { }, null, sp);

            var (app, _) = CreateChatHost(sp);
            app.BeforeBuild(ctx);
            var built = app.Build();
            app.AfterBuild();
            ctx.Reset();

            var fragment = Assert.IsType<Fragment>(built);
            var contentView = Assert.IsType<ContentView>(fragment.Children[0]);

            Assert.Equal("codex", contentView.SelectedAgentState.Value);
            Assert.Equal(targetModel, contentView.SelectedModelState.Value);
            Assert.Equal("high", contentView.SelectedEffortState.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ChatApp_WhenLastChatModelIsInvalidOrUnset_FallsBackGracefully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilChatModelPref_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var agentRunner = TestAgentRunner.Create();
            var codexModels = ChatApp.GetModelsForAgent(agentRunner, "codex");
            Assert.NotEmpty(codexModels);

            var config = new TendrilSettings
            {
                CodingAgent = "claude",
                LastChatAgent = "codex",
                LastChatModel = "nonexistent-model-xyz",
                LastChatEffort = null
            };

            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);

            var sp = CreateServiceProvider(configService, chatService, agentRunner);
            var ctx = new Ivy.Core.Hooks.ViewContext(() => { }, null, sp);

            var (app, _) = CreateChatHost(sp);
            app.BeforeBuild(ctx);
            var built = app.Build();
            app.AfterBuild();
            ctx.Reset();

            var fragment = Assert.IsType<Fragment>(built);
            var contentView = Assert.IsType<ContentView>(fragment.Children[0]);

            Assert.Equal("codex", contentView.SelectedAgentState.Value);
            // Fallback to first model in catalog for codex
            Assert.Equal(codexModels[0].Id, contentView.SelectedModelState.Value);
            Assert.Equal("default", contentView.SelectedEffortState.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ContentView_ModelAndAgentAndEffortChanged_PersistsToSettings()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilChatModelPref_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var agentRunner = TestAgentRunner.Create();
            var config = new TendrilSettings { CodingAgent = "claude" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var sp = CreateServiceProvider(configService, chatService, agentRunner);
            var ctx = new Ivy.Core.Hooks.ViewContext(() => { }, null, sp);

            var (app, _) = CreateChatHost(sp);
            app.BeforeBuild(ctx);
            var built = app.Build();
            app.AfterBuild();

            var fragment = Assert.IsType<Fragment>(built);
            var contentView = Assert.IsType<ContentView>(fragment.Children[0]);

            // Build ContentView to get ChatWidget
            contentView.BeforeBuild(ctx);
            var builtContent = contentView.Build();
            contentView.AfterBuild();
            ctx.Reset();

            var contentFragment = Assert.IsType<Fragment>(builtContent);
            var layoutView = Assert.IsType<LayoutView>(contentFragment.Children[0]);
            var layout = Assert.IsAssignableFrom<AbstractWidget>(layoutView.Build());
            var chatWidget = Assert.IsType<Ivy.Tendril.Widgets.ChatWidget>(layout.Children[0]);

            // 1. Change Agent to codex
            Assert.NotNull(chatWidget.OnAgentChanged);
            await chatWidget.OnAgentChanged(new Event<Ivy.Tendril.Widgets.ChatWidget, string>("OnAgentChanged", chatWidget, "codex"));

            Assert.Equal("codex", configService.Settings.LastChatAgent);
            var codexModels = ChatApp.GetModelsForAgent(agentRunner, "codex");
            Assert.Equal(codexModels[0].Id, configService.Settings.LastChatModel);

            // 2. Change Model
            Assert.NotNull(chatWidget.OnModelChanged);
            await chatWidget.OnModelChanged(new Event<Ivy.Tendril.Widgets.ChatWidget, string>("OnModelChanged", chatWidget, "custom-codex-model"));
            Assert.Equal("custom-codex-model", configService.Settings.LastChatModel);
            Assert.Equal("codex", configService.Settings.LastChatAgent);

            // 3. Change Effort
            Assert.NotNull(chatWidget.OnEffortChanged);
            await chatWidget.OnEffortChanged(new Event<Ivy.Tendril.Widgets.ChatWidget, string>("OnEffortChanged", chatWidget, "low"));
            Assert.Equal("low", configService.Settings.LastChatEffort);

            // 4. Verify disk persistence
            var reloaded = new ConfigService(new TendrilSettings(), tempDir);
            reloaded.ReloadSettings();
            Assert.Equal("codex", reloaded.Settings.LastChatAgent);
            Assert.Equal("custom-codex-model", reloaded.Settings.LastChatModel);
            Assert.Equal("low", reloaded.Settings.LastChatEffort);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
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
