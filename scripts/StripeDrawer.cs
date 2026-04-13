using Godot;

/// <summary>
/// Renders an arbitrary number of dashed-stripe lines as a single canvas item,
/// replacing the original pattern of spawning one Polygon2D per dash.
///
/// Why this matters:
///   Four hazard stripes at ArenaWidth=2400 / ArenaHeight=1800 produce
///   ~344 individual Polygon2D nodes when drawn the naive way, each with its
///   own draw call. Consolidating them into one Node2D with _Draw() reduces
///   that to a single canvas item — Godot batches same-color DrawRect calls
///   on the same canvas item into one GPU command.
///
/// Usage (from ArenaLevel._BuildWalls):
///   var drawer = new StripeDrawer { ZIndex = 2 };
///   AddChild(drawer);
///   drawer.AddStripe(center, length, horizontal);
///   // ... add more stripes ...
///   drawer.Commit(color);  // must be called once after all AddStripe calls
/// </summary>
public partial class StripeDrawer : Node2D
{
    private struct StripeDesc
    {
        public Vector2 Center;
        public float   Length;
        public bool    Horizontal;
    }

    private const float StripeH = 6f;
    private const float DashLen = 14f;
    private const float GapLen  = 14f;

    private System.Collections.Generic.List<StripeDesc> _stripes = new();
    private Color _color;

    /// <summary>Queue a stripe. Call before <see cref="Commit"/>.</summary>
    public void AddStripe(Vector2 center, float length, bool horizontal)
    {
        _stripes.Add(new StripeDesc
        {
            Center     = center,
            Length     = length,
            Horizontal = horizontal,
        });
    }

    /// <summary>Finalise the color and trigger the initial draw.</summary>
    public void Commit(Color color)
    {
        _color = color;
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var s in _stripes)
        {
            int   count    = (int)(s.Length / (DashLen + GapLen)) + 1;
            float totalLen = count * (DashLen + GapLen);
            float start    = -totalLen * 0.5f;

            for (int i = 0; i < count; i++)
            {
                float off = start + i * (DashLen + GapLen);

                Rect2 rect = s.Horizontal
                    ? new Rect2(s.Center.X + off,            s.Center.Y - StripeH * 0.5f, DashLen, StripeH)
                    : new Rect2(s.Center.X - StripeH * 0.5f, s.Center.Y + off,            StripeH, DashLen);

                DrawRect(rect, _color);
            }
        }
    }
}
