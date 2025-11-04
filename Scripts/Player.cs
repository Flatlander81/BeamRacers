using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Player Light Cycle controller with movement, trail, shield, and collision systems.
/// </summary>
public partial class Player : CharacterBody2D
{
	// ========== EXPORTS ==========
	[Export] public float MoveSpeed = 200.0f;
	[Export] public float ShieldDuration = 1.5f;
	[Export] public float ShieldCooldown = 8.0f;

	// ========== NODE REFERENCES ==========
	private Polygon2D _bodyPolygon;
	private Line2D _outlineLine;
	private Polygon2D _shieldVisual;
	private Line2D _trailLine;
	private Area2D _trailCollision;
	private CollisionPolygon2D _trailCollisionShape;

	// ========== MOVEMENT STATE ==========
	private int _currentDirection = 0; // 0=right, 1=down, 2=left, 3=up
	private bool _inputEnabled = true;

	// ========== TRAIL STATE ==========
	private List<Vector2> _trailPoints = new List<Vector2>();
	private float _distanceSinceLastTrailPoint = 0.0f;
	private const float TRAIL_POINT_DISTANCE = 5.0f;
	private const float TRAIL_WIDTH = 4.0f;
	private const float TRAIL_COLLISION_SAFE_DISTANCE = 25.0f; // Don't collide with trail within this radius
	private const int MAX_TRAIL_POINTS = 500; // Maximum trail points to prevent performance issues
	private const float TRAIL_CLEANUP_DISTANCE = 1000.0f; // Remove trail points farther than this

	// ========== SHIELD STATE ==========
	private enum ShieldState { Ready, Active, Cooldown }
	private ShieldState _shieldState = ShieldState.Ready;
	private float _shieldTimer = 0.0f;
	private float _shieldPulseTime = 0.0f;

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
		_trailLine = GetNode<Line2D>("TrailRenderer/TrailLine");
		_trailCollision = GetNode<Area2D>("TrailRenderer/TrailCollision");
		_trailCollisionShape = GetNode<CollisionPolygon2D>("TrailRenderer/TrailCollision/TrailCollisionShape");

		// Move trail renderer to world space so it doesn't move with player
		var trailRenderer = GetNode<Node2D>("TrailRenderer");
		RemoveChild(trailRenderer);
		GetParent().AddChild(trailRenderer);
		trailRenderer.GlobalPosition = Vector2.Zero;

		GD.Print("[Player] ✓ Trail renderer moved to world space");

		// Generate visuals
		GenerateWedgeGeometry();
		GenerateShieldGeometry();

		GD.Print("[Player] ✓ Player initialized");
	}

	/// <summary>
	/// Creates the wedge-shaped player geometry
	/// </summary>
	private void GenerateWedgeGeometry()
	{
		// Define wedge vertices
		Vector2[] wedgeVertices = new Vector2[]
		{
			new Vector2(20, 0),    // Front point
			new Vector2(-10, -8),  // Back top
			new Vector2(-5, -8),   // Inner top
			new Vector2(-5, 8),    // Inner bottom
			new Vector2(-10, 8)    // Back bottom
		};

		// Setup body polygon with cyan semi-transparent fill
		_bodyPolygon.Polygon = wedgeVertices;
		_bodyPolygon.Color = new Color(0, 1, 1, 0.3f);

		// Create additive blend material for neon glow effect
		var material = new CanvasItemMaterial();
		material.BlendMode = CanvasItemMaterial.BlendModeEnum.Add;
		_bodyPolygon.Material = material;

		// Setup outline with full opacity cyan
		_outlineLine.Points = wedgeVertices;
		_outlineLine.AddPoint(wedgeVertices[0]); // Close the shape
		_outlineLine.DefaultColor = new Color(0, 1, 1, 1);
		_outlineLine.Width = 2.0f;
		_outlineLine.Closed = true;

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

		// Create additive blend material for glow
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

		// Update trail
		UpdateTrail();

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
	/// Processes player movement input - Tron-style 90-degree turns
	/// </summary>
	private void ProcessMovement(float delta)
	{
		// Handle turning (only when not already turning)
		if (Input.IsActionJustPressed("move_left"))
		{
			// Turn left (counter-clockwise)
			_currentDirection = (_currentDirection + 3) % 4; // +3 is same as -1 with wrapping
			UpdateRotationFromDirection();
		}
		else if (Input.IsActionJustPressed("move_right"))
		{
			// Turn right (clockwise)
			_currentDirection = (_currentDirection + 1) % 4;
			UpdateRotationFromDirection();
		}

		// Always move forward in current direction
		Vector2 moveDirection = GetDirectionVector();
		Velocity = moveDirection * MoveSpeed;
	}

	/// <summary>
	/// Gets the movement direction vector based on current direction
	/// </summary>
	private Vector2 GetDirectionVector()
	{
		return _currentDirection switch
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

	// ========== TRAIL SYSTEM ==========
	/// <summary>
	/// Updates the trail based on player movement
	/// </summary>
	private void UpdateTrail()
	{
		// Add trail points as player moves
		if (Velocity.Length() > 1.0f)
		{
			_distanceSinceLastTrailPoint += Velocity.Length() * (float)GetPhysicsProcessDeltaTime();

			if (_distanceSinceLastTrailPoint >= TRAIL_POINT_DISTANCE)
			{
				AddTrailPoint(GlobalPosition);
				_distanceSinceLastTrailPoint = 0.0f;
			}
		}

		// Periodic cleanup
		CleanupOldTrailPoints();
	}

	/// <summary>
	/// Adds a trail point, merging collinear points to reduce trail size
	/// </summary>
	private void AddTrailPoint(Vector2 newPoint)
	{
		if (_trailPoints.Count == 0)
		{
			// First point
			_trailPoints.Add(newPoint);
		}
		else if (_trailPoints.Count == 1)
		{
			// Second point
			_trailPoints.Add(newPoint);
		}
		else
		{
			// Check if last 3 points are collinear (in a straight line)
			Vector2 p1 = _trailPoints[_trailPoints.Count - 2];
			Vector2 p2 = _trailPoints[_trailPoints.Count - 1];
			Vector2 p3 = newPoint;

			if (ArePointsCollinear(p1, p2, p3))
			{
				// Points are in a straight line, replace the middle point
				_trailPoints[_trailPoints.Count - 1] = newPoint;
			}
			else
			{
				// Corner detected, add new point
				_trailPoints.Add(newPoint);
			}
		}

		// Update visuals
		UpdateTrailVisual();
		UpdateTrailCollision();
	}

	/// <summary>
	/// Checks if three points are collinear (in a straight line)
	/// </summary>
	private bool ArePointsCollinear(Vector2 p1, Vector2 p2, Vector2 p3)
	{
		// Use cross product to check collinearity
		// If cross product is ~0, points are collinear
		Vector2 v1 = p2 - p1;
		Vector2 v2 = p3 - p2;

		float crossProduct = v1.X * v2.Y - v1.Y * v2.X;

		// Allow small threshold for floating point errors
		return Mathf.Abs(crossProduct) < 0.1f;
	}

	/// <summary>
	/// Removes old trail points that are too far away or exceed max count
	/// </summary>
	private void CleanupOldTrailPoints()
	{
		// Limit total trail points
		if (_trailPoints.Count > MAX_TRAIL_POINTS)
		{
			int pointsToRemove = _trailPoints.Count - MAX_TRAIL_POINTS;
			_trailPoints.RemoveRange(0, pointsToRemove);

			// Update visuals after cleanup
			UpdateTrailVisual();
			UpdateTrailCollision();

			GD.Print($"[Player] Trail cleanup: removed {pointsToRemove} old points");
		}

		// Also remove points that are very far from player (remove from start of list - oldest points)
		int removedCount = 0;
		while (_trailPoints.Count > 0 && _trailPoints[0].DistanceTo(GlobalPosition) > TRAIL_CLEANUP_DISTANCE)
		{
			_trailPoints.RemoveAt(0);
			removedCount++;
		}

		if (removedCount > 0)
		{
			UpdateTrailVisual();
			UpdateTrailCollision();
			GD.Print($"[Player] Trail cleanup: removed {removedCount} distant points");
		}
	}

	/// <summary>
	/// Updates the trail line visual
	/// </summary>
	private void UpdateTrailVisual()
	{
		if (_trailPoints.Count > 0)
		{
			// Trail points are in global coordinates
			// TrailLine is now in world space at (0,0), so global coords work directly
			_trailLine.Points = _trailPoints.ToArray();
			_trailLine.DefaultColor = new Color(0, 1, 1, 0.8f);
			_trailLine.Width = TRAIL_WIDTH;
		}
	}

	/// <summary>
	/// Updates the trail collision polygon
	/// </summary>
	private void UpdateTrailCollision()
	{
		if (_trailPoints.Count < 2)
		{
			_trailCollisionShape.Polygon = Array.Empty<Vector2>();
			return;
		}

		// Filter out trail points that are too close to the player
		// This prevents collision with recently-laid trail during turns
		List<Vector2> validTrailPoints = new List<Vector2>();
		foreach (var point in _trailPoints)
		{
			if (point.DistanceTo(GlobalPosition) >= TRAIL_COLLISION_SAFE_DISTANCE)
			{
				validTrailPoints.Add(point);
			}
		}

		// Need at least 2 points to make a collision shape
		if (validTrailPoints.Count < 2)
		{
			_trailCollisionShape.Polygon = Array.Empty<Vector2>();
			return;
		}

		// Create a collision polygon with width around the trail line
		// Trail points are in global coords, TrailCollision is now at world (0,0)
		List<Vector2> collisionPoints = new List<Vector2>();
		float halfWidth = TRAIL_WIDTH / 2.0f;

		// Create offset points on both sides of the trail
		for (int i = 0; i < validTrailPoints.Count - 1; i++)
		{
			Vector2 current = validTrailPoints[i];
			Vector2 next = validTrailPoints[i + 1];
			Vector2 direction = (next - current).Normalized();
			Vector2 perpendicular = new Vector2(-direction.Y, direction.X) * halfWidth;

			if (i == 0)
			{
				collisionPoints.Add(current + perpendicular);
			}
			collisionPoints.Add(next + perpendicular);
		}

		// Add points in reverse for the other side
		for (int i = validTrailPoints.Count - 1; i >= 0; i--)
		{
			Vector2 point = validTrailPoints[i];

			if (i > 0)
			{
				Vector2 prev = validTrailPoints[i - 1];
				Vector2 direction = (point - prev).Normalized();
				Vector2 perpendicular = new Vector2(-direction.Y, direction.X) * halfWidth;
				collisionPoints.Add(point - perpendicular);
			}
			else
			{
				Vector2 next = validTrailPoints[i + 1];
				Vector2 direction = (next - point).Normalized();
				Vector2 perpendicular = new Vector2(-direction.Y, direction.X) * halfWidth;
				collisionPoints.Add(point - perpendicular);
			}
		}

		_trailCollisionShape.Polygon = collisionPoints.ToArray();
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
	private void _OnTrailCollisionBodyEntered(Node2D body)
	{
		// Check if it's the player hitting their own trail
		if (body == this)
		{
			// The collision polygon already excludes nearby trail points
			// So if we're colliding, it's definitely a real hit

			if (_shieldState == ShieldState.Active)
			{
				// Break trail at contact point
				BreakTrail(GlobalPosition);
				GD.Print("[Player] Shield absorbed trail collision!");
			}
			else
			{
				// Player dies
				Die();
			}
		}
	}

	/// <summary>
	/// Breaks the trail at a specific contact point
	/// </summary>
	private void BreakTrail(Vector2 contactPoint)
	{
		const float BREAK_RADIUS = 15.0f;
		int pointsRemoved = 0;

		// Remove trail points within the break radius
		for (int i = _trailPoints.Count - 1; i >= 0; i--)
		{
			if (_trailPoints[i].DistanceTo(contactPoint) <= BREAK_RADIUS)
			{
				_trailPoints.RemoveAt(i);
				pointsRemoved++;
			}
		}

		if (pointsRemoved > 0)
		{
			// Update visuals and collision
			UpdateTrailVisual();
			UpdateTrailCollision();

			GD.Print($"[Player] Trail broken! Removed {pointsRemoved} points");

			// TODO: Spawn particle effect at contact point
			// For now, just print
			GD.Print($"[Player] Trail break effect at {contactPoint}");
		}
	}

	// ========== DEATH HANDLING ==========
	/// <summary>
	/// Handles player death
	/// </summary>
	private void Die()
	{
		GD.Print("═══════════════════════════════");
		GD.Print("[Player] PLAYER DEATH");
		GD.Print($"[Player] Position: {GlobalPosition}");
		GD.Print($"[Player] Trail points: {_trailPoints.Count}");
		GD.Print("═══════════════════════════════");

		// Stop movement
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
		_currentDirection = 0; // Face right
		Rotation = 0;
		_inputEnabled = true;

		ClearTrail();

		GD.Print($"[Player] Position reset to {pos}");
	}

	/// <summary>
	/// Clears all trail points
	/// </summary>
	public void ClearTrail()
	{
		_trailPoints.Clear();
		_distanceSinceLastTrailPoint = 0.0f;

		_trailLine.Points = Array.Empty<Vector2>();
		_trailCollisionShape.Polygon = Array.Empty<Vector2>();

		GD.Print("[Player] Trail cleared");
	}

	// ========== DEBUG ==========
	/// <summary>
	/// Prints debug information every 60 frames (roughly 1 second)
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

		GD.Print($"[Player] Pos: {GlobalPosition:F0} | Dir: {directionName} | Speed: {Velocity.Length():F0} | Trail: {_trailPoints.Count} pts | Shield: {shieldStatus}");
	}
}
