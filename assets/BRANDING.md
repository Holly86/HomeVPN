# HomeVPN mark

Original vector mark: an emerald shield with a house-shaped cutout and an open
central doorway/tunnel. SVG and WPF geometry share the same paths. It is not the
WireGuard mark. `scripts/Build-Branding.ps1` deterministically renders 16, 24, 32,
48 and 256 pixel PNGs and a multi-resolution ICO from the vector geometry.

The built-in Imagegen tool was used for conceptual exploration with this prompt:

> Use case: logo-brand. Create one concept sheet with three original minimalist
> HomeVPN Windows application icon concepts: abstract home within protective
> shield, subtle connecting tunnel line, emerald green main accent, simple strong
> silhouette readable at 16 pixels, white background, flat vector-friendly
> geometry, no text, no WireGuard logo, no clipart, no shadows or gradients. This
> is conceptual reference for subsequent original SVG/XAML implementation.

The generated raster had decorative effects, so the production asset was
redrawn as simple original vector geometry. The raster is concept-only and is
not a runtime dependency. App, EXE, taskbar, tray and setup use HomeVPN.ico.
