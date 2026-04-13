using Godot;

/// <summary>
/// Procedural arena with industrial aesthetic.
/// Properly proportioned hazard stripes, detailed containers, scattered debris.
/// </summary>
public partial class ArenaLevel : Node2D
{
    [Export] public float ArenaWidth  = 2400f;
    [Export] public float ArenaHeight = 1800f;
    [Export] public float WallThick   = 60f;

    // Muted industrial palette
    static readonly Color CWall    = new(0.30f, 0.28f, 0.26f);
    static readonly Color CWallDk  = new(0.20f, 0.18f, 0.17f);
    static readonly Color CBlue    = new(0.22f, 0.38f, 0.58f);
    static readonly Color CRed     = new(0.58f, 0.22f, 0.20f);
    static readonly Color CGreen   = new(0.22f, 0.45f, 0.25f);
    static readonly Color COrange  = new(0.70f, 0.42f, 0.16f);
    static readonly Color CYellow  = new(0.62f, 0.58f, 0.18f);
    static readonly Color CRust    = new(0.48f, 0.28f, 0.16f);
    static readonly Color CHazardY = new(0.82f, 0.72f, 0.12f, 0.80f);
    static readonly Color CHazardB = new(0.12f, 0.12f, 0.10f, 0.80f);

    public override void _Ready()
    {
        _BuildWalls();
        _BuildContainers();
        _BuildDebris();
        _BuildGroundMarkings();
    }

    // ── Walls with properly-sized hazard stripes ─────────────────────────────
    private void _BuildWalls()
    {
        float hw = ArenaWidth  * 0.5f;
        float hh = ArenaHeight * 0.5f;
        float wt = WallThick;

        // Perimeter walls
        _MakeWall(new Vector2(0, -hh - wt * 0.5f),  new Vector2(ArenaWidth + wt * 2, wt));
        _MakeWall(new Vector2(0,  hh + wt * 0.5f),  new Vector2(ArenaWidth + wt * 2, wt));
        _MakeWall(new Vector2(-hw - wt * 0.5f, 0),  new Vector2(wt, ArenaHeight));
        _MakeWall(new Vector2( hw + wt * 0.5f, 0),  new Vector2(wt, ArenaHeight));

        // Hazard stripes: narrow band along inner face
        float sh = 6f;
        _AddHazardStripe(new Vector2(0,            -hh + sh * 0.5f), ArenaWidth, true);
        _AddHazardStripe(new Vector2(0,             hh - sh * 0.5f), ArenaWidth, true);
        _AddHazardStripe(new Vector2(-hw + sh * 0.5f, 0),            ArenaHeight, false);
        _AddHazardStripe(new Vector2( hw - sh * 0.5f, 0),            ArenaHeight, false);
    }

