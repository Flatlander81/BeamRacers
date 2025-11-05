using Godot;
using System;

/// <summary>
/// Enemy Light Cycle with grid-snapped movement, chase AI, and trail generation.
/// Movement system identical to Player - 90-degree turns at grid lines only.
/// Trail management delegated to TrailManager singleton.
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

		// Remove old trail nodes
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

		// Set initial direction based on spawn position
		InitializeDirection();

		// Register with TrailManager
		if (TrailManager.Instance != null)
		{
			TrailManager.Instance.RegisterCycle(this, new Color(1, 0, 0, 0.8f), _trailRenderer, _trailCollision);
			GD.Print("[EnemyCycle] ✓ Registered with TrailManager");
		}
		else
		{
			GD.PrintErr("[EnemyCycle] ERROR: TrailManager not found!");
		}

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
		Vector2 toCenter = -GlobalPosition.Normalized();
		float angle = Mathf.Atan2(toCenter.Y, toCenter.X);

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
		Vector2[] wedgeVertices = new Vector2[]
		{
			new Vector2(20, 0),
			new Vector2(-10, -8),
			new Vector2(-5, -8),
			new Vector2(-5, 8),
			new Vector2(-10, 8)
		};

		_bodyPolygon.Polygon = wedgeVertices;
		_bodyPolygon.Color = new Color(1, 0, 0, 0.3f);

		var material = new CanvasItemMaterial();
		material.BlendMode = CanvasItemMaterial.BlendModeEnum.Add;
		_bodyPolygon.Material = material;

		_outlineLine.Points = wedgeVertices;
		_outlineLine.AddPoint(wedgeVertices[0]);
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

		// Update trail via TrailManager
		TrailManager.Instance?.UpdateCycleTrail(this);
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
		int leftDirection = (_currentDirection + 3) % 4;
		int rightDirection = (_currentDirection + 1) % 4;

		// Evaluate each direction
		float straightScore = EvaluateDirection(_currentDirection);
		float leftScore = EvaluateDirection(leftDirection);
		float rightScore = EvaluateDirection(rightDirection);

		// Decide which direction to go
		if (straightScore < 0)
		{
			// Must turn
			if (leftScore >= rightScore && leftScore > 0)
			{
				_queuedDirection = leftDirection;
			}
			else if (rightScore > 0)
			{
				_queuedDirection = rightDirection;
			}
		}
		else
		{
			// Can continue straight - should we turn toward player?
			if (leftScore > straightScore + 0.3f && leftScore > rightScore)
			{
				_queuedDirection = leftDirection;
			}
			else if (rightScore > straightScore + 0.3f)
			{
				_queuedDirection = rightDirection;
			}
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
			return -1.0f;
		}

		// Score based on how much this direction points toward player
		Vector2 toPlayer = (_targetPosition - GlobalPosition).Normalized();
		float dotProduct = directionVector.Dot(toPlayer);

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

			// Create turn via TrailManager
			TrailManager.Instance?.CreateTurn(this, GlobalPosition);

			// Execute the turn
			_currentDirection = _queuedDirection.Value;
			_queuedDirection = null;
			UpdateRotationFromDirection();
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
	private void UpdateRotationFromDirection()
	{
		Rotation = _currentDirection * Mathf.Pi / 2.0f;
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

	// ========== COLLISION HANDLING ==========
	/// <summary>
	/// Called when something collides with the trail
	/// </summary>
	private void _OnTrailCollisionBodyEntered(Node2D body)
	{
		// Check if this enemy hit its own trail
		if (body == this)
		{
			GD.Print($"[EnemyCycle] Enemy hit own trail at {GlobalPosition}");
			Die();
		}
		// Check if the player hit this enemy's trail
		else if (body is Player player)
		{
			GD.Print($"[EnemyCycle] Player hit enemy trail at {body.GlobalPosition}");
			if (!player.IsShieldActive())
			{
				player.Die();
			}
		}
		// Check if another enemy hit this enemy's trail
		else if (body is EnemyCycle otherEnemy)
		{
			GD.Print($"[EnemyCycle] Enemy hit another enemy's trail at {body.GlobalPosition}");
			otherEnemy.Die();
		}
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

		// Unregister from TrailManager
		TrailManager.Instance?.UnregisterCycle(this);

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
		TrailManager.Instance?.UnregisterCycle(this);

		// Clean up trail renderer if it still exists
		if (_trailRenderer != null && IsInstanceValid(_trailRenderer))
		{
			_trailRenderer.QueueFree();
		}
	}
}
