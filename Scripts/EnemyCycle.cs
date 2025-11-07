using Godot;
using System;

/// <summary>
/// Enemy Light Cycle with grid-snapped movement, chase AI, and trail generation.
/// Movement system identical to Player - 90-degree turns at grid lines only.
/// Trail management delegated to TrailManager singleton.
/// Inherits grid movement logic from GridCycle base class.
/// </summary>
public partial class EnemyCycle : GridCycle
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

	// ========== GRID PARAMETERS ==========
	public Rect2 ArenaBounds = new Rect2(-800, -450, 1600, 900);

	// ========== AI STATE ==========
	private Vector2 _targetPosition = Vector2.Zero;
	private float _targetUpdateTimer = 0.0f;
	[Export] public float TargetUpdateInterval = 0.2f;
	private Player _player;
	private float _nextDecisionTime = 0.0f;
	[Export] public float DecisionInterval = 0.3f;

	// ========== DEATH STATE ==========
	private bool _isDead = false;

	// ========== TRAIL AVOIDANCE ==========
	[Export] public float AvoidanceCheckDistance = 150.0f;

	// ========== AUTOMATED TEST MODE ==========
	private bool _autoTestMode = false;
	private float _autoTestTimer = 0.0f;
	private int _autoTestStep = 0;
	private string _autoTestPattern = "move_straight";
	private bool _collisionTestMode = false;  // When true, log collisions but don't die

	// ========== INITIALIZATION ==========
	public override void _Ready()
	{
		GD.Print("[EnemyCycle] Initializing enemy cycle...");

		// Get node references
		_bodyPolygon = GetNode<Polygon2D>("Sprite/Body");
		_outlineLine = GetNode<Line2D>("Sprite/Outline");
		_trailRenderer = GetNode<Node2D>("TrailRenderer");

		// Initialize trail renderer
		InitializeTrailRenderer(_trailRenderer);

		// Generate visuals
		GenerateWedgeGeometry();

		// Set initial direction based on spawn position
		InitializeDirection();

		// Register with TrailManager
		RegisterWithTrailManager(new Color(1, 0, 0, 0.8f), _trailRenderer, CellOccupant.EnemyTrail, "EnemyCycle");

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
		GenerateWedgeGeometry(_bodyPolygon, _outlineLine, new Color(1, 0, 0, 1)); // Red
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

		// Check collision with grid
		CheckGridCollision();

		// Check boundary
		CheckBoundary();

		// Update trail via TrailManager
		TrailManager.Instance?.UpdateCycleTrail(this, deltaF);
	}

	/// <summary>
	/// Updates the target position (player position)
	/// </summary>
	private void UpdateTarget(float delta)
	{
		_targetUpdateTimer += delta;

		if (_targetUpdateTimer >= TargetUpdateInterval)
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
		// Handle automated test mode
		if (_autoTestMode)
		{
			ProcessAutomatedTest(delta);
			return;
		}

		if (_player == null) return;

		// Make decisions at intervals
		float currentTime = Time.GetTicksMsec() / 1000.0f;
		if (currentTime < _nextDecisionTime) return;

		_nextDecisionTime = currentTime + DecisionInterval;

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
	/// Automated test sequence for enemy
	/// </summary>
	private void ProcessAutomatedTest(float delta)
	{
		_autoTestTimer += delta;

		switch (_autoTestPattern)
		{
			case "move_straight":
				// Just move straight - no turns
				break;
			case "box_pattern":
				ProcessBoxPattern();
				break;
			case "turn_left":
				if (_autoTestTimer > 1.0f && _queuedDirection == null)
				{
					GD.Print("[Enemy AutoTest] Turning LEFT");
					_queuedDirection = (_currentDirection + 3) % 4;
					_autoTestTimer = 0.0f;
				}
				break;
			case "turn_right":
				if (_autoTestTimer > 1.0f && _queuedDirection == null)
				{
					GD.Print("[Enemy AutoTest] Turning RIGHT");
					_queuedDirection = (_currentDirection + 1) % 4;
					_autoTestTimer = 0.0f;
				}
				break;
		}
	}

	private void ProcessBoxPattern()
	{
		switch (_autoTestStep)
		{
			case 0:
			case 1:
			case 2:
			case 3:
				if (_autoTestTimer > 1.2f && _queuedDirection == null)
				{
					GD.Print($"[Enemy AutoTest] Box turn {_autoTestStep + 1}: LEFT");
					_queuedDirection = (_currentDirection + 3) % 4;
					_autoTestTimer = 0.0f;
					_autoTestStep++;
				}
				break;
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
			GlobalPosition + direction * AvoidanceCheckDistance
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
	/// Checks grid collision (trails, boundaries, obstacles)
	/// </summary>
	private void CheckGridCollision()
	{
		if (GridCollisionManager.Instance == null) return;

		// Check the cell ahead of us in our movement direction, not our current cell
		// This prevents hitting our own currently-drawing trail while allowing others to hit it
		Vector2 directionVector = GetDirectionVector();
		Vector2 checkPosition = GlobalPosition + directionVector * (GridSize / 2.0f);

		CellOccupant occupant = GridCollisionManager.Instance.GetCell(checkPosition);

		// Check if we hit anything that would kill us
		if (occupant != CellOccupant.Empty)
		{
			Vector2I checkGrid = GridCollisionManager.Instance.WorldToGrid(checkPosition);
			GD.Print($"[Enemy] ⚠ COLLISION DETECTED: {occupant} at {checkPosition} (grid {checkGrid})");

			if (_collisionTestMode)
			{
				GD.Print($"[Enemy] 🧪 TEST MODE COLLISION: Would have died hitting {occupant} at {checkPosition}");
			}
			else
			{
				GD.Print($"[Enemy] ☠ DEATH: Hit {occupant} at {checkPosition}");
				Die();
			}
		}
	}

	/// <summary>
	/// Checks if enemy has left the arena boundary and dies if so
	/// </summary>
	private void CheckBoundary()
	{
		if (!ArenaBounds.HasPoint(GlobalPosition))
		{
			if (_collisionTestMode)
			{
				GD.Print($"[EnemyCycle] 🧪 TEST MODE: Would have died leaving arena boundary at {GlobalPosition}");
			}
			else
			{
				GD.Print($"[EnemyCycle] Left arena boundary at {GlobalPosition}");
				Die();
			}
		}
	}

	// ========== DEATH HANDLING ==========
	/// <summary>
	/// Handles enemy death
	/// </summary>
	public void Die()
	{
		// Prevent double-death (can happen if hit multiple trails simultaneously)
		if (_isDead) return;
		_isDead = true;

		GD.Print($"[EnemyCycle] Enemy died at {GlobalPosition}");

		// Award cycles to player
		GameManager.Instance?.AddClockCycles(10);
		GameManager.Instance?.AddEnemyKill();

		// TODO: Spawn death particle effect
		GD.Print("[EnemyCycle] Death effect placeholder");

		// Emit signal
		EmitSignal(SignalName.OnEnemyDied);

		// Clean up trail resources
		CleanupTrailResources();

		// Remove this enemy
		QueueFree();
	}

	// ========== CLEANUP ==========
	/// <summary>
	/// Cleans up trail resources (unregister from TrailManager and free trail renderer)
	/// </summary>
	private void CleanupTrailResources()
	{
		// Unregister from TrailManager
		TrailManager.Instance?.UnregisterCycle(this);

		// Clean up trail renderer
		if (_trailRenderer != null && IsInstanceValid(_trailRenderer))
		{
			_trailRenderer.QueueFree();
		}
	}

	public override void _ExitTree()
	{
		CleanupTrailResources();
	}
}
