using Godot;
using System;

/// <summary>
/// Player Light Cycle controller with movement, shield, and collision systems.
/// Trail management delegated to TrailManager singleton.
/// Inherits grid movement logic from GridCycle base class.
/// </summary>
public partial class Player : GridCycle
{
	// ========== EXPORTS ==========
	[Export] public float MoveSpeed = 200.0f;
	[Export] public float ShieldDuration = 1.5f;
	[Export] public float ShieldCooldown = 8.0f;

	// ========== NODE REFERENCES ==========
	private Polygon2D _bodyPolygon;
	private Line2D _outlineLine;
	private Polygon2D _shieldVisual;
	private Node2D _trailRenderer;
	private Area2D _trailCollision;

	// ========== MOVEMENT STATE ==========
	public int GridExtent = 2000;
	private bool _inputEnabled = true;

	// ========== SHIELD STATE ==========
	private enum ShieldState { Ready, Active, Cooldown }
	private ShieldState _shieldState = ShieldState.Ready;
	private float _shieldTimer = 0.0f;
	private float _shieldPulseTime = 0.0f;
	private bool _shieldBrokeTrailThisActivation = false;

	// ========== DEATH STATE ==========
	private bool _isDead = false;

	// ========== DEBUG ==========
	private int _frameCounter = 0;

	// ========== INITIALIZATION ==========
	public override void _Ready()
	{
		GD.Print("[Player] Initializing player system...");

		// Get node references
		_bodyPolygon = GetNode<Polygon2D>("Sprite/Body");
		_outlineLine = GetNode<Line2D>("Sprite/Outline");
		_shieldVisual = GetNode<Polygon2D>("ShieldSystem/ShieldVisual");
		_trailRenderer = GetNode<Node2D>("TrailRenderer");
		_trailCollision = GetNode<Area2D>("TrailRenderer/TrailCollision");

		// Initialize trail renderer (now reconnects signal after reparenting)
		InitializeTrailRenderer(_trailRenderer, _trailCollision);
		GD.Print("[Player] ✓ Trail renderer moved to world space");

		// Generate visuals
		GenerateWedgeGeometry();
		GenerateShieldGeometry();

		// Register with TrailManager
		RegisterWithTrailManager(new Color(0, 1, 1, 0.8f), _trailRenderer, _trailCollision, "Player");

		// Debug: Verify collision configuration
		GD.Print($"[Player] CollisionLayer: {CollisionLayer}, CollisionMask: {CollisionMask}");
		GD.Print($"[Player] TrailCollision Layer: {_trailCollision.CollisionLayer}, Mask: {_trailCollision.CollisionMask}");
		GD.Print($"[Player] TrailCollision Monitoring: {_trailCollision.Monitoring}, Monitorable: {_trailCollision.Monitorable}");
		GD.Print($"[Player] TrailCollision Parent: {_trailCollision.GetParent().Name}");
		GD.Print($"[Player] ✓ Player initialized at {GlobalPosition}");
	}

	/// <summary>
	/// Creates the wedge-shaped player geometry
	/// </summary>
	private void GenerateWedgeGeometry()
	{
		GenerateWedgeGeometry(_bodyPolygon, _outlineLine, new Color(0, 1, 1, 1)); // Cyan
		GD.Print("[Player] ✓ Wedge geometry generated");
	}

	/// <summary>
	/// Creates the circular shield visual
	/// </summary>
	private void GenerateShieldGeometry()
	{
		const int CIRCLE_VERTICES = 32;
		const float SHIELD_RADIUS = 25.0f;
		Vector2[] circlePoints = new Vector2[CIRCLE_VERTICES];

		for (int i = 0; i < CIRCLE_VERTICES; i++)
		{
			float angle = (float)i / CIRCLE_VERTICES * Mathf.Tau;
			circlePoints[i] = new Vector2(
				Mathf.Cos(angle) * SHIELD_RADIUS,
				Mathf.Sin(angle) * SHIELD_RADIUS
			);
		}

		_shieldVisual.Polygon = circlePoints;
		_shieldVisual.Color = new Color(1, 1, 1, 0.3f);

		var material = new CanvasItemMaterial();
		material.BlendMode = CanvasItemMaterial.BlendModeEnum.Add;
		_shieldVisual.Material = material;

		_shieldVisual.Visible = false;

		GD.Print("[Player] ✓ Shield geometry generated");
	}

