using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Represents a single trail wall segment with start and end points
/// </summary>
public struct TrailWall
{
	public Vector2 Start;
	public Vector2 End;

	public TrailWall(Vector2 start, Vector2 end)
	{
		Start = start;
		End = end;
	}
}

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
	private Node2D _trailRenderer; // Container for trail segments
	private Area2D _trailCollision; // Container for collision shapes (created dynamically)

	// ========== MOVEMENT STATE ==========
	public int GridSize = 50; // Set by Main scene
	public int GridExtent = 2000; // Set by Main scene
	private int _currentDirection = 0; // 0=right, 1=down, 2=left, 3=up
	private int? _queuedDirection = null; // Queued turn waiting for grid alignment
	private bool _inputEnabled = true;

	// ========== TRAIL STATE ==========
	private List<TrailWall> _walls = new List<TrailWall>(); // All completed walls
	private Vector2 _currentWallStart = Vector2.Zero; // Start point of wall currently being laid
	private int _lastWallIndex = -1; // Index of most recent wall - no collision until next turn
	private const float TRAIL_WIDTH = 4.0f;

	// ========== SHIELD STATE ==========
	private enum ShieldState { Ready, Active, Cooldown }
	private ShieldState _shieldState = ShieldState.Ready;
	private float _shieldTimer = 0.0f;
	private float _shieldPulseTime = 0.0f;
	private bool _shieldBrokeTrailThisActivation = false; // Track if we already broke trail this activation

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

		// Remove old trail nodes if they exist (we'll create segments dynamically)
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

		// Move trail renderer to world space so it doesn't move with player
		RemoveChild(_trailRenderer);
		GetParent().AddChild(_trailRenderer);
		_trailRenderer.GlobalPosition = Vector2.Zero;

		GD.Print("[Player] ✓ Trail renderer moved to world space");

		// Generate visuals
		GenerateWedgeGeometry();
		GenerateShieldGeometry();

		// Initialize wall start at player spawn position
		_currentWallStart = GlobalPosition;

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

		// Check if player left the grid boundary
		CheckBoundary();

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
	/// Checks if player has left the grid boundary and dies if so
	/// </summary>
	private void CheckBoundary()
	{
		if (!_inputEnabled) return; // Already dead

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
			// Queue turn left (counter-clockwise)
			_queuedDirection = (_currentDirection + 3) % 4;
		}
		else if (Input.IsActionJustPressed("move_right") && _queuedDirection == null)
		{
			// Queue turn right (clockwise)
			_queuedDirection = (_currentDirection + 1) % 4;
		}

		// Check if we can execute queued turn (grid-aligned)
		if (_queuedDirection.HasValue && IsAlignedToGrid())
		{
			// Snap position to grid
			SnapToGrid();

			// Finalize current wall before turning
			if (_currentWallStart != Vector2.Zero)
			{
				var wall = new TrailWall(_currentWallStart, GlobalPosition);
				_walls.Add(wall);

				// The newly added wall becomes the "last wall" - no collision until next turn
				_lastWallIndex = _walls.Count - 1;

				GD.Print($"[Player] Wall completed: {_currentWallStart} -> {GlobalPosition} (index {_lastWallIndex}, no collision yet)");
			}

			// Start new wall at turn point
			_currentWallStart = GlobalPosition;

			// Execute the turn
			_currentDirection = _queuedDirection.Value;
			_queuedDirection = null;
			UpdateRotationFromDirection();

			// Update visuals and collision
			UpdateTrailVisual();
			UpdateTrailCollision();

			GD.Print($"[Player] Turn executed at {GlobalPosition}");
		}

		// Always move forward in current direction
		Vector2 moveDirection = GetDirectionVector();
		Velocity = moveDirection * MoveSpeed;
	}

	/// <summary>
	/// Checks if player is aligned to grid for turning
	/// </summary>
	private bool IsAlignedToGrid()
	{
		// When moving horizontally, Y is already aligned, check if X is aligned (at intersection)
		// When moving vertically, X is already aligned, check if Y is aligned (at intersection)
		if (_currentDirection == 0 || _currentDirection == 2) // Right or Left (horizontal)
		{
			// Check X alignment (changing axis)
			float remainder = Mathf.Abs(GlobalPosition.X) % GridSize;
			return remainder < 2.0f || remainder > (GridSize - 2.0f);
		}
		else // Up or Down (vertical)
		{
			// Check Y alignment (changing axis)
			float remainder = Mathf.Abs(GlobalPosition.Y) % GridSize;
			return remainder < 2.0f || remainder > (GridSize - 2.0f);
		}
	}

	/// <summary>
	/// Snaps player position to nearest grid line
	/// </summary>
	private void SnapToGrid()
	{
		// When turning at grid intersection, snap BOTH X and Y to grid
		Vector2 snappedPos = GlobalPosition;
		snappedPos.X = Mathf.Round(snappedPos.X / GridSize) * GridSize;
		snappedPos.Y = Mathf.Round(snappedPos.Y / GridSize) * GridSize;
		GlobalPosition = snappedPos;

		GD.Print($"[Player] Snapped to grid: {GlobalPosition}");
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
	/// Updates the trail (walls created on turns, this updates current wall visual)
	/// </summary>
	private void UpdateTrail()
	{
		// Update visual to show current wall being laid from last turn to player position
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
		if (_currentWallStart != Vector2.Zero && GlobalPosition != _currentWallStart)
		{
			var currentWall = new TrailWall(_currentWallStart, GlobalPosition);
			CreateWallLine(currentWall);
		}
	}

	/// <summary>
	/// Creates a Line2D for a single wall
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
		line.DefaultColor = new Color(0, 1, 1, 0.8f); // Cyan
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
			// Skip the last wall - player just turned there, collision enabled on next turn
			if (i == _lastWallIndex)
			{
				continue;
			}

			CreateCollisionForWall(_walls[i]);
		}

		// Don't create collision for current wall being laid (it's behind the player)
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
		_shieldBrokeTrailThisActivation = false; // Reset break flag
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
				// Only break trail once per shield activation
				if (!_shieldBrokeTrailThisActivation)
				{
					GD.Print($"[Player] Shield collision detected at {GlobalPosition}");

					// Find the closest wall to the player and break it
					BreakClosestWall();
					_shieldBrokeTrailThisActivation = true;

					GD.Print("[Player] Shield absorbed trail collision!");
				}
				// Subsequent collisions during same activation are ignored
			}
			else
			{
				// Player dies
				Die();
			}
		}
	}

	/// <summary>
	/// Finds and breaks the closest wall to the player
	/// </summary>
	private void BreakClosestWall()
	{
		const float HALF_GAP = 15.0f; // 15 pixels on each side = 30px total gap

		GD.Print($"[Player] Breaking closest wall at player position {GlobalPosition}, searching {_walls.Count} walls");

		// Find the closest wall to the player
		int hitWallIndex = -1;
		float closestDistance = float.MaxValue;
		Vector2 contactPoint = Vector2.Zero;

		for (int i = 0; i < _walls.Count; i++)
		{
			// Skip the last wall (no collision yet)
			if (i == _lastWallIndex)
				continue;

			Vector2 closestPoint = ClosestPointOnLineSegment(GlobalPosition, _walls[i].Start, _walls[i].End);
			float distance = GlobalPosition.DistanceTo(closestPoint);

			if (distance < closestDistance)
			{
				closestDistance = distance;
				hitWallIndex = i;
				contactPoint = closestPoint;
			}
		}

		if (hitWallIndex == -1 || closestDistance > 20.0f)
		{
			GD.Print($"[Player] No wall found near player (closest distance: {closestDistance:F1})");
			return;
		}

		TrailWall hitWall = _walls[hitWallIndex];
		GD.Print($"[Player] Hit wall #{hitWallIndex}: {hitWall.Start} -> {hitWall.End}, contact at {contactPoint}");

		// Get player's right vector (perpendicular to forward direction)
		Vector2 forwardDir = GetDirectionVector();
		Vector2 rightDir = new Vector2(-forwardDir.Y, forwardDir.X);

		// Calculate gap endpoints: +/- 15 pixels along player's right vector from contact point
		Vector2 gapPoint1 = contactPoint + rightDir * HALF_GAP;
		Vector2 gapPoint2 = contactPoint - rightDir * HALF_GAP;

		GD.Print($"[Player] Gap points: {gapPoint1} and {gapPoint2}");

		// Create two new walls with the gap
		// Wall 1: original start -> gapPoint2  (the -15 pixel point)
		// Wall 2: gapPoint1 -> original end   (the +15 pixel point)
		TrailWall wall1 = new TrailWall(hitWall.Start, gapPoint2);
		TrailWall wall2 = new TrailWall(gapPoint1, hitWall.End);

		// Replace the hit wall with two new walls
		_walls.RemoveAt(hitWallIndex);
		_walls.Insert(hitWallIndex, wall1);
		_walls.Insert(hitWallIndex + 1, wall2);

		// Update _lastWallIndex if needed (we added an extra wall)
		if (_lastWallIndex >= hitWallIndex)
		{
			_lastWallIndex++; // Shift index forward since we inserted a wall
		}

		GD.Print($"[Player] Wall broken! Created 30px gap. New walls: [{wall1.Start}->{wall1.End}] and [{wall2.Start}->{wall2.End}]");

		// Update visuals and collision
		UpdateTrailVisual();
		UpdateTrailCollision();
	}

	/// <summary>
	/// Finds the closest point on a line segment to a given point
	/// </summary>
	private Vector2 ClosestPointOnLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
	{
		Vector2 line = lineEnd - lineStart;
		float lineLength = line.Length();

		if (lineLength < 0.001f)
			return lineStart; // Degenerate segment

		Vector2 lineDir = line / lineLength;
		Vector2 toPoint = point - lineStart;

		float projection = toPoint.Dot(lineDir);
		projection = Mathf.Clamp(projection, 0, lineLength);

		return lineStart + lineDir * projection;
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
		GD.Print($"[Player] Trail walls: {_walls.Count}");
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
	/// Clears all trail walls
	/// </summary>
	public void ClearTrail()
	{
		_walls.Clear();
		_currentWallStart = GlobalPosition;
		_lastWallIndex = -1; // Reset last wall tracking

		// Clear all trail line visuals
		foreach (Node child in _trailRenderer.GetChildren())
		{
			if (child is Line2D)
			{
				child.QueueFree();
			}
		}

		// Clear all collision shapes
		foreach (Node child in _trailCollision.GetChildren())
		{
			if (child is CollisionPolygon2D)
			{
				child.QueueFree();
			}
		}

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

		GD.Print($"[Player] Pos: {GlobalPosition:F0} | Dir: {directionName} | Speed: {Velocity.Length():F0} | Walls: {_walls.Count} | Shield: {shieldStatus}");
	}
}
