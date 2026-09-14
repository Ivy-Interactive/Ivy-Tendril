using Ivy.Tendril.Wireframe.Assets;
using Ivy.Tendril.Wireframe.Hosting;
using Ivy.Tendril.Wireframe.Project;

namespace WidgetSamples.Apps.DraftMarkdown;

/// <summary>
/// A wireframe for the Wireframes sample to show, served the way Tendril serves a plan's: a
/// scaffolded project on disk, built by esbuild and hot reloaded, behind a
/// <see cref="WireframeHost"/> on the sample app's own origin. Plan "1" is the only plan.
/// </summary>
internal static class WireframeSamples
{
    public const string BaseUrl = "/__wireframes/1/";

    public static string Root { get; } =
        Path.Combine(Path.GetTempPath(), "tendril-widget-samples", "wireframes");

    public static WireframeHost CreateHost()
    {
        var checkout = WireframeProject.At(Path.Combine(Root, "checkout"));
        new ProjectScaffolder(AssetCatalog.Default).Scaffold(checkout);
        File.WriteAllText(Path.Combine(checkout.SourceDir, "App.tsx"), CheckoutApp);

        return new WireframeHost(
            AssetCatalog.Default,
            (scope, name) => scope == "1" ? Path.Combine(Root, name) : null);
    }

    private const string CheckoutApp =
        """
        import { Badge, Button, Card } from "tendril-wireframes";

        export default function App() {
          return (
            <div className="p-6 grid grid-cols-3 gap-6">
              <div className="col-span-2 flex flex-col gap-4">
                <Card title="Payment">
                  <div className="flex flex-col gap-3">
                    <Badge title="Step 2 of 3" />
                    <Button title="Visa ending 4242" variant="Outline" />
                    <Button title="Add a new card" variant="Ghost" />
                  </div>
                </Card>
              </div>
              <Card title="Order summary">
                <div className="flex flex-col gap-3">
                  <span>3 items</span>
                  <Button title="Pay now" variant="Primary" />
                </div>
              </Card>
            </div>
          );
        }

        """;
}
