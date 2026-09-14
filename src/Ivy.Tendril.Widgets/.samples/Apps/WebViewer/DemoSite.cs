using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace WidgetSamples.Apps.WebViewer;

public sealed record DemoSiteAddress(string Base)
{
    public string Home => Base + "/";

    public string Page(string pathAndQuery) => Base + pathAndQuery;
}

public static class DemoSite
{
    public const string Prefix = "/__demo";

    public const string TasksPath = "/tasks";
    public const string SettingsPath = "/settings?tab=billing";
    public const string SlowPath = "/slow";
    public const string MissingPath = "/this-page-does-not-exist";
    public const string LongPath =
        "/reports/2026/q3/engineering/velocity-and-throughput-by-team-and-sprint"
        + "?range=last-90-days&group=team&include=archived&sort=-completed&columns=owner,estimate,actual,variance";

    private static readonly (string Id, string Title, string Owner, string Status)[] Tasks =
    [
        ("T-101", "Ship the review toolbar", "Mira", "active"),
        ("T-102", "Write release notes", "Jonas", "active"),
        ("T-103", "Fix flaky proxy test", "Mira", "done"),
        ("T-104", "Design the empty state", "Sasha", "active"),
        ("T-105", "Migrate settings storage", "Jonas", "done"),
        ("T-106", "Add keyboard shortcuts", "Sasha", "done"),
    ];

    public static void MapDemoSite(this WebApplication app)
    {
        app.MapGet(Prefix + "/{**path}", async (HttpContext context, string? path) =>
        {
            var route = "/" + (path ?? "").Trim('/');
            if (route == SlowPath) await Task.Delay(TimeSpan.FromSeconds(3), context.RequestAborted);

            var (status, html) = Render(route, context.Request.Query);
            context.Response.StatusCode = status;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(html, context.RequestAborted);
        });
    }

    private static (int Status, string Html) Render(string route, IQueryCollection query) => route switch
    {
        "/" => (200, Home()),
        "/tasks" => (200, TasksPage(query["filter"].ToString())),
        "/settings" => (200, SettingsPage(query["tab"].ToString())),
        "/slow" => (200, Simple("Slow page", "Finally here",
            "This page waits three seconds before answering, so the progress line under the toolbar has something to show.")),
        _ when route.StartsWith("/reports/") => (200, ReportsPage()),
        _ => (404, Simple("Not found", "There is nothing at " + WebUtility.HtmlEncode(route),
            "The address bar keeps the URL that was asked for, and Back returns to the previous page.")),
    };

    private static string Home() => Layout("Home", "/", $$"""
        <h1>Welcome back, Mira</h1>
        <p class="lead">Six tasks across three owners. Two are due this week.</p>
        <div class="grid">
          <div class="card" data-testid="card-open">
            <h3>Open tasks</h3>
            <p><strong>{{Tasks.Count(t => t.Status == "active")}}</strong> in progress</p>
          </div>
          <div class="card" data-testid="card-done">
            <h3>Completed</h3>
            <p><strong>{{Tasks.Count(t => t.Status == "done")}}</strong> this sprint</p>
          </div>
          <div class="card">
            <h3>Next review</h3>
            <p>Thursday, 14:00</p>
          </div>
        </div>
        <p class="actions">
          <a class="btn primary" href="{{Prefix}}/tasks" data-testid="cta-tasks">Open tasks</a>
          <a class="btn" href="{{Prefix}}/settings?tab=profile">Settings</a>
        </p>
        """);

    private static string TasksPage(string filter)
    {
        var rows = string.Join("\n", Tasks.Select(t => $"""
            <tr data-status="{t.Status}">
              <td><input type="checkbox" aria-label="Select {t.Id}" {(t.Status == "done" ? "checked" : "")}></td>
              <td class="mono">{t.Id}</td>
              <td>{WebUtility.HtmlEncode(t.Title)}</td>
              <td>{t.Owner}</td>
              <td><span class="pill {t.Status}">{(t.Status == "done" ? "Done" : "Active")}</span></td>
            </tr>
            """));

        string Tab(string key, string label) =>
            $"""<a href="{Prefix}/tasks{(key == "all" ? "" : "?filter=" + key)}" data-filter="{key}" {(Current(filter, key) ? "aria-current=\"page\"" : "")}>{label}</a>""";

        return Layout("Tasks", "/tasks", $$"""
            <h1>Tasks</h1>
            <p class="lead">The filter tabs change the URL without a page load, the way a client-side router does.</p>
            <div class="tabs" id="filters">
              {{Tab("all", "All")}}
              {{Tab("active", "Active")}}
              {{Tab("done", "Done")}}
            </div>
            <table data-testid="task-table">
              <thead><tr><th></th><th>Id</th><th>Task</th><th>Owner</th><th>Status</th></tr></thead>
              <tbody>
              {{rows}}
              </tbody>
            </table>
            <p class="actions">
              <button class="btn primary" type="button" data-testid="new-task">New task</button>
              <button class="btn" type="button">Export</button>
            </p>
            <script>
              (function () {
                var tabs = Array.prototype.slice.call(document.querySelectorAll('#filters a'));
                var rows = Array.prototype.slice.call(document.querySelectorAll('tbody tr'));
                function apply(filter) {
                  tabs.forEach(function (tab) {
                    if (tab.dataset.filter === filter) tab.setAttribute('aria-current', 'page');
                    else tab.removeAttribute('aria-current');
                  });
                  rows.forEach(function (row) {
                    row.style.display = filter === 'all' || row.dataset.status === filter ? '' : 'none';
                  });
                }
                function current() { return new URLSearchParams(location.search).get('filter') || 'all'; }
                tabs.forEach(function (tab) {
                  tab.addEventListener('click', function (event) {
                    event.preventDefault();
                    var filter = tab.dataset.filter;
                    history.pushState(null, '', '{{Prefix}}/tasks' + (filter === 'all' ? '' : '?filter=' + filter));
                    apply(filter);
                  });
                });
                window.addEventListener('popstate', function () { apply(current()); });
                apply(current());
              })();
            </script>
            """);
    }

    private static string SettingsPage(string tab)
    {
        var active = tab is "billing" or "notifications" ? tab : "profile";

        string Tab(string key, string label) =>
            $"""<a href="{Prefix}/settings?tab={key}" {(active == key ? "aria-current=\"page\"" : "")}>{label}</a>""";

        var form = active switch
        {
            "billing" => """
                <label>Company <input value="Demo Tasks AB" data-testid="company"></label>
                <label>Plan <select><option>Team</option><option>Business</option></select></label>
                <label>Invoice email <input type="email" value="invoices@example.test"></label>
                """,
            "notifications" => """
                <label><input type="checkbox" checked> Email me when a task is assigned to me</label>
                <label><input type="checkbox"> Weekly digest</label>
                <label><input type="checkbox" checked> Mention alerts</label>
                """,
            _ => """
                <label>Display name <input value="Mira Lindqvist" data-testid="display-name"></label>
                <label>Email <input type="email" value="mira@example.test"></label>
                <label>About <textarea rows="3" placeholder="Tell your team something about yourself"></textarea></label>
                """,
        };

        return Layout("Settings", "/settings", $$"""
            <h1>Settings</h1>
            <p class="lead">Each tab is its own URL, so a comment left on Billing stays on Billing.</p>
            <div class="tabs">
              {{Tab("profile", "Profile")}}
              {{Tab("billing", "Billing")}}
              {{Tab("notifications", "Notifications")}}
            </div>
            <form onsubmit="return false">
              {{form}}
              <p class="actions"><button class="btn primary" type="submit">Save changes</button></p>
            </form>
            """);
    }

    private static string ReportsPage()
    {
        var rows = string.Join("\n", new[]
        {
            ("Platform", "42", "39", "-3"),
            ("Frontend", "31", "35", "+4"),
            ("Infra", "18", "18", "0"),
        }.Select(r => $"<tr><td>{r.Item1}</td><td>{r.Item2}</td><td>{r.Item3}</td><td>{r.Item4}</td></tr>"));

        return Layout("Reports", "/reports", $$"""
            <h1>Velocity and throughput</h1>
            <p class="lead">Last 90 days, grouped by team, archived work included. The URL of this page is deliberately long.</p>
            <table>
              <thead><tr><th>Team</th><th>Estimate</th><th>Actual</th><th>Variance</th></tr></thead>
              <tbody>{{rows}}</tbody>
            </table>
            """);
    }

    private static string Simple(string title, string heading, string text) => Layout(title, "", $$"""
        <h1>{{WebUtility.HtmlEncode(heading)}}</h1>
        <p class="lead">{{WebUtility.HtmlEncode(text)}}</p>
        <p class="actions"><a class="btn" href="{{Prefix}}/">Back to home</a></p>
        """);

    private static bool Current(string filter, string key) => (string.IsNullOrEmpty(filter) ? "all" : filter) == key;

