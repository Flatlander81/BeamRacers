using Godot;
using System.Collections.Generic;

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
/// Centralized manager for trail rendering and grid-based collision.
/// Uses GridCollisionManager for instant, deterministic collision detection.
/// </summary>
public partial class TrailManager : Node2D
{
	// ========== SINGLETON ==========
	private static TrailManager _instance;
	public static TrailManager Instance => _instance;

	// ========== TRAIL STORAGE ==========
	private Dictionary<Node2D, CycleTrailData> _cycleTrails = new Dictionary<Node2D, CycleTrailData>();

	// ========== CONSTANTS ==========
	private const float TRAIL_VISUAL_WIDTH = 4.0f;

	// ========== INITIALIZATION ==========
	public override void _EnterTree()
	{
		if (_instance != null && _instance != this)
		{
			GD.PrintErr("[TrailManager] ERROR: Multiple TrailManager instances detected!");
			QueueFree();
			return;
		}

		_instance = this;
	}

	public override void _Ready()
	{
		GD.Print("[TrailManager] Trail management system ready");
	}

	// ========== PUBLIC API ==========

	/// <summary>
	/// Registers a cycle (player or enemy) with the trail system
	/// </summary>
	public void RegisterCycle(Node2D cycle, Color trailColor, Node2D trailRenderer, CellOccupant trailType)
	{
		if (_cycleTrails.ContainsKey(cycle))
		{
			GD.PrintErr($"[TrailManager] Cycle {cycle.Name} already registered!");
			return;
		}

		var trailData = new CycleTrailData
		{
			Owner = cycle,
			TrailColor = trailColor,
			TrailRenderer = trailRenderer,
			TrailType = trailType,
			Walls = new List<TrailWall>(),
			CurrentWallStart = cycle.GlobalPosition,
			HasWallStart = true
		};

		_cycleTrails[cycle] = trailData;
		GD.Print($"[TrailManager] Registered cycle: {cycle.Name} (Type: {cycle.GetType().Name})");
	}

	/// <summary>
	/// Unregisters a cycle and cleans up its trails
	/// </summary>
	public void UnregisterCycle(Node2D cycle)
	{
		if (!_cycleTrails.ContainsKey(cycle))
			return;

		var trailData = _cycleTrails[cycle];
		ClearCycleTrails(trailData);
		_cycleTrails.Remove(cycle);

		GD.Print($"[TrailManager] Unregistered cycle: {cycle.Name}");
	}

	/// <summary>
	/// Creates a turn point for a cycle (finalizes current wall and starts new one)
	/// </summary>
	public void CreateTurn(Node2D cycle, Vector2 turnPosition)
	{
		if (!_cycleTrails.TryGetValue(cycle, out var trailData))
			return;

		// Finalize current wall
		if (trailData.HasWallStart)
		{
			var wall = new TrailWall(trailData.CurrentWallStart, turnPosition);
			trailData.Walls.Add(wall);

			// Register wall in grid collision system
			if (GridCollisionManager.Instance != null)
			{
				GridCollisionManager.Instance.SetLine(wall.Start, wall.End, trailData.TrailType);
			}
		}

		// Start new wall at turn point
		trailData.CurrentWallStart = turnPosition;
		trailData.HasWallStart = true;

		// Update visuals
		UpdateTrailVisual(trailData);
	}

	/// <summary>
	/// Updates trail visual for a cycle (called every frame)
	/// </summary>
	public void UpdateCycleTrail(Node2D cycle, float delta)
	{
		if (!_cycleTrails.TryGetValue(cycle, out var trailData))
			return;

		// Update visual (includes current wall being drawn)
		UpdateTrailVisual(trailData);

		// NOTE: We don't update grid collision for the actively-being-drawn wall here
		// Only finalized walls (at turns) are marked in the grid
		// This prevents the cycle from immediately hitting its own trail as it draws it
	}

	/// <summary>
	/// Clears all trails for a specific cycle
	/// </summary>
	public void ClearCycleTrails(Node2D cycle)
	{
		if (!_cycleTrails.TryGetValue(cycle, out var trailData))
			return;

		ClearCycleTrails(trailData);

		// Reset state
		trailData.Walls.Clear();
		trailData.CurrentWallStart = cycle.GlobalPosition;
	}

