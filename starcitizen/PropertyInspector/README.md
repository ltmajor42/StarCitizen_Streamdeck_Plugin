# Property Inspector assets

This folder is the source of truth for the Property Inspector pages that ship with the plugin. The compiled plugin copies everything in `PropertyInspector/` into the `.sdPlugin` output, so edits here are reflected in Stream Deck.

Key paths:
- Manifest references: `PropertyInspector/StarCitizen/*.html`
- Project includes: `<Content Include="PropertyInspector\\**\\*">`

If you add or update a Property Inspector, place the HTML/CSS/JS here so the build picks it up. Keeping a single folder prevents edits from landing in unused locations.
