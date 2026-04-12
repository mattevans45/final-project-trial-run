using Godot;

/// <summary>
/// Procedural arena / shipping-yard level.
/// Builds outer boundary walls (hard collision) and shipping-container
/// obstacles entirely from code — no external assets needed.
///
/// Layout is a 2400 × 1800 walled compound with container clusters, alley
/// corridors, central islands, and corner deflectors to keep the car from
/// wedging into 90° corners.
/// </summary>
public partial class ArenaLevel : Node2D
{
    [Export] public float ArenaWidth  = 2400f;
    [Export] public float ArenaHeight = 1800f;
    [Export] public float WallThick   = 60f;

    // ── Palette ───────────────────────────────────────────────────────────────
    static readonly Color CConcrete = new(0.40f, 0.38f, 0.36f);
    static readonly Color CBlue     = new(0.18f, 0.40f, 0.72f);
    static readonly Color CRed      = new(0.72f, 0.20f, 0.20f);
    static readonly Color CGreen    = new(0.20f, 0.58f, 0.26f);
    static readonly Color COrange   = new(0.88f, 0.50f, 0.16f);
    static readonly Color CYellow   = new(0.80f, 0.74f, 0.16f);

    public override void _Ready()
    {
        _BuildWalls();
        _BuildContainers();
        _BuildGroundMarkings();
    }

    // ── Outer boundary ────────────────────────────────────────────────────────
    private void _BuildWalls()
    {
        float hw = ArenaWidth  * 0.5f;
        float hh = ArenaHeight * 0.5f;
        float wt = WallThick;

        // Four perimeter walls (extend to cover corners)
        _MakeWall(new Vector2(0,             -hh - wt * 0.5f), new Vector2(ArenaWidth + wt * 2, wt));
        _MakeWall(new Vector2(0,              hh + wt * 0.5f), new Vector2(ArenaWidth + wt * 2, wt));
        _MakeWall(new Vector2(-hw - wt * 0.5f, 0),             new Vector2(wt, ArenaHeight));
        _MakeWall(new Vector2( hw + wt * 0.5f, 0),             new Vector2(wt, ArenaHeight));

        // Yellow hazard stripe along inner face of each wall
        float s = 10f;
        _AddStripe(new Vector2(0,          -hh + s * 0.5f), new Vector2(ArenaWidth, s));
        _AddStripe(new Vector2(0,           hh - s * 0.5f), new Vector2(ArenaWidth, s));
        _AddStripe(new Vector2(-hw + s * 0.5f, 0),          new Vector2(s, ArenaHeight));
        _AddStripe(new Vector2( hw - s * 0.5f, 0),          new Vector2(s, ArenaHeight));
    }

