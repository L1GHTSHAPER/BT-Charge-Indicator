# BT Charge Indicator visual identity

This icon follows the LightShaper identity: black and white, purple accents, flat shapes, and rounded strokes.

- Background: `#151519`; outline: `#303036`; symbols: `#FFFFFF`; accent: `#A855F7`.
- `icon.svg`: editable application icon, combining a battery and Bluetooth symbol.
- `logo.png`: 1024 × 1024 application logo for documentation and reuse.
- `app.ico`: Windows icon with 16, 20, 24, 32, 40, 48, 64, 96, 128 and 256 pixel images.
- `lightshaper-logo.svg` / `.png`: the original standalone LightShaper mark, preserved from the approved source.
- `lightshaper-wordmark.svg` / `.png`: the original LightShaper wordmark for documentation.

The application icon uses the same clean design at every size, without a corner signature.

Regenerate PNG and ICO assets with Node.js and `sharp`:

```powershell
node .\tools\render-branding.cjs
# Or provide the installed sharp module explicitly:
node .\tools\render-branding.cjs --sharp <module-path>
```

All PNG and ICO application assets are rendered directly from the SVG source.
