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
	private Node2D _trailRenderer; // Container for trail segments
	private Area2D _trailCollision;
	private CollisionPolygon2D _trailCollisionShape;

	// ========== MOVEMENT STATE ==========
	public int GridSize = 50; // Set by Main scene
	public int GridExtent = 2000; // Set by Main scene
	private int _currentDirection = 0; // 0=right, 1=down, 2=left, 3=up
	private int? _queuedDirection = null; // Queued turn waiting for grid alignment
	private bool _inputEnabled = true;

	// ========== TRAIL STATE ==========
	private List<List<Vector2>> _trailSegments = new List<List<Vector2>>(); // Trail stored as separate segments
	private List<Vector2> _currentSegment = new List<Vector2>(); // Current segment being built
	private float _distanceSinceLastTrailPoint = 0.0f;
	private const float TRAIL_POINT_DISTANCE = 5.0f;
	private const float TRAIL_WIDTH = 4.0f;
	private const float TRAIL_COLLISION_SAFE_DISTANCE = 25.0f; // Don't collide with trail within this radius

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
		_trailRenderer = GetNode<Node2D>("TrailRenderer");
		_trailCollision = GetNode<Area2D>("TrailRenderer/TrailCollision");
		_trailCollisionShape = GetNode<CollisionPolygon2D>("TrailRenderer/TrailCollision/TrailCollisionShape");

		// Remove the old TrailLine node if it exists (we'll create segments dynamically)
		var oldTrailLine = GetNodeOrNull<Line2D>("TrailRenderer/TrailLine");
		if (oldTrailLine != null)
		{
			oldTrailLine.QueueFree();
		}

		// Move trail renderer to world space so it doesn't move with player
		RemoveChild(_trailRenderer);
		GetParent().AddChild(_trailRenderer);
		_trailRenderer.GlobalPosition = Vector2.Zero;

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
		Vector2 snappedPos = GlobalPosition;

		if (_currentDirection == 0 || _currentDirection == 2) // Right or Left
		{
			// Snap Y to grid
			snappedPos.Y = Mathf.Round(snappedPos.Y / GridSize) * GridSize;
		}
		else // Up or Down
		{
			// Snap X to grid
			snappedPos.X = Mathf.Round(snappedPos.X / GridSize) * GridSize;
		}

		GlobalPosition = snappedPos;
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
	}

	/// <summary>
	/// Adds a trail point, merging collinear points to reduce trail size
	/// </summary>
	private void AddTrailPoint(Vector2 newPoint)
	{
		if (_currentSegment.Count == 0)
		{
			// First point in current segment
			_currentSegment.Add(newPoint);
		}
		else if (_currentSegment.Count == 1)
		{
			// Second point in current segment
			_currentSegment.Add(newPoint);
		}
		else
		{
			// Check if last 3 points are collinear (in a straight line)
			Vector2 p1 = _currentSegment[_currentSegment.Count - 2];
			Vector2 p2 = _currentSegment[_currentSegment.Count - 1];
			Vector2 p3 = newPoint;

			if (ArePointsCollinear(p1, p2, p3))
			{
				// Points are in a straight line, replace the middle point
				_currentSegment[_currentSegment.Count - 1] = newPoint;
			}
			else
			{
				// Corner detected, add new point
				_currentSegment.Add(newPoint);
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
	/// Updates the trail line visual
	/// </summary>
	private void UpdateTrailVisual()
	{
		// Clear all existing trail line segments - collect first to avoid modification during iteration
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

		// Render all completed segments
		foreach (var segment in _trailSegments)
		{
			if (segment.Count >= 2)
			{
				CreateTrailSegment(segment);
			}
		}

		// Render current segment being built
		if (_currentSegment.Count >= 2)
		{
			CreateTrailSegment(_currentSegment);
		}
	}

	/// <summary>
	/// Creates a Line2D segment for a continuous portion of the trail
	/// </summary>
	private void CreateTrailSegment(List<Vector2> points)
	{
		var line = new Line2D();
		line.Points = points.ToArray();
		line.DefaultColor = new Color(0, 1, 1, 0.8f); // Cyan
		line.Width = TRAIL_WIDTH;

		// Add additive blend for glow effect
		var material = new CanvasItemMaterial();
		material.BlendMode = CanvasItemMaterial.BlendModeEnum.Add;
		line.Material = material;

		_trailRenderer.AddChild(line);
	}

	/// <summary>
	/// Gets all trail points from all segments
	/// </summary>
	private List<Vector2> GetAllTrailPoints()
	{
		List<Vector2> allPoints = new List<Vector2>();
		foreach (var segment in _trailSegments)
		{
			allPoints.AddRange(segment);
		}
		allPoints.AddRange(_currentSegment);
		return allPoints;
	}

	/// <summary>
	/// Updates the trail collision polygon
	/// </summary>
	private void UpdateTrailCollision()
	{
		var allTrailPoints = GetAllTrailPoints();
		if (allTrailPoints.Count < 2)
		{
			_trailCollisionShape.Polygon = Array.Empty<Vector2>();
			return;
		}

		// Filter out trail points that are too close to the player
		// This prevents collision with recently-laid trail during turns
		List<Vector2> validTrailPoints = new List<Vector2>();
		foreach (var point in allTrailPoints)
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
				GD.Print($"[Player] Shield collision detected at {GlobalPosition}");
				GD.Print($"[Player] Current segments: {_trailSegments.Count}, Current segment points: {_currentSegment.Count}");

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
	/// Breaks the trail at a specific contact point by creating a gap
	/// </summary>
	private void BreakTrail(Vector2 contactPoint)
	{
		const float GAP_SIZE = 30.0f; // Total gap size
		const float HALF_GAP = GAP_SIZE / 2.0f;

		GD.Print($"[Player] BreakTrail called at {contactPoint}, searching {_trailSegments.Count} completed segments and current segment with {_currentSegment.Count} points");

		// Find the closest line segment across all trail segments
		int closestSegmentListIndex = -1; // Which segment list (completed or current)
		int closestPointIndex = -1; // Which point pair within that segment
		float closestDistance = float.MaxValue;
		Vector2 closestPointOnSegment = Vector2.Zero;
		List<Vector2> closestSegment = null;

		// Search completed segments
		for (int segIdx = 0; segIdx < _trailSegments.Count; segIdx++)
		{
			var segment = _trailSegments[segIdx];
			for (int i = 0; i < segment.Count - 1; i++)
			{
				Vector2 pointOnSegment = ClosestPointOnLineSegment(contactPoint, segment[i], segment[i + 1]);
				float distance = contactPoint.DistanceTo(pointOnSegment);

				if (distance < closestDistance)
				{
					closestDistance = distance;
					closestSegmentListIndex = segIdx;
					closestPointIndex = i;
					closestPointOnSegment = pointOnSegment;
					closestSegment = segment;
				}
			}
		}

		// Search current segment
		for (int i = 0; i < _currentSegment.Count - 1; i++)
		{
			Vector2 pointOnSegment = ClosestPointOnLineSegment(contactPoint, _currentSegment[i], _currentSegment[i + 1]);
			float distance = contactPoint.DistanceTo(pointOnSegment);

			if (distance < closestDistance)
			{
				closestDistance = distance;
				closestSegmentListIndex = -2; // Special marker for current segment
				closestPointIndex = i;
				closestPointOnSegment = pointOnSegment;
				closestSegment = _currentSegment;
			}
		}

		GD.Print($"[Player] Closest segment found: listIdx={closestSegmentListIndex}, pointIdx={closestPointIndex}, distance={closestDistance:F1}");

		if (closestSegment == null || closestDistance > 50.0f)
		{
			GD.Print($"[Player] Shield miss - no trail segment nearby (distance: {closestDistance:F1}, threshold: 50.0)");
			return;
		}

		// Calculate gap endpoints
		Vector2 segmentStart = closestSegment[closestPointIndex];
		Vector2 segmentEnd = closestSegment[closestPointIndex + 1];
		Vector2 segmentDir = (segmentEnd - segmentStart).Normalized();
		Vector2 gapStart = closestPointOnSegment - segmentDir * HALF_GAP;
		Vector2 gapEnd = closestPointOnSegment + segmentDir * HALF_GAP;

		// Split the segment at the gap
		List<Vector2> beforeGap = new List<Vector2>();
		List<Vector2> afterGap = new List<Vector2>();

		// Points before the break
		for (int i = 0; i <= closestPointIndex; i++)
		{
			beforeGap.Add(closestSegment[i]);
		}
		beforeGap.Add(gapStart);

		// Points after the break
		afterGap.Add(gapEnd);
		for (int i = closestPointIndex + 1; i < closestSegment.Count; i++)
		{
			afterGap.Add(closestSegment[i]);
		}

		// Update the appropriate segment list
		if (closestSegmentListIndex == -2)
		{
			// Breaking current segment - finish it and start a new one
			if (beforeGap.Count >= 2)
			{
				_trailSegments.Add(beforeGap);
			}
			_currentSegment = afterGap;
		}
		else
		{
			// Breaking a completed segment - replace with two segments
			_trailSegments.RemoveAt(closestSegmentListIndex);
			if (beforeGap.Count >= 2)
			{
				_trailSegments.Insert(closestSegmentListIndex, beforeGap);
			}
			if (afterGap.Count >= 2)
			{
				_trailSegments.Insert(closestSegmentListIndex + (beforeGap.Count >= 2 ? 1 : 0), afterGap);
			}
		}

		// Update visuals and collision
		UpdateTrailVisual();
		UpdateTrailCollision();

		GD.Print($"[Player] Trail broken! Created {GAP_SIZE}px gap at {closestPointOnSegment}");
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
		GD.Print($"[Player] Trail points: {GetAllTrailPoints().Count}");
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
		_trailSegments.Clear();
		_currentSegment.Clear();
		_distanceSinceLastTrailPoint = 0.0f;

		// Clear all trail line segments
		foreach (Node child in _trailRenderer.GetChildren())
		{
			if (child is Line2D)
			{
				child.QueueFree();
			}
		}

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

		GD.Print($"[Player] Pos: {GlobalPosition:F0} | Dir: {directionName} | Speed: {Velocity.Length():F0} | Trail: {GetAllTrailPoints().Count} pts | Shield: {shieldStatus}");
	}
}