    private void _MakeWall(Vector2 pos, Vector2 size)
    {
        var body = new StaticBody2D { Position = pos, ZIndex = 1 };
        AddChild(body);
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = size } });
        body.AddChild(_Rect(size, CConcrete));
    }

    private void _AddStripe(Vector2 pos, Vector2 size)
    {
        var p = _Rect(size, new Color(0.90f, 0.80f, 0.10f, 0.75f));
        p.Position = pos;
        p.ZIndex   = 2;
        AddChild(p);
    }

    // ── Container / obstacle catalogue ───────────────────────────────────────
    private void _BuildContainers()
    {
        // (centerX, centerY, width, height, rotationDegrees, color)
        var items = new (float x, float y, float w, float h, float r, Color c)[]
        {
            // ── Top-left cluster ──────────────────────────────────────────────
            (-780, -600, 220, 48,   0, CBlue),
            (-780, -545, 220, 48,   0, CBlue),
            (-560, -670, 160, 48,   0, CRed),

            // ── Top-right cluster ─────────────────────────────────────────────
            ( 760, -640, 220, 48,   0, COrange),
            ( 760, -585, 220, 48,   0, COrange),
            ( 540, -620,  48, 160,  0, CGreen),
            ( 980, -640, 160, 48,   0, CConcrete),

            // ── Top-center barrier (creates a chicane) ────────────────────────
            (   0, -740, 320, 48,   0, CConcrete),
            (-310, -670, 200, 48,   0, CBlue),
            ( 310, -670, 200, 48,   0, CBlue),

            // ── Left alley (tight corridor with left wall) ────────────────────
            (-860, -230, 200, 48,   0, CRed),
            (-860, -174, 200, 48,   0, CRed),
            (-860,  170, 200, 48,   0, CBlue),
            (-860,  226, 200, 48,   0, CBlue),

            // ── Right side vertical stacks ────────────────────────────────────
            ( 910, -210,  48, 220,  0, CGreen),
            ( 910,  160,  48, 220,  0, CYellow),

            // ── Center-left angled pair (forces S-curve) ──────────────────────
            (-245, -115, 200, 48,  18, COrange),
            (-245,  135, 200, 48, -18, COrange),

            // ── Center island (splits the middle of the arena) ────────────────
            (  90,    0,  48, 280,  0, CConcrete),

            // ── Bottom-left cluster ───────────────────────────────────────────
            (-680,  640, 220, 48,   0, CYellow),
            (-445,  640, 220, 48,   0, CYellow),
            (-680,  585, 220, 48,   0, COrange),

            // ── Bottom-right cluster ──────────────────────────────────────────
            ( 710,  620,  48, 200,  0, CBlue),
            ( 770,  620,  48, 200,  0, CRed),
            ( 930,  700, 180, 48,   0, CGreen),

            // ── Bottom-center barrier ─────────────────────────────────────────
            (   0,  740, 300, 48,   0, CRed),
            (-290,  670,  48, 160,  0, CYellow),
            ( 290,  670,  48, 160,  0, CYellow),

            // ── Scatter pillars (small, harder to dodge at speed) ─────────────
            (-490, -290,  52, 52,   0, CConcrete),
            ( 490,  280,  52, 52,   0, CConcrete),
            (-370,  430,  52, 52,   0, CConcrete),
            ( 380, -410,  52, 52,   0, CConcrete),
            ( -55,  300,  52, 52,   0, CConcrete),

            // ── Corner deflectors (45°, prevent car wedging in corners) ───────
            (-1020, -780, 170, 48,  45, CConcrete),
            ( 1020, -780, 170, 48, -45, CConcrete),
            (-1020,  780, 170, 48, -45, CConcrete),
            ( 1020,  780, 170, 48,  45, CConcrete),
        };

        foreach (var (x, y, w, h, rot, col) in items)
            _MakeContainer(new Vector2(x, y), new Vector2(w, h), rot, col);
    }

    private void _MakeContainer(Vector2 pos, Vector2 size, float rotDeg, Color col)
    {
        var body = new StaticBody2D
        {
            Position = pos,
            Rotation = Mathf.DegToRad(rotDeg),
            ZIndex   = 1
        };
        AddChild(body);

        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = size } });

        // Drop shadow (slightly offset, behind face)
        var shadow = _Rect(new Vector2(size.X + 6, size.Y + 6), new Color(0, 0, 0, 0.35f),
                           new Vector2(4, 5));
        shadow.ZIndex = -1;
        body.AddChild(shadow);

        // Main face
        body.AddChild(_Rect(size, col));

        // Corrugation stripes at top and bottom edges
        Color dark = col.Darkened(0.40f);
        float sh   = Mathf.Min(10f, size.Y * 0.22f);
        body.AddChild(_Rect(new Vector2(size.X, sh), dark,
                            new Vector2(0, -(size.Y - sh) * 0.5f)));
        body.AddChild(_Rect(new Vector2(size.X, sh), dark,
                            new Vector2(0,  (size.Y - sh) * 0.5f)));

        // Corner casting squares
        Color darker = col.Darkened(0.55f);
        float cm = Mathf.Min(10f, size.Y * 0.22f);
        float ex = (size.X - cm) * 0.5f;
        float ey = (size.Y - cm) * 0.5f;
        foreach (float cx in new[] { -ex, ex })
        foreach (float cy in new[] { -ey, ey })
            body.AddChild(_Rect(new Vector2(cm, cm), darker, new Vector2(cx, cy)));
    }

    // ── Ground markings (yellow edge lines around container clusters) ─────────
    private void _BuildGroundMarkings()
    {
        // Simple approach: a few yellow Line2D runs along container row edges
        var runs = new (Vector2 a, Vector2 b)[]
        {
            // Top-left cluster border
            (new(-900, -630), new(-660, -630)),
            (new(-660, -630), new(-660, -700)),
            // Top-right cluster border
            (new( 650, -660), new( 900, -660)),
            // Bottom-left cluster border
            (new(-820,  610), new(-320,  610)),
            (new(-820,  610), new(-820,  670)),
            // Bottom-right cluster border
            (new( 670,  590), new( 820,  590)),
            (new( 820,  590), new( 820,  760)),
        };

        foreach (var (a, b) in runs)
        {
            var line = new Line2D
            {
                DefaultColor = new Color(0.88f, 0.80f, 0.12f, 0.65f),
                Width        = 4f,
                ZIndex       = -1
            };
            line.AddPoint(a);
            line.AddPoint(b);
            AddChild(line);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static Polygon2D _Rect(Vector2 size, Color color, Vector2 offset = default)
    {
        float hw = size.X * 0.5f, hh = size.Y * 0.5f;
        return new Polygon2D
        {
            Polygon  = new[] { new Vector2(-hw,-hh), new Vector2(hw,-hh),
                               new Vector2( hw, hh), new Vector2(-hw, hh) },
            Color    = color,
            Position = offset
        };
    }
}