    private void _MakeWall(Vector2 pos, Vector2 size)
    {
        var body = new StaticBody2D { Position = pos, ZIndex = 1 };
        body.CollisionLayer = 2;
        body.CollisionMask = 1 | 4;
        AddChild(body);
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = size } });
        body.AddChild(_MakeOccluder(size));

        // Main face
        body.AddChild(_Rect(size, CWall));

        // Dark inner edge bevel
        float bevelH = Mathf.Min(4f, size.Y * 0.1f);
        body.AddChild(_Rect(new Vector2(size.X, bevelH), CWallDk,
            new Vector2(0, (size.Y - bevelH) * 0.5f)));
    }

    private void _AddHazardStripe(Vector2 pos, float length, bool horizontal)
    {
        // Narrow alternating yellow/black dashes — NOT rotated diagonal stripes
        float stripeH = 6f;
        float dashLen = 14f;
        float gapLen = 14f;
        int count = (int)(length / (dashLen + gapLen)) + 1;
        float totalLen = count * (dashLen + gapLen);
        float start = -totalLen * 0.5f;

        for (int i = 0; i < count; i++)
        {
            float offset = start + i * (dashLen + gapLen);

            // Yellow dash
            var dash = _Rect(
                horizontal ? new Vector2(dashLen, stripeH) : new Vector2(stripeH, dashLen),
                CHazardY
            );
            dash.Position = pos + (horizontal
                ? new Vector2(offset + dashLen * 0.5f, 0)
                : new Vector2(0, offset + dashLen * 0.5f));
            dash.ZIndex = 2;
            AddChild(dash);
        }
    }

    // ── Containers ───────────────────────────────────────────────────────────
    private void _BuildContainers()
    {
        var items = new (float x, float y, float w, float h, float r, Color c)[]
        {
            // Top-left cluster
            (-780, -600, 220, 48,  1.2f, CBlue),
            (-780, -545, 220, 48, -0.6f, CBlue),
            (-560, -670, 160, 48,  1.8f, CRed),

            // Top-right cluster
            ( 760, -640, 220, 48, -0.8f, COrange),
            ( 760, -585, 220, 48,  0.4f, COrange),
            ( 540, -620,  48, 160,  1.5f, CGreen),
            ( 980, -640, 160, 48, -1.8f, CRust),

            // Top-center chicane
            (   0, -740, 320, 48,  0f, CWall),
            (-310, -670, 200, 48,  0.8f, CBlue),
            ( 310, -670, 200, 48, -0.8f, CBlue),

            // Left alley
            (-860, -230, 200, 48,  0.4f, CRed),
            (-860, -174, 200, 48, -0.3f, CRed),
            (-860,  170, 200, 48,  0.8f, CBlue),
            (-860,  226, 200, 48, -0.2f, CBlue),

            // Right side stacks
            ( 910, -210,  48, 220,  1.2f, CGreen),
            ( 910,  160,  48, 220, -0.8f, CYellow),

            // Center angled pair
            (-245, -115, 200, 48,  18f, COrange),
            (-245,  135, 200, 48, -18f, COrange),

            // Center island
            (  90,    0,  48, 280,  0f, CWall),

            // Bottom-left
            (-680,  640, 220, 48, -1.2f, CYellow),
            (-445,  640, 220, 48,  1.5f, CYellow),
            (-680,  585, 220, 48,  0.6f, CRust),

            // Bottom-right
            ( 710,  620,  48, 200,  1.0f, CBlue),
            ( 770,  620,  48, 200, -0.6f, CRed),
            ( 930,  700, 180, 48,  1.8f, CGreen),

            // Bottom-center
            (   0,  740, 300, 48,  0f, CRed),
            (-290,  670,  48, 160,  1.2f, CYellow),
            ( 290,  670,  48, 160, -1.2f, CYellow),

            // Pillars
            (-490, -290,  48, 48,  8f, CWall),
            ( 490,  280,  48, 48, -6f, CWall),
            (-370,  430,  48, 48, 15f, CWall),
            ( 380, -410,  48, 48,-10f, CWall),
            ( -55,  300,  48, 48,  3f, CWall),

            // Corner deflectors
            (-1020, -780, 170, 48,  45f, CWall),
            ( 1020, -780, 170, 48, -45f, CWall),
            (-1020,  780, 170, 48, -45f, CWall),
            ( 1020,  780, 170, 48,  45f, CWall),
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
            ZIndex = 1
        };
        body.CollisionLayer = 2;
        body.CollisionMask = 1 | 4;
        AddChild(body);

        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = size } });
        body.AddChild(_MakeOccluder(size));

        // Shadow (offset, behind)
        var shadow = _Rect(new Vector2(size.X + 4, size.Y + 4),
            new Color(0, 0, 0, 0.30f), new Vector2(3, 4));
        shadow.ZIndex = -1;
        body.AddChild(shadow);

        // Main face with slight gradient feel: lighter center, darker edges
        body.AddChild(_Rect(size, col));

        // Top/bottom corrugation bands
        Color band = col.Darkened(0.30f);
        float bh = Mathf.Min(8f, size.Y * 0.18f);
        body.AddChild(_Rect(new Vector2(size.X - 2, bh), band,
            new Vector2(0, -(size.Y - bh) * 0.5f)));
        body.AddChild(_Rect(new Vector2(size.X - 2, bh), band,
            new Vector2(0,  (size.Y - bh) * 0.5f)));

        // Corner castings (small squares at corners)
        Color corner = col.Darkened(0.45f);
        float cs = Mathf.Min(8f, Mathf.Min(size.X, size.Y) * 0.18f);
        float cx = (size.X - cs) * 0.5f;
        float cy = (size.Y - cs) * 0.5f;
        body.AddChild(_Rect(new Vector2(cs, cs), corner, new Vector2(-cx, -cy)));
        body.AddChild(_Rect(new Vector2(cs, cs), corner, new Vector2( cx, -cy)));
        body.AddChild(_Rect(new Vector2(cs, cs), corner, new Vector2(-cx,  cy)));
        body.AddChild(_Rect(new Vector2(cs, cs), corner, new Vector2( cx,  cy)));

        // Center line (rivet strip) on longer containers
        if (size.X > 100f)
        {
            body.AddChild(_Rect(new Vector2(size.X * 0.7f, 1.5f),
                col.Lightened(0.08f), Vector2.Zero));
        }
    }

    // ── Small debris ─────────────────────────────────────────────────────────
    private void _BuildDebris()
    {
        // Small square obstacles — bollards, crates, barrels
        var debris = new (float x, float y, float s, Color c)[]
        {
            (-620, -615, 12, CRust),
            ( 620, -580, 12, COrange),
            (-900, -100, 10, CYellow),
            (-900,  300, 12, CRed),
            (-680, -230, 10, CYellow),
            (-680,  170, 10, CYellow),
            (-200, -400, 14, CRust),
            ( 300,  400, 12, CWall),
            ( 450, -150, 10, COrange),
            ( 150,  550, 12, CRed),
            ( 800,  400, 12, CRust),
        };

        foreach (var (x, y, s, col) in debris)
        {
            var body = new StaticBody2D { Position = new Vector2(x, y), ZIndex = 1 };
            body.CollisionLayer = 2;
            body.CollisionMask = 1 | 4;
            AddChild(body);

            body.AddChild(new CollisionShape2D {
                Shape = new CircleShape2D { Radius = s * 0.5f }
            });
            // Debris is too small (10–14 px) to produce useful light shadows;
            // adding an occluder here would only risk trapping the headlight
            // source inside the polygon when the car drives close.

            // Simple visual: small dark square with lighter top
            body.AddChild(_Rect(new Vector2(s, s), col.Darkened(0.15f)));
            body.AddChild(_Rect(new Vector2(s * 0.7f, s * 0.4f),
                col.Lightened(0.08f), new Vector2(0, -s * 0.12f)));

            // Tiny shadow
            var sh = _Rect(new Vector2(s + 2, s + 2), new Color(0, 0, 0, 0.2f), new Vector2(1, 2));
            sh.ZIndex = -1;
            body.AddChild(sh);
        }
    }

    // ── Ground markings ──────────────────────────────────────────────────────
    private void _BuildGroundMarkings()
    {
        var runs = new (Vector2 a, Vector2 b)[]
        {
            (new(-900, -630), new(-660, -630)),
            (new(-660, -630), new(-660, -700)),
            (new( 650, -660), new( 900, -660)),
            (new(-820,  610), new(-320,  610)),
            (new(-820,  610), new(-820,  670)),
            (new( 670,  590), new( 820,  590)),
            (new( 820,  590), new( 820,  760)),
        };

        foreach (var (a, b) in runs)
        {
            var line = new Line2D
            {
                DefaultColor = new Color(0.50f, 0.45f, 0.12f, 0.30f),
                Width        = 2.5f,
                ZIndex       = -1
            };
            line.AddPoint(a);
            line.AddPoint(b);
            AddChild(line);
        }
    }

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

    /// <summary>
    /// Creates a LightOccluder2D whose polygon matches the given rectangle size,
    /// so PointLight2D shadows are blocked by this object.
    /// </summary>
    /// <summary>
    /// Creates a LightOccluder2D whose polygon is slightly smaller than the 
    /// collision size to prevent PointLight2D nodes from clipping inside and inverting shadows.
    /// </summary>
    private static LightOccluder2D _MakeOccluder(Vector2 size)
    {
        // Shrink the occluder by 2 pixels (1px padding on all sides)
        float hw = (size.X * 0.5f) - 1f;
        float hh = (size.Y * 0.5f) - 1f;
        
        return new LightOccluder2D
        {
            Occluder = new OccluderPolygon2D
            {
                // Explicitly tell Godot the winding order to prevent rendering glitches
                CullMode = OccluderPolygon2D.CullModeEnum.Clockwise,
                Polygon = new Vector2[]
                {
                    new(-hw, -hh), new(hw, -hh),
                    new( hw,  hh), new(-hw,  hh)
                }
            }
        };
    }
}