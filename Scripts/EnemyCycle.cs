using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Enemy Light Cycle with grid-snapped movement, chase AI, and trail generation.
/// Movement system identical to Player - 90-degree turns at grid lines only.
/// </summary>
public partial class EnemyCycle : CharacterBody2D
{
	// ========== SIGNALS ==========
	[Signal]
	public delegate void OnEnemyDiedEventHandler();

	// ========== EXPORTS ==========
	[Export] public float MoveSpeed = 250.0f;

	// ========== NODE REFERENCES ==========
	private Polygon2D _bodyPolygon;
	private Line2D _outlineLine;
	private Node2D _trailRenderer;
	private Area2D _trailCollision;

	// ========== GRID PARAMETERS ==========
	public int GridSize = 50;
	public Rect2 ArenaBounds = new Rect2(-800, -450, 1600, 900);

	// ========== MOVEMENT STATE (Grid-snapped like Player) ==========
	private int _currentDirection = 0; // 0=right, 1=down, 2=left, 3=up
	private int? _queuedDirection = null;

	// ========== AI STATE ==========
	private Vector2 _targetPosition = Vector2.Zero;
	private float _targetUpdateTimer = 0.0f;
	private const float TARGET_UPDATE_INTERVAL = 0.2f;
	private Player _player;
	private float _nextDecisionTime = 0.0f;
	private const float DECISION_INTERVAL = 0.3f;

	// ========== TRAIL STATE ==========
	private List<TrailWall> _walls = new List<TrailWall>();
	private Vector2 _currentWallStart = Vector2.Zero;
	private bool _hasWallStart = false;
	private int _lastWallIndex = -1;
	private const float TRAIL_WIDTH = 4.0f;

	// ========== TRAIL AVOIDANCE ==========
	private const float AVOIDANCE_CHECK_DISTANCE = 150.0f;

	// ========== INITIALIZATION ==========
	public override void _Ready()
	{
		GD.Print("[EnemyCycle] Initializing enemy cycle...");

		// Get node references
		_bodyPolygon = GetNode<Polygon2D>("Sprite/Body");
		_outlineLine = GetNode<Line2D>("Sprite/Outline");
		_trailRenderer = GetNode<Node2D>("TrailRenderer");
		_trailCollision = GetNode<Area2D>("TrailRenderer/TrailCollision");

		// Remove old trail nodes (we'll create them dynamically)
		var oldTrailLine = GetNodeOrNull<Line2D>("TrailRenderer/TrailLine");
		if (oldTrailLine != null)
		{
			oldTrailLine.QueueFree();
		}

		var oldCollisionShape = GetNodeOrNull<CollisionPolygon2D>("TrailRenderer/TrailCollision/TrailCollisionShape");
		if (oldCollisionShape != null)
		{
			oldCollisionShape.QueueFree();
		}

		// Move trail renderer to world space
		RemoveChild(_trailRenderer);
		GetParent().AddChild(_trailRenderer);
		_trailRenderer.GlobalPosition = Vector2.Zero;

		// Generate visuals
		GenerateWedgeGeometry();

		// Initialize wall start at spawn position
		_currentWallStart = GlobalPosition;
		_hasWallStart = true;

		// Set initial direction based on spawn position
		InitializeDirection();

		// Find player reference
		_player = GetTree().Root.FindChild("Player", true, false) as Player;
		if (_player == null)
		{
			GD.PrintErr("[EnemyCycle] ERROR: Could not find Player node!");
		}

		GD.Print($"[EnemyCycle] ✓ Enemy initialized at {GlobalPosition}, direction: {_currentDirection}");
	}

	/// <summary>
	/// Initialize direction based on spawn position relative to center
	/// </summary>
	private void InitializeDirection()
	{
		// Point toward the center (0, 0)
		Vector2 toCenter = -GlobalPosition.Normalized();

		// Pick the cardinal direction closest to pointing at center
		float angle = Mathf.Atan2(toCenter.Y, toCenter.X);

		// Convert angle to direction (0=right, 1=down, 2=left, 3=up)
		// Angle ranges: right: -45 to 45, down: 45 to 135, left: 135 to -135, up: -135 to -45
		if (angle >= -Mathf.Pi / 4 && angle < Mathf.Pi / 4)
			_currentDirection = 0; // Right
		else if (angle >= Mathf.Pi / 4 && angle < 3 * Mathf.Pi / 4)
			_currentDirection = 1; // Down
		else if (angle >= 3 * Mathf.Pi / 4 || angle < -3 * Mathf.Pi / 4)
			_currentDirection = 2; // Left
		else
			_currentDirection = 3; // Up

		UpdateRotationFromDirection();
	}

