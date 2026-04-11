using Godot;

public partial class Vehicle : CharacterBody2D
{
    private const float Speed = 100.0f;
    private const int FramesPerDir = 7;
    private const float Fps = 12.0f;

    // Sector → spritesheet row
    // Godot angle() sectors: 0=E, 1=SE, 2=S, 3=SW, 4=W, 5=NW, 6=N, 7=NE
    // Remapped to sheet rows:  E=7, SE=0, S=1, SW=2, W=3, NW=4, N=5, NE=6
    private static readonly int[] SectorToRow = { 7, 0, 1, 2, 3, 4, 5, 6 };

    private Sprite2D _sprite;
    private int _animFrame = 0;
    private float _animTimer = 0.0f;
    private int _lastDirRow = 1; // default: facing South

    public override void _Ready()
    {
        _sprite = GetNode<Sprite2D>("Sprite2D");
        _sprite.Hframes = FramesPerDir;
        _sprite.Vframes = 8;
    }

    public override void _PhysicsProcess(double delta)
    {
        var inputDir = new Vector2(
            Input.GetAxis("ui_left", "ui_right"),
            Input.GetAxis("ui_up", "ui_down")
        );

        if (inputDir != Vector2.Zero)
        {
            Velocity = inputDir.Normalized() * Speed;
            _lastDirRow = GetDirRow(inputDir);

            _animTimer += (float)delta;
            if (_animTimer >= 1.0f / Fps)
            {
                _animTimer = 0.0f;
                _animFrame = (_animFrame + 1) % FramesPerDir;
            }
        }
        else
        {
            Velocity = Vector2.Zero;
            _animFrame = 0;
            _animTimer = 0.0f;
        }

        _sprite.Frame = _lastDirRow * FramesPerDir + _animFrame;
        MoveAndSlide();
    }

    private int GetDirRow(Vector2 dir)
    {
        float angle = Mathf.RadToDeg(dir.Angle());
        if (angle < 0)
            angle += 360.0f;

        int sector = (int)(((angle + 22.5f) % 360.0f) / 45.0f);
        return SectorToRow[sector];
    }
}