# Basic Demo

Open BasicDemo.unity in a Linear-color-space URP project with a 2D Renderer. The camera uses renderer index 1; select your own 2D Renderer in its Inspector if your project uses another index.

- Left: neutral original sprites.
- Middle: exposure, tint and saturation.
- Right: blur and a SpriteMask inside the same SortingGroup.

Select a group and toggle the component's header checkbox to compare. The scene has a Global 2D Light and uses the included SpriteLit material. Disc.png is generated original artwork by marmunc, MIT.

Pipeline.asset and its two renderer assets are an optional standalone demo configuration (index 0 Universal Renderer, index 1 2D Renderer). Keep your own game's pipeline settings when importing the sample.