	/// <summary>
	/// Creates the wedge-shaped enemy geometry (RED)
	/// </summary>
	private void GenerateWedgeGeometry()
	{
		// Define wedge vertices (same as player)
		Vector2[] wedgeVertices = new Vector2[]
		{
			new Vector2(20, 0),    // Front point
			new Vector2(-10, -8),  // Back top
			new Vector2(-5, -8),   // Inner top
			new Vector2(-5, 8),    // Inner bottom
			new Vector2(-10, 8)    // Back bottom
		};

		// Setup body polygon with RED semi-transparent fill
		_bodyPolygon.Polygon = wedgeVertices;
		_bodyPolygon.Color = new Color(1, 0, 0, 0.3f);

		// Create additive blend material for neon glow effect
		var material = new CanvasItemMaterial();
		material.BlendMode = CanvasItemMaterial.BlendModeEnum.Add;
		_bodyPolygon.Material = material;

		// Setup outline with full opacity RED
		_outlineLine.Points = wedgeVertices;
		_outlineLine.AddPoint(wedgeVertices[0]); // Close the shape
		_outlineLine.DefaultColor = new Color(1, 0, 0, 1);
		_outlineLine.Width = 2.0f;
		_outlineLine.Closed = true;

		GD.Print("[EnemyCycle] ✓ Red wedge geometry generated");
	}

	// ========== PHYSICS & AI ==========
	public override void _PhysicsProcess(double delta)
	{
		float deltaF = (float)delta;

		// Update AI target
		UpdateTarget(deltaF);

		// Process AI decision-making
		ProcessAI(deltaF);

		// Process grid-snapped movement (like Player)
		ProcessMovement(deltaF);

		// Move and slide
		MoveAndSlide();

		// Check boundary
		CheckBoundary();

		// Update trail
		UpdateTrail();
	}

	/// <summary>
	/// Updates the target position (player position)
	/// </summary>
	private void UpdateTarget(float delta)
	{
		_targetUpdateTimer += delta;

		if (_targetUpdateTimer >= TARGET_UPDATE_INTERVAL)
		{
			_targetUpdateTimer = 0.0f;

			if (_player != null)
			{
				_targetPosition = _player.GlobalPosition;
			}
		}
	}

	/// <summary>
	/// AI decision making - decides which direction to turn
	/// </summary>
	private void ProcessAI(float delta)
	{
		if (_player == null) return;

		// Make decisions at intervals
		float currentTime = Time.GetTicksMsec() / 1000.0f;
		if (currentTime < _nextDecisionTime) return;

		_nextDecisionTime = currentTime + DECISION_INTERVAL;

		// Don't queue another turn if one is already queued
		if (_queuedDirection.HasValue) return;

		// Get possible turns (left or right)
		int leftDirection = (_currentDirection + 3) % 4; // Counter-clockwise
		int rightDirection = (_currentDirection + 1) % 4; // Clockwise

		// Evaluate each direction
		float straightScore = EvaluateDirection(_currentDirection);
		float leftScore = EvaluateDirection(leftDirection);
		float rightScore = EvaluateDirection(rightDirection);

		// Decide which direction to go
		// Priority: avoid obstacles, move toward player

		// If current direction is blocked, must turn
		if (straightScore < 0)
		{
			if (leftScore >= rightScore && leftScore > 0)
			{
				_queuedDirection = leftDirection;
			}
			else if (rightScore > 0)
			{
				_queuedDirection = rightDirection;
			}
			// Both blocked - keep going straight (will die soon)
		}
		else
		{
			// Current direction is clear - should we turn toward player?
			// Only turn if the turn direction is significantly better
			if (leftScore > straightScore + 0.3f && leftScore > rightScore)
			{
				_queuedDirection = leftDirection;
			}
			else if (rightScore > straightScore + 0.3f)
			{
				_queuedDirection = rightDirection;
			}
			// Otherwise keep going straight
		}
	}

	/// <summary>
	/// Evaluates a direction: returns negative if blocked, positive score based on player proximity
	/// </summary>
	private float EvaluateDirection(int direction)
	{
		Vector2 directionVector = GetDirectionVector(direction);

		// Check for obstacles ahead
		if (IsDirectionBlocked(directionVector))
		{
			return -1.0f; // Blocked
		}

		// Score based on how much this direction points toward player
		Vector2 toPlayer = (_targetPosition - GlobalPosition).Normalized();
		float dotProduct = directionVector.Dot(toPlayer);

		// Score ranges from 0 (perpendicular) to 1 (directly toward player)
		return (dotProduct + 1.0f) / 2.0f;
	}

	/// <summary>
	/// Checks if a direction is blocked by trails or boundaries
	/// </summary>
	private bool IsDirectionBlocked(Vector2 direction)
	{
		var spaceState = GetWorld2D().DirectSpaceState;
		var query = PhysicsRayQueryParameters2D.Create(
			GlobalPosition,
			GlobalPosition + direction * AVOIDANCE_CHECK_DISTANCE
		);
		query.CollisionMask = 6; // Layers 2 (trails) + 3 (boundaries/obstacles)
		query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };

		var result = spaceState.IntersectRay(query);
		return result.Count > 0;
	}

	/// <summary>
	/// Processes grid-snapped movement (identical to Player system)
	/// </summary>
	private void ProcessMovement(float delta)
	{
		// Check if we can execute queued turn (grid-aligned)
		if (_queuedDirection.HasValue && IsAlignedToGrid())
		{
			// Snap position to grid
			SnapToGrid();

			// Finalize current wall before turning
			if (_hasWallStart)
			{
				var wall = new TrailWall(_currentWallStart, GlobalPosition);
				_walls.Add(wall);
				_lastWallIndex = _walls.Count - 1;
			}

			// Start new wall at turn point
			_currentWallStart = GlobalPosition;
			_hasWallStart = true;

			// Execute the turn
			_currentDirection = _queuedDirection.Value;
			_queuedDirection = null;
			UpdateRotationFromDirection();

			// Update visuals and collision
			UpdateTrailVisual();
			UpdateTrailCollision();
		}

		// Always move forward in current direction
		Vector2 moveDirection = GetDirectionVector(_currentDirection);
		Velocity = moveDirection * MoveSpeed;
	}

	/// <summary>
	/// Checks if enemy is aligned to grid for turning
	/// </summary>
	private bool IsAlignedToGrid()
	{
		if (_currentDirection == 0 || _currentDirection == 2) // Right or Left (horizontal)
		{
			float remainder = Mathf.Abs(GlobalPosition.X) % GridSize;
			return remainder < 2.0f || remainder > (GridSize - 2.0f);
		}
		else // Up or Down (vertical)
		{
			float remainder = Mathf.Abs(GlobalPosition.Y) % GridSize;
			return remainder < 2.0f || remainder > (GridSize - 2.0f);
		}
	}

	/// <summary>
	/// Snaps enemy position to nearest grid line
	/// </summary>
	private void SnapToGrid()
	{
		Vector2 snappedPos = GlobalPosition;
		snappedPos.X = Mathf.Round(snappedPos.X / GridSize) * GridSize;
		snappedPos.Y = Mathf.Round(snappedPos.Y / GridSize) * GridSize;
		GlobalPosition = snappedPos;
	}

	/// <summary>
	/// Gets the movement direction vector based on direction index
	/// </summary>
	private Vector2 GetDirectionVector(int direction)
	{
		return direction switch
		{
			0 => Vector2.Right,  // East
			1 => Vector2.Down,   // South
			2 => Vector2.Left,   // West
			3 => Vector2.Up,     // North
			_ => Vector2.Right
		};
	}

	/// <summary>
	/// Updates the visual rotation to match the current direction
	/// </summary>
	private void UpdateRotationFromDirection()
	{
		Rotation = _currentDirection * Mathf.Pi / 2.0f; // 0, 90, 180, 270 degrees
	}

	/// <summary>
	/// Checks if enemy has left the arena boundary and dies if so
	/// </summary>
	private void CheckBoundary()
	{
		if (!ArenaBounds.HasPoint(GlobalPosition))
		{
			GD.Print($"[EnemyCycle] Left arena boundary at {GlobalPosition}");
			Die();
		}
	}

	// ========== TRAIL SYSTEM ==========
	/// <summary>
	/// Updates the trail
	/// </summary>
	private void UpdateTrail()
	{
		UpdateTrailVisual();
	}

	/// <summary>
	/// Updates the trail line visual
	/// </summary>
	private void UpdateTrailVisual()
	{
		// Clear all existing trail lines
		var childrenToRemove = new List<Node>();
		foreach (Node child in _trailRenderer.GetChildren())
		{
			if (child is Line2D)
			{
				childrenToRemove.Add(child);
			}
		}
		foreach (Node child in childrenToRemove)
		{
			_trailRenderer.RemoveChild(child);
			child.QueueFree();
		}

		// Render all completed walls
		foreach (var wall in _walls)
		{
			CreateWallLine(wall);
		}

		// Render current wall being laid
		if (_hasWallStart && GlobalPosition.DistanceTo(_currentWallStart) > 1.0f)
		{
			var currentWall = new TrailWall(_currentWallStart, GlobalPosition);
			CreateWallLine(currentWall);
		}
	}

	/// <summary>
	/// Creates a Line2D for a single wall (RED)
	/// </summary>
	private void CreateWallLine(TrailWall wall)
	{
		// Don't create zero-length walls
		if (wall.Start.DistanceTo(wall.End) < 1.0f)
		{
			return;
		}

		var line = new Line2D();
		line.Points = new Vector2[] { wall.Start, wall.End };
		line.DefaultColor = new Color(1, 0, 0, 0.8f); // RED
		line.Width = TRAIL_WIDTH;

		// Add additive blend for glow effect
		var material = new CanvasItemMaterial();
		material.BlendMode = CanvasItemMaterial.BlendModeEnum.Add;
		line.Material = material;

		_trailRenderer.AddChild(line);
	}

	/// <summary>
	/// Updates the trail collision polygon
	/// </summary>
	private void UpdateTrailCollision()
	{
		// Clear existing collision shapes
		foreach (Node child in _trailCollision.GetChildren())
		{
			if (child is CollisionPolygon2D)
			{
				_trailCollision.RemoveChild(child);
				child.QueueFree();
			}
		}

		// Create collision shape for each wall (except the most recent one)
		for (int i = 0; i < _walls.Count; i++)
		{
			// Skip the last wall - enemy just turned there
			if (i == _lastWallIndex)
			{
				continue;
			}

			CreateCollisionForWall(_walls[i]);
		}
	}

	/// <summary>
	/// Creates a collision polygon for a single wall
	/// </summary>
	private void CreateCollisionForWall(TrailWall wall)
	{
		// Create a rectangular collision polygon with width around the wall
		Vector2 direction = (wall.End - wall.Start).Normalized();
		Vector2 perpendicular = new Vector2(-direction.Y, direction.X) * (TRAIL_WIDTH / 2.0f);

		// Create 4 corners of rectangle
		Vector2[] collisionPoints = new Vector2[]
		{
			wall.Start + perpendicular,  // Top-left
			wall.End + perpendicular,    // Top-right
			wall.End - perpendicular,    // Bottom-right
			wall.Start - perpendicular   // Bottom-left
		};

		// Create collision polygon node
		var collisionShape = new CollisionPolygon2D();
		collisionShape.Polygon = collisionPoints;
		_trailCollision.AddChild(collisionShape);
	}

	// ========== COLLISION HANDLING ==========
	/// <summary>
	/// Called when something collides with the trail
	/// </summary>
	private void _OnTrailCollisionBodyEntered(Node2D body)
	{
		// Check if enemy hit a trail (including its own)
		Die();
	}

	// ========== DEATH HANDLING ==========
	/// <summary>
	/// Handles enemy death
	/// </summary>
	public void Die()
	{
		GD.Print($"[EnemyCycle] Enemy died at {GlobalPosition}");

		// Award cycles to player
		GameManager.Instance?.AddClockCycles(10);
		GameManager.Instance?.AddEnemyKill();

		// TODO: Spawn death particle effect
		GD.Print("[EnemyCycle] Death effect placeholder");

		// Emit signal
		EmitSignal(SignalName.OnEnemyDied);

		// Clean up trail renderer
		if (_trailRenderer != null && IsInstanceValid(_trailRenderer))
		{
			_trailRenderer.QueueFree();
		}

		// Remove this enemy
		QueueFree();
	}

	// ========== CLEANUP ==========
	public override void _ExitTree()
	{
		// Clean up trail renderer if it still exists
		if (_trailRenderer != null && IsInstanceValid(_trailRenderer))
		{
			_trailRenderer.QueueFree();
		}
	}
}