	// ========== PHYSICS & MOVEMENT ==========
	public override void _PhysicsProcess(double delta)
	{
		float deltaF = (float)delta;

		// Handle movement
		if (_inputEnabled)
		{
			ProcessMovement(deltaF);
		}
		else
		{
			Velocity = Vector2.Zero;
		}

		// Move and slide
		MoveAndSlide();

		// Check boundary
		CheckBoundary();

		// Update trail via TrailManager
		TrailManager.Instance?.UpdateCycleTrail(this, deltaF);

		// Handle shield
		UpdateShield(deltaF);

		// Debug output
		_frameCounter++;
		if (_frameCounter >= 60)
		{
			PrintDebugInfo();
			_frameCounter = 0;
		}
	}

	/// <summary>
	/// Checks if player has left the grid boundary and dies if so
	/// </summary>
	private void CheckBoundary()
	{
		if (!_inputEnabled) return;

		if (Mathf.Abs(GlobalPosition.X) > GridExtent || Mathf.Abs(GlobalPosition.Y) > GridExtent)
		{
			GD.Print($"[Player] Left grid boundary at {GlobalPosition}");
			Die();
		}
	}

	/// <summary>
	/// Processes player movement input - Grid-snapped Tron-style turns
	/// </summary>
	private void ProcessMovement(float delta)
	{
		// Queue turn input
		if (Input.IsActionJustPressed("move_left") && _queuedDirection == null)
		{
			_queuedDirection = (_currentDirection + 3) % 4;
		}
		else if (Input.IsActionJustPressed("move_right") && _queuedDirection == null)
		{
			_queuedDirection = (_currentDirection + 1) % 4;
		}

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

			GD.Print($"[Player] Turn executed at {GlobalPosition}");
		}

		// Always move forward in current direction
		Vector2 moveDirection = GetDirectionVector();
		Velocity = moveDirection * MoveSpeed;
	}

	// ========== SHIELD SYSTEM ==========
	/// <summary>
	/// Updates shield state and visuals
	/// </summary>
	private void UpdateShield(float delta)
	{
		// Handle shield input
		if (Input.IsActionJustPressed("shield_activate") && _shieldState == ShieldState.Ready && _inputEnabled)
		{
			ActivateShield();
		}

		// Update shield timer
		if (_shieldState == ShieldState.Active)
		{
			_shieldTimer += delta;
			_shieldPulseTime += delta;

			// Pulse animation
			float pulseScale = 1.0f + Mathf.Sin(_shieldPulseTime * 8.0f) * 0.05f;
			_shieldVisual.Scale = new Vector2(pulseScale, pulseScale);

			if (_shieldTimer >= ShieldDuration)
			{
				DeactivateShield();
			}
		}
		else if (_shieldState == ShieldState.Cooldown)
		{
			_shieldTimer += delta;

			if (_shieldTimer >= ShieldCooldown)
			{
				_shieldState = ShieldState.Ready;
				_shieldTimer = 0.0f;
				GD.Print("[Player] Shield ready!");
			}
		}
	}

	/// <summary>
	/// Activates the player shield
	/// </summary>
	private void ActivateShield()
	{
		_shieldState = ShieldState.Active;
		_shieldTimer = 0.0f;
		_shieldPulseTime = 0.0f;
		_shieldBrokeTrailThisActivation = false;
		_shieldVisual.Visible = true;
		_shieldVisual.Scale = Vector2.One;

		GD.Print($"[Player] Shield activated! Duration: {ShieldDuration}s");
	}

	/// <summary>
	/// Deactivates the player shield
	/// </summary>
	private void DeactivateShield()
	{
		_shieldState = ShieldState.Cooldown;
		_shieldTimer = 0.0f;
		_shieldVisual.Visible = false;

		GD.Print($"[Player] Shield deactivated. Cooldown: {ShieldCooldown}s");
	}

	// ========== COLLISION HANDLING ==========
	/// <summary>
	/// Called when something collides with the trail
	/// </summary>
	protected override void _OnTrailCollisionBodyEntered(Node2D body)
	{
		GD.Print($"[Player] Trail collision detected with: {body.Name} (Type: {body.GetType().Name})");

		// Check if it's the player hitting their own trail
		if (body == this)
		{
			// Grace period: Ignore self-collision if we just turned at this position
			if (TrailManager.Instance != null && TrailManager.Instance.IsWithinTurnGracePeriod(this, GlobalPosition))
			{
				GD.Print($"[Player] Self-collision ignored (turn grace period)");
				return;
			}

			if (_shieldState == ShieldState.Active)
			{
				if (!_shieldBrokeTrailThisActivation)
				{
					GD.Print($"[Player] Shield collision detected at {GlobalPosition}");
					TrailManager.Instance?.BreakClosestWall(GlobalPosition, this);
					_shieldBrokeTrailThisActivation = true;
					GD.Print("[Player] Shield absorbed trail collision!");
				}
			}
			else
			{
				Die();
			}
		}
		// Check if an enemy hit the player's trail
		else if (body is EnemyCycle enemy)
		{
			GD.Print($"[Player] Enemy hit player trail at {body.GlobalPosition}");
			enemy.Die();
		}
	}

	// ========== DEATH HANDLING ==========
	/// <summary>
	/// Handles player death
	/// </summary>
	public void Die()
	{
		// Prevent double-death (can happen if hit multiple trails simultaneously)
		if (_isDead) return;
		_isDead = true;

		GD.Print("═══════════════════════════════");
		GD.Print("[Player] PLAYER DEATH");
		GD.Print($"[Player] Position: {GlobalPosition}");
		GD.Print("═══════════════════════════════");

		_inputEnabled = false;

		// TODO: Spawn death particle effect
		GD.Print("[Player] Death effect placeholder");

		// End the run after a short delay
		GetTree().CreateTimer(1.0).Timeout += () =>
		{
			GameManager.Instance?.EndRun();
		};
	}

	// ========== PUBLIC PROPERTIES & METHODS ==========
	/// <summary>
	/// Returns whether the shield is currently active
	/// </summary>
	public bool IsShieldActive()
	{
		return _shieldState == ShieldState.Active;
	}

	/// <summary>
	/// Resets the player to a specific position
	/// </summary>
	public void ResetPosition(Vector2 pos)
	{
		GlobalPosition = pos;
		_currentDirection = 0;
		Rotation = 0;
		_inputEnabled = true;

		TrailManager.Instance?.ClearCycleTrails(this);

		GD.Print($"[Player] Position reset to {pos}");
	}

	/// <summary>
	/// Clears all trail walls
	/// </summary>
	public void ClearTrail()
	{
		TrailManager.Instance?.ClearCycleTrails(this);
		GD.Print("[Player] Trail cleared");
	}

	// ========== CLEANUP ==========
	public override void _ExitTree()
	{
		TrailManager.Instance?.UnregisterCycle(this);
	}

	// ========== DEBUG ==========
	/// <summary>
	/// Prints debug information every 60 frames
	/// </summary>
	private void PrintDebugInfo()
	{
		string shieldStatus = _shieldState switch
		{
			ShieldState.Ready => "READY",
			ShieldState.Active => $"ACTIVE ({(ShieldDuration - _shieldTimer):F1}s)",
			ShieldState.Cooldown => $"COOLDOWN ({(ShieldCooldown - _shieldTimer):F1}s)",
			_ => "UNKNOWN"
		};

		string directionName = _currentDirection switch
		{
			0 => "RIGHT",
			1 => "DOWN",
			2 => "LEFT",
			3 => "UP",
			_ => "UNKNOWN"
		};

		GD.Print($"[Player] Pos: {GlobalPosition:F0} | Dir: {directionName} | Speed: {Velocity.Length():F0} | Shield: {shieldStatus}");
	}
}
