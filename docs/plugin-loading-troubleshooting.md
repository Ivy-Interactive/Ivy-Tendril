# Plugin Loading Troubleshooting

## Source-referenced plugin fails to load: missing dependency DLLs

If you reference your plugin project as a path in `<TENDRIL_HOME>/plugins/plugin-references.yaml`
(see "Option A: Plugin References File" in the plugin developer guide), the plugin can fail to load
when a NuGet dependency it needs is not copied next to the built plugin assembly.

**Symptom:** Tendril logs a `Could not load file or assembly` error from `PluginLoader`:

```text
Could not load file or assembly 'SomeDependency, Version=1.0.0.0, ...'
```

**Cause:** Your plugin's `AssemblyLoadContext` resolves dependencies from the plugin's own output
folder. Only the shared host assemblies (`Ivy`, `Ivy.Plugin.Abstractions`,
`Ivy.Tendril.Plugin.Abstractions`, `Ivy.Tendril.Plugin.Extended.Abstractions`) are provided by the
host. Every other assembly your plugin references must ship alongside the plugin DLL.

**How to fix:**

- Confirm the dependency DLL is present in the plugin project's `bin/<Configuration>/net10.0/`
  output directory, next to the plugin assembly.
- If it is missing, make sure the `PackageReference` is a direct reference of the plugin project,
  and that its assets are not marked `PrivateAssets="all"` or `ExcludeAssets="runtime"`.
- Rebuild the plugin project and let Tendril reload it.
