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

    // Enemy spawn definitions: (position, maxHealth, armor, tint, name)
    private static readonly (Vector2 pos, float hp, float armor, Color tint, string label)[] EnemyDefs =
    {
        // ── Standard scouts — low HP, no armor, flanking positions ──────────
        (new Vector2(-500f,  -350f), 80f,  0f,  new Color(0.85f, 0.85f, 0.85f), "Scout_NW"),
        (new Vector2( 500f,  -350f), 80f,  0f,  new Color(0.85f, 0.85f, 0.85f), "Scout_NE"),

        // ── Heavy bruisers — high HP, no armor, roaming mid-field ───────────
        (new Vector2(-600f,   200f), 250f, 0f,  new Color(0.80f, 0.28f, 0.22f), "Heavy_W"),
        (new Vector2( 600f,   200f), 250f, 0f,  new Color(0.80f, 0.28f, 0.22f), "Heavy_E"),

        // ── Armored sentinels — medium HP, high armor, choke points ─────────
        (new Vector2(   0f,  -550f), 150f, 20f, new Color(0.28f, 0.45f, 0.72f), "Sentinel_N"),
        (new Vector2(   0f,   550f), 150f, 20f, new Color(0.28f, 0.45f, 0.72f), "Sentinel_S"),

        // ── Elite veterans — high HP, moderate armor, near walls ─────────────
        (new Vector2(-750f,    50f), 320f, 12f, new Color(0.55f, 0.30f, 0.70f), "Elite_W"),
        (new Vector2( 750f,    50f), 320f, 12f, new Color(0.55f, 0.30f, 0.70f), "Elite_E"),
    };

    public override void _Ready()
    {
        _BuildWalls();
        _BuildContainers();
        _BuildGroundMarkings();
        _BuildGlobalLight();
        _SpawnTestEnemies();
    }

    // ── Enemy spawning ────────────────────────────────────────────────────────

    private void _SpawnTestEnemies()
    {
        var scene = ResourceLoader.Load<PackedScene>("res://scenes/dummy_enemy.tscn");
        if (scene == null)
        {
            GD.PushWarning("ArenaLevel: could not load dummy_enemy.tscn");
            return;
        }

        foreach (var (pos, hp, armor, tint, label) in EnemyDefs)
        {
            var enemy = scene.Instantiate<DummyEnemy>();
            // Set stats BEFORE AddChild so _Ready reads the correct values
            enemy.MaxHealth  = hp;
            enemy.Armor      = armor;
            enemy.Name       = label;
            AddChild(enemy);
            enemy.GlobalPosition = pos;
            enemy.Modulate       = tint;
        }
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

        // Hazard stripes — all four wall faces rendered as one StripeDrawer node
        // instead of ~344 individual Polygon2D nodes (one per dash).
        float sh = 6f;
        var stripes = new StripeDrawer { ZIndex = 2 };
        AddChild(stripes);
        stripes.AddStripe(new Vector2(0,            -hh + sh * 0.5f), ArenaWidth,  horizontal: true);
        stripes.AddStripe(new Vector2(0,             hh - sh * 0.5f), ArenaWidth,  horizontal: true);
        stripes.AddStripe(new Vector2(-hw + sh * 0.5f, 0),            ArenaHeight, horizontal: false);
        stripes.AddStripe(new Vector2( hw - sh * 0.5f, 0),            ArenaHeight, horizontal: false);
        stripes.Commit(CHazardY);
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
    /// Creates a LightOccluder2D whose polygon is slightly smaller than the
    /// collision size to prevent PointLight2D nodes from clipping inside and
    /// inverting shadows.
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

    // ── Global Lighting ──────────────────────────────────────────────────────
// ── Global Lighting ──────────────────────────────────────────────────────
    private void _BuildGlobalLight()
    {
        var sun = new DirectionalLight2D
        {
            Color  = new Color(0.45f, 0.55f, 0.75f), 
            Energy = 0.85f, 
            Height = 60f,   

            // CHANGE THIS TO FALSE
            // This prevents the arena walls from acting like a giant roof!
            ShadowEnabled = false, 
        };

        sun.Rotation = Mathf.DegToRad(-45f);
        AddChild(sun);
    }
}