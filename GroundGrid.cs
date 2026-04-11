using Godot;

/// <summary>
/// Procedural ground that gives a frame of reference for movement.
/// Draws a large grid of alternating asphalt-colored tiles.
/// Add to your main scene — it positions itself at Z-index -10 behind everything.
/// </summary>
public partial class GroundGrid : Node2D
{
    [Export] public int TileSize    = 128;
    [Export] public int GridExtent  = 40;   // tiles in each direction from origin
    [Export] public Color ColorA    = new Color("2a2a2e");
    [Export] public Color ColorB    = new Color("232327");
    [Export] public Color LineColor = new Color("3a3a40");
    [Export] public float LineWidth = 1f;

    /// <summary>Road lane markings every N tiles for extra reference.</summary>
    [Export] public int   LaneInterval  = 4;
    [Export] public Color LaneColor     = new Color("4a4a3a");
    [Export] public float LaneWidth     = 2f;

    public override void _Ready()
    {
        ZIndex = -10;
    }

    public override void _Draw()
    {
        int half = GridExtent;

        // Checkerboard tiles
        for (int x = -half; x < half; x++)
        {
            for (int y = -half; y < half; y++)
            {
                Color col = (x + y) % 2 == 0 ? ColorA : ColorB;
                Rect2 rect = new Rect2(x * TileSize, y * TileSize, TileSize, TileSize);
                DrawRect(rect, col, true);
            }
        }

        // Grid lines
        float totalSize = half * 2 * TileSize;
        float start = -half * TileSize;

        for (int i = -half; i <= half; i++)
        {
            float pos = i * TileSize;
            // Vertical
            DrawLine(new Vector2(pos, start), new Vector2(pos, start + totalSize), LineColor, LineWidth);
            // Horizontal
            DrawLine(new Vector2(start, pos), new Vector2(start + totalSize, pos), LineColor, LineWidth);
        }

        // Lane markings (dashed lines every LaneInterval tiles)
        if (LaneInterval > 0)
        {
            int dashLen = TileSize / 3;
            int gapLen  = TileSize / 3;

            for (int i = -half; i <= half; i += LaneInterval)
            {
                float pos = i * TileSize;

                // Draw dashed lines
                for (float d = start; d < start + totalSize; d += dashLen + gapLen)
                {
                    float end = Mathf.Min(d + dashLen, start + totalSize);
                    // Vertical dashed
                    DrawLine(new Vector2(pos, d), new Vector2(pos, end), LaneColor, LaneWidth);
                    // Horizontal dashed
                    DrawLine(new Vector2(d, pos), new Vector2(end, pos), LaneColor, LaneWidth);
                }
            }
        }
    }
}