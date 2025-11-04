using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Enemy Light Cycle with chase AI, trail generation, and avoidance behavior.
/// </summary>
public partial class EnemyCycle : CharacterBody2D
{
	// ========== SIGNALS ==========
	[Signal]
	public delegate void OnEnemyDiedEventHandler();

	// ========== EXPORTS ==========
	[Export] public float MaxSpeed = 250.0f;
	[Export] public float Acceleration = 500.0f;

	// ========== NODE REFERENCES ==========
	private Polygon2D _bodyPolygon;
	private Line2D _outlineLine;
	private Node2D _trailRenderer;
	private Area2D _trailCollision;

	// ========== AI STATE ==========
	private Vector2 _targetPosition = Vector2.Zero;
	private float _targetUpdateTimer = 0.0f;
	private const float TARGET_UPDATE_INTERVAL = 0.2f;
	private Player _player;

	// ========== TRAIL STATE ==========
	private List<TrailWall> _walls = new List<TrailWall>();
	private Vector2 _currentWallStart = Vector2.Zero;
	private bool _hasWallStart = false;
	private int _lastWallIndex = -1;
	private const float TRAIL_WIDTH = 4.0f;
	private float _lastTurnTime = 0.0f;
	private const float MIN_TURN_INTERVAL = 0.15f; // Prevent turn spam

	// ========== TRAIL AVOIDANCE ==========
	private const float AVOIDANCE_RAYCAST_LENGTH = 100.0f;
	private const float MIN_CLEAR_DISTANCE = 50.0f;

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

		// Find player reference
		_player = GetTree().Root.FindChild("Player", true, false) as Player;
		if (_player == null)
		{
			GD.PrintErr("[EnemyCycle] ERROR: Could not find Player node!");
		}

		GD.Print($"[EnemyCycle] ✓ Enemy initialized at {GlobalPosition}");
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

		// Process movement with avoidance
		ProcessMovement(deltaF);

		// Move and slide
		MoveAndSlide();

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
	/// Processes AI movement with trail avoidance
	/// </summary>
	private void ProcessMovement(float delta)
	{
		if (_player == null)
		{
			Velocity = Vector2.Zero;
			return;
		}

		// Get direction to target
		Vector2 directionToTarget = (_targetPosition - GlobalPosition).Normalized();

		// Check for obstacles ahead and adjust direction
		Vector2 moveDirection = GetAvoidanceDirection(directionToTarget);

		// Accelerate toward target
		Vector2 desiredVelocity = moveDirection * MaxSpeed;
		Velocity = Velocity.MoveToward(desiredVelocity, Acceleration * delta);

		// Update rotation to face movement direction
		if (Velocity.LengthSquared() > 0.1f)
		{
			Rotation = Velocity.Angle();
		}

		// Handle turning for trail system
		CheckAndCreateTurn();
	}

	/// <summary>
	/// Checks ahead for trails and returns adjusted direction
	/// </summary>
	private Vector2 GetAvoidanceDirection(Vector2 desiredDirection)
	{
		// Cast ray ahead
		var spaceState = GetWorld2D().DirectSpaceState;
		var query = PhysicsRayQueryParameters2D.Create(
			GlobalPosition,
			GlobalPosition + desiredDirection * AVOIDANCE_RAYCAST_LENGTH
		);
		query.CollisionMask = 2; // Layer 2 is trails
		query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };

		var result = spaceState.IntersectRay(query);

		// If no obstacle ahead, continue forward
		if (result.Count == 0)
		{
			return desiredDirection;
		}

		// Obstacle detected, try turning
		Vector2 leftDirection = desiredDirection.Rotated(-Mathf.Pi / 2);
		Vector2 rightDirection = desiredDirection.Rotated(Mathf.Pi / 2);

		// Check left direction
		float leftClearance = CheckDirectionClearance(leftDirection);

		// Check right direction
		float rightClearance = CheckDirectionClearance(rightDirection);

		// Pick clearer direction
		if (leftClearance > MIN_CLEAR_DISTANCE || rightClearance > MIN_CLEAR_DISTANCE)
		{
			return leftClearance > rightClearance ? leftDirection : rightDirection;
		}

		// Both blocked, continue forward (will likely die)
		return desiredDirection;
	}

	/// <summary>
	/// Checks how clear a direction is (returns distance to obstacle)
	/// </summary>
	private float CheckDirectionClearance(Vector2 direction)
	{
		var spaceState = GetWorld2D().DirectSpaceState;
		var query = PhysicsRayQueryParameters2D.Create(
			GlobalPosition,
			GlobalPosition + direction * AVOIDANCE_RAYCAST_LENGTH
		);
		query.CollisionMask = 2;
		query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };

		var result = spaceState.IntersectRay(query);

		if (result.Count == 0)
		{
			return AVOIDANCE_RAYCAST_LENGTH;
		}

		Vector2 hitPosition = (Vector2)result["position"];
		return GlobalPosition.DistanceTo(hitPosition);
	}

	/// <summary>
	/// Checks if enemy should create a turn (when direction changes significantly)
	/// </summary>
	private void CheckAndCreateTurn()
	{
		// Prevent turn spam
		float currentTime = Time.GetTicksMsec() / 1000.0f;
		if (currentTime - _lastTurnTime < MIN_TURN_INTERVAL)
		{
			return;
		}

		// Check if we've changed direction significantly (>45 degrees)
		if (_hasWallStart && Velocity.LengthSquared() > 0.1f)
		{
			Vector2 currentDirection = Velocity.Normalized();
			Vector2 wallDirection = (GlobalPosition - _currentWallStart).Normalized();

			if (wallDirection.LengthSquared() > 0.1f)
			{
				float angle = Mathf.Abs(currentDirection.AngleTo(wallDirection));

				if (angle > Mathf.Pi / 4) // 45 degrees
				{
					CreateTurn();
					_lastTurnTime = currentTime;
				}
			}
		}
	}

	/// <summary>
	/// Creates a turn point and finalizes the current wall
	/// </summary>
	private void CreateTurn()
	{
		if (_hasWallStart)
		{
			var wall = new TrailWall(_currentWallStart, GlobalPosition);

			// Don't create zero-length walls
			if (_currentWallStart.DistanceTo(GlobalPosition) > 10.0f)
			{
				_walls.Add(wall);
				_lastWallIndex = _walls.Count - 1;
			}
		}

		// Start new wall at turn point
		_currentWallStart = GlobalPosition;
		_hasWallStart = true;

		// Update visuals and collision
		UpdateTrailVisual();
		UpdateTrailCollision();
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
