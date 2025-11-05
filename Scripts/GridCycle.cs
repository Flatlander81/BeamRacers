using Godot;

/// <summary>
/// Base class for grid-snapped movement used by both Player and Enemy cycles.
/// Provides shared grid alignment, snapping, direction, and rotation logic.
/// </summary>
public abstract partial class GridCycle : CharacterBody2D
{
	// ========== GRID PARAMETERS ==========
	public int GridSize = 50;

	// ========== MOVEMENT STATE ==========
	protected int _currentDirection = 0; // 0=right, 1=down, 2=left, 3=up
	protected int? _queuedDirection = null;

	// ========== GRID MOVEMENT METHODS ==========

	/// <summary>
	/// Checks if cycle is aligned to grid for turning
	/// </summary>
	protected bool IsAlignedToGrid()
	{
		if (_currentDirection == 0 || _currentDirection == 2) // Horizontal
		{
			float remainder = Mathf.Abs(GlobalPosition.X) % GridSize;
			return remainder < 2.0f || remainder > (GridSize - 2.0f);
		}
		else // Vertical
		{
			float remainder = Mathf.Abs(GlobalPosition.Y) % GridSize;
			return remainder < 2.0f || remainder > (GridSize - 2.0f);
		}
	}

	/// <summary>
	/// Snaps cycle position to nearest grid line
	/// </summary>
	protected void SnapToGrid()
	{
		Vector2 snappedPos = GlobalPosition;
		snappedPos.X = Mathf.Round(snappedPos.X / GridSize) * GridSize;
		snappedPos.Y = Mathf.Round(snappedPos.Y / GridSize) * GridSize;
		GlobalPosition = snappedPos;
	}

	/// <summary>
	/// Gets the movement direction vector based on current direction
	/// </summary>
	protected Vector2 GetDirectionVector()
	{
		return GetDirectionVector(_currentDirection);
	}

	/// <summary>
	/// Gets the movement direction vector based on a specific direction index
	/// </summary>
	protected Vector2 GetDirectionVector(int direction)
	{
		return direction switch
		{
			0 => Vector2.Right,
			1 => Vector2.Down,
			2 => Vector2.Left,
			3 => Vector2.Up,
			_ => Vector2.Right
		};
	}

	/// <summary>
	/// Updates the visual rotation to match the current direction
	/// </summary>
	protected void UpdateRotationFromDirection()
	{
		Rotation = _currentDirection * Mathf.Pi / 2.0f;
	}

	// ========== VISUAL GENERATION ==========

	/// <summary>
	/// Creates the wedge-shaped cycle geometry with the specified color
	/// </summary>
	protected void GenerateWedgeGeometry(Polygon2D bodyPolygon, Line2D outlineLine, Color color)
	{
		Vector2[] wedgeVertices = new Vector2[]
		{
			new Vector2(20, 0),
			new Vector2(-10, -8),
			new Vector2(-5, -8),
			new Vector2(-5, 8),
			new Vector2(-10, 8)
		};

		bodyPolygon.Polygon = wedgeVertices;
		bodyPolygon.Color = new Color(color.R, color.G, color.B, 0.3f);

		var material = new CanvasItemMaterial();
		material.BlendMode = CanvasItemMaterial.BlendModeEnum.Add;
		bodyPolygon.Material = material;

		outlineLine.Points = wedgeVertices;
		outlineLine.AddPoint(wedgeVertices[0]);
		outlineLine.DefaultColor = color;
		outlineLine.Width = 2.0f;
		outlineLine.Closed = true;
	}

	// ========== TRAIL MANAGEMENT ==========

	/// <summary>
	/// Initializes trail renderer by removing old nodes and moving to world space
	/// </summary>
	protected void InitializeTrailRenderer(Node2D trailRenderer)
	{
		// Remove old trail nodes (if any)
		var oldTrailLine = GetNodeOrNull<Line2D>("TrailRenderer/TrailLine");
		if (oldTrailLine != null)
		{
			oldTrailLine.QueueFree();
		}

		// Move trail renderer to world space for proper rendering
		RemoveChild(trailRenderer);
		GetParent().AddChild(trailRenderer);
		trailRenderer.GlobalPosition = Vector2.Zero;

		GD.Print($"[{GetType().Name}] Trail renderer moved to world space");
	}

	/// <summary>
	/// Registers cycle with TrailManager using the specified color and trail type
	/// </summary>
	protected void RegisterWithTrailManager(Color trailColor, Node2D trailRenderer, CellOccupant trailType, string cycleTypeName)
	{
		if (TrailManager.Instance != null)
		{
			TrailManager.Instance.RegisterCycle(this, trailColor, trailRenderer, trailType);
			GD.Print($"[{cycleTypeName}] ✓ Registered with TrailManager");
		}
		else
		{
			GD.PrintErr($"[{cycleTypeName}] ERROR: TrailManager not found!");
		}
	}
}
