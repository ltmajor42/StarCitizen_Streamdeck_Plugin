## Property Inspector assets

This folder is the on-disk location referenced by both the plugin manifest and the project file for Property Inspector pages:

- `manifest.json` paths such as `PropertyInspector/StarCitizen/ActionDelay.html`
- `starcitizen.csproj` `<Content Include="PropertyInspector\StarCitizen\*.html">`

Only `ActionDelay.html` lives here in source form today, but the folder itself is required so the manifest paths remain valid when additional Property Inspector pages are emitted. Legacy template files once lived at the repo root; they have been removed because Property Inspectors are maintained directly in this folder now.