    private static string Layout(string title, string activePath, string body)
    {
        string Nav(string path, string label) =>
            $"""<a href="{Prefix}{path}" {(activePath == path.Split('?')[0] ? "aria-current=\"page\"" : "")}>{label}</a>""";

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{WebUtility.HtmlEncode(title)}} · Demo Tasks</title>
            <style>
              :root { --bg: #f6f7f9; --card: #ffffff; --fg: #1b1f24; --muted: #6b7280; --line: #e5e7eb; --accent: #2563eb; --accent-fg: #ffffff; }
              * { box-sizing: border-box; }
              body { margin: 0; font: 15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; background: var(--bg); color: var(--fg); }
              header { display: flex; align-items: center; gap: 24px; padding: 14px 28px; background: var(--card); border-bottom: 1px solid var(--line); }
              .brand { font-weight: 700; letter-spacing: -0.01em; }
              nav { display: flex; gap: 4px; flex-wrap: wrap; }
              nav a { padding: 6px 10px; border-radius: 6px; color: var(--muted); text-decoration: none; }
              nav a[aria-current="page"] { background: #eef2ff; color: var(--accent); }
              main { max-width: 960px; margin: 0 auto; padding: 28px; }
              h1 { margin: 0 0 6px; font-size: 26px; letter-spacing: -0.02em; }
              .lead { color: var(--muted); margin: 0 0 24px; }
              .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 16px; }
              .card { background: var(--card); border: 1px solid var(--line); border-radius: 10px; padding: 18px; }
              .card h3 { margin: 0 0 4px; font-size: 15px; }
              .card p { margin: 0; color: var(--muted); font-size: 14px; }
              .actions { display: flex; gap: 8px; margin-top: 24px; }
              .btn { display: inline-block; padding: 8px 14px; border-radius: 8px; border: 1px solid var(--line); background: var(--card); color: var(--fg); font: inherit; cursor: pointer; text-decoration: none; }
              .btn.primary { background: var(--accent); border-color: var(--accent); color: var(--accent-fg); }
              table { width: 100%; border-collapse: collapse; background: var(--card); border: 1px solid var(--line); border-radius: 10px; overflow: hidden; }
              th, td { text-align: left; padding: 10px 14px; border-bottom: 1px solid var(--line); font-size: 14px; }
              th { color: var(--muted); font-weight: 600; font-size: 12px; text-transform: uppercase; letter-spacing: 0.04em; }
              tr:last-child td { border-bottom: 0; }
              .mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 13px; color: var(--muted); }
              .pill { display: inline-block; padding: 2px 8px; border-radius: 999px; font-size: 12px; background: #eef2ff; color: var(--accent); }
              .pill.done { background: #ecfdf5; color: #047857; }
              .tabs { display: flex; gap: 4px; margin-bottom: 16px; }
              .tabs a { padding: 6px 12px; border-radius: 6px; color: var(--muted); text-decoration: none; border: 1px solid transparent; }
              .tabs a[aria-current="page"] { background: var(--card); border-color: var(--line); color: var(--fg); }
              form { display: grid; gap: 14px; max-width: 480px; }
              label { display: grid; gap: 6px; font-size: 13px; color: var(--muted); }
              label:has(input[type="checkbox"]) { grid-template-columns: auto 1fr; align-items: center; }
              input, select, textarea { font: inherit; padding: 8px 10px; border: 1px solid var(--line); border-radius: 8px; background: var(--card); color: var(--fg); }
              input[type="checkbox"] { padding: 0; width: 16px; height: 16px; }
              footer { padding: 24px 28px; color: var(--muted); font-size: 13px; }
              footer a { color: inherit; }
              @media (max-width: 640px) {
                header { flex-direction: column; align-items: flex-start; gap: 10px; padding: 14px 18px; }
                main { padding: 18px; }
                h1 { font-size: 22px; }
                .actions { flex-direction: column; }
              }
            </style>
            </head>
            <body>
            <header>
              <span class="brand" data-testid="brand">Demo Tasks</span>
              <nav aria-label="Primary">
                {{Nav("/", "Home")}}
                {{Nav("/tasks", "Tasks")}}
                {{Nav("/settings?tab=profile", "Settings")}}
                {{Nav(LongPath, "Reports")}}
              </nav>
            </header>
            <main>
            {{body}}
            </main>
            <footer>Demo site served by the widget samples · <a href="{{Prefix}}{{SlowPath}}">slow page</a> · <a href="{{Prefix}}{{MissingPath}}">missing page</a></footer>
            </body>
            </html>
            """;
    }
}