	/// <summary>
	/// Breaks the closest wall to a given position (for shield mechanics)
	/// </summary>
	public void BreakClosestWall(Vector2 position, Node2D cycleOwner)
	{
		if (!_cycleTrails.TryGetValue(cycleOwner, out var trailData))
			return;

		int hitWallIndex = -1;
		float closestDistance = float.MaxValue;

		for (int i = 0; i < trailData.Walls.Count; i++)
		{
			Vector2 closestPoint = ClosestPointOnLineSegment(position, trailData.Walls[i].Start, trailData.Walls[i].End);
			float distance = position.DistanceTo(closestPoint);

			if (distance < closestDistance)
			{
				closestDistance = distance;
				hitWallIndex = i;
			}
		}

		if (hitWallIndex == -1 || closestDistance > 20.0f)
		{
			return;
		}

		// Remove the wall from grid
		if (GridCollisionManager.Instance != null)
		{
			GridCollisionManager.Instance.SetLine(
				trailData.Walls[hitWallIndex].Start,
				trailData.Walls[hitWallIndex].End,
				CellOccupant.Empty
			);
		}

		// Remove from walls list
		trailData.Walls.RemoveAt(hitWallIndex);

		GD.Print($"[TrailManager] Wall destroyed at index {hitWallIndex}");

		// Update visuals
		UpdateTrailVisual(trailData);
	}

	// ========== PRIVATE METHODS ==========

	private void UpdateTrailVisual(CycleTrailData trailData)
	{
		if (trailData.TrailRenderer == null || !IsInstanceValid(trailData.TrailRenderer))
			return;

		// Clear existing trail lines
		foreach (Node child in trailData.TrailRenderer.GetChildren())
		{
			if (child is Line2D)
			{
				child.QueueFree();
			}
		}

		// Render all completed walls
		foreach (var wall in trailData.Walls)
		{
			CreateWallLine(wall, trailData.TrailColor, trailData.TrailRenderer);
		}

		// Render current wall being laid
		if (trailData.HasWallStart && trailData.Owner != null && IsInstanceValid(trailData.Owner))
		{
			Vector2 currentPos = trailData.Owner.GlobalPosition;
			if (currentPos.DistanceTo(trailData.CurrentWallStart) > 1.0f)
			{
				var currentWall = new TrailWall(trailData.CurrentWallStart, currentPos);
				CreateWallLine(currentWall, trailData.TrailColor, trailData.TrailRenderer);
			}
		}
	}

	private void CreateWallLine(TrailWall wall, Color color, Node2D container)
	{
		if (wall.Start.DistanceTo(wall.End) < 1.0f)
			return;

		var line = new Line2D();
		line.Points = new Vector2[] { wall.Start, wall.End };
		line.DefaultColor = color;
		line.Width = TRAIL_VISUAL_WIDTH;

		var material = new CanvasItemMaterial();
		material.BlendMode = CanvasItemMaterial.BlendModeEnum.Add;
		line.Material = material;

		container.AddChild(line);
	}

	private void ClearCycleTrails(CycleTrailData trailData)
	{
		// Clear from grid collision system
		if (GridCollisionManager.Instance != null)
		{
			foreach (var wall in trailData.Walls)
			{
				GridCollisionManager.Instance.SetLine(wall.Start, wall.End, CellOccupant.Empty);
			}
		}

		// Clear trail visuals
		if (trailData.TrailRenderer != null && IsInstanceValid(trailData.TrailRenderer))
		{
			foreach (Node child in trailData.TrailRenderer.GetChildren())
			{
				if (child is Line2D)
				{
					child.QueueFree();
				}
			}
		}
	}

	private Vector2 ClosestPointOnLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
	{
		Vector2 line = lineEnd - lineStart;
		float lineLength = line.Length();

		if (lineLength < 0.001f)
			return lineStart;

		Vector2 lineDir = line / lineLength;
		Vector2 toPoint = point - lineStart;

		float projection = toPoint.Dot(lineDir);
		projection = Mathf.Clamp(projection, 0, lineLength);

		return lineStart + lineDir * projection;
	}

	// ========== NESTED CLASSES ==========

	private class CycleTrailData
	{
		public Node2D Owner;
		public Color TrailColor;
		public Node2D TrailRenderer;
		public CellOccupant TrailType;
		public List<TrailWall> Walls;
		public Vector2 CurrentWallStart;
		public bool HasWallStart;
	}
}
