using Godot;
using System;
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
/// Centralized manager for all trail walls in the game.
/// Handles trail rendering, collision, and cleanup.
/// </summary>
public partial class TrailManager : Node2D
{
	// ========== SINGLETON ==========
	private static TrailManager _instance;
	public static TrailManager Instance => _instance;

	// ========== TRAIL STORAGE ==========
	private Dictionary<Node2D, CycleTrailData> _cycleTrails = new Dictionary<Node2D, CycleTrailData>();

	// ========== CONSTANTS ==========
	private const float TRAIL_WIDTH = 4.0f;

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
		GD.Print("[TrailManager] ✓ Singleton instance created");
	}

	public override void _Ready()
	{
		GD.Print("[TrailManager] Trail management system ready");
	}

	// ========== PUBLIC API ==========

	/// <summary>
	/// Registers a cycle (player or enemy) with the trail system
	/// </summary>
	public void RegisterCycle(Node2D cycle, Color trailColor, Node2D trailRenderer, Area2D trailCollision)
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
			TrailCollision = trailCollision,
			Walls = new List<TrailWall>(),
			CurrentWallStart = cycle.GlobalPosition,
			HasWallStart = true,
			LastWallIndex = -1
		};

		_cycleTrails[cycle] = trailData;
		GD.Print($"[TrailManager] Registered cycle: {cycle.Name} (Type: {cycle.GetType().Name})");
		GD.Print($"[TrailManager]   - TrailCollision instance ID: {trailCollision.GetInstanceId()}");
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
			trailData.LastWallIndex = trailData.Walls.Count - 1;
		}

		// Start new wall at turn point
		trailData.CurrentWallStart = turnPosition;
		trailData.HasWallStart = true;

		// Update visuals and collision
		UpdateTrailVisual(trailData);
		UpdateTrailCollision(trailData);
	}

	/// <summary>
	/// Updates trail visual for a cycle (called every frame)
	/// </summary>
	public void UpdateCycleTrail(Node2D cycle)
	{
		if (!_cycleTrails.TryGetValue(cycle, out var trailData))
			return;

		UpdateTrailVisual(trailData);
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
		trailData.LastWallIndex = -1;
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
			// Skip the last wall (no collision yet)
			if (i == trailData.LastWallIndex)
				continue;

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
			GD.Print($"[TrailManager] No wall found near position {position}");
			return;
		}

		// Remove the wall
		trailData.Walls.RemoveAt(hitWallIndex);

		// Update last wall index
		if (trailData.LastWallIndex > hitWallIndex)
			trailData.LastWallIndex--;
		else if (trailData.LastWallIndex == hitWallIndex)
			trailData.LastWallIndex = -1;

		GD.Print($"[TrailManager] Wall destroyed at index {hitWallIndex}");

		// Update visuals and collision
		UpdateTrailVisual(trailData);
		UpdateTrailCollision(trailData);
	}

	/// <summary>
	/// Checks if a position collides with any trail wall
	/// </summary>
	public bool CheckCollisionWithTrails(Vector2 position, Node2D ignoreCycle = null)
	{
		foreach (var kvp in _cycleTrails)
		{
			// Skip checking against own trails if specified
			if (ignoreCycle != null && kvp.Key == ignoreCycle)
				continue;

			var trailData = kvp.Value;
			for (int i = 0; i < trailData.Walls.Count; i++)
			{
				// Skip the last wall
				if (i == trailData.LastWallIndex)
					continue;

				Vector2 closestPoint = ClosestPointOnLineSegment(position, trailData.Walls[i].Start, trailData.Walls[i].End);
				if (position.DistanceTo(closestPoint) < TRAIL_WIDTH)
					return true;
			}
		}

		return false;
	}

	// ========== PRIVATE METHODS ==========

	private void UpdateTrailVisual(CycleTrailData trailData)
	{
		if (trailData.TrailRenderer == null || !IsInstanceValid(trailData.TrailRenderer))
			return;

		// Clear existing trail lines
		ClearChildrenOfType<Line2D>(trailData.TrailRenderer, removeBeforeFreeing: true);

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

	private void UpdateTrailCollision(CycleTrailData trailData)
	{
		if (trailData.TrailCollision == null || !IsInstanceValid(trailData.TrailCollision))
			return;

		// Debug: Log which cycle's collision we're updating
		string cycleInfo = trailData.Owner != null && IsInstanceValid(trailData.Owner)
			? $"{trailData.Owner.Name} ({trailData.Owner.GetType().Name})"
			: "Unknown";
		GD.Print($"[TrailManager] Updating collision for {cycleInfo}, TrailCollision ID:{trailData.TrailCollision.GetInstanceId()}");

		// Clear existing collision shapes
		ClearChildrenOfType<CollisionPolygon2D>(trailData.TrailCollision, removeBeforeFreeing: true);

		// Create collision shape for each wall (except the most recent one)
		for (int i = 0; i < trailData.Walls.Count; i++)
		{
			if (i == trailData.LastWallIndex)
				continue;

			CreateCollisionForWall(trailData.Walls[i], trailData.TrailCollision);
		}
	}

	private void CreateWallLine(TrailWall wall, Color color, Node2D container)
	{
		if (wall.Start.DistanceTo(wall.End) < 1.0f)
			return;

		var line = new Line2D();
		line.Points = new Vector2[] { wall.Start, wall.End };
		line.DefaultColor = color;
		line.Width = TRAIL_WIDTH;

		var material = new CanvasItemMaterial();
		material.BlendMode = CanvasItemMaterial.BlendModeEnum.Add;
		line.Material = material;

		container.AddChild(line);
	}

	private void CreateCollisionForWall(TrailWall wall, Area2D collisionArea)
	{
		Vector2 direction = (wall.End - wall.Start).Normalized();
		Vector2 perpendicular = new Vector2(-direction.Y, direction.X) * (TRAIL_WIDTH / 2.0f);

		Vector2[] collisionPoints = new Vector2[]
		{
			wall.Start + perpendicular,
			wall.End + perpendicular,
			wall.End - perpendicular,
			wall.Start - perpendicular
		};

		var collisionShape = new CollisionPolygon2D();
		collisionShape.Polygon = collisionPoints;
		collisionArea.AddChild(collisionShape);

		// Debug: Log collision shape creation with instance ID to track which trail this belongs to
		GD.Print($"[TrailManager] Created collision shape for TrailCollision ID:{collisionArea.GetInstanceId()} at {wall.Start} to {wall.End}");
		GD.Print($"[TrailManager]   - CollisionArea children count: {collisionArea.GetChildCount()}");
	}

	/// <summary>
	/// Generic helper to clear all children of a specific type from a parent node
	/// </summary>
	private void ClearChildrenOfType<T>(Node parent, bool removeBeforeFreeing = false) where T : Node
	{
		if (parent == null || !IsInstanceValid(parent))
			return;

		if (removeBeforeFreeing)
		{
			// Two-step process: collect, then remove and free
			var childrenToRemove = new List<Node>();
			foreach (Node child in parent.GetChildren())
			{
				if (child is T)
					childrenToRemove.Add(child);
			}
			foreach (Node child in childrenToRemove)
			{
				parent.RemoveChild(child);
				child.QueueFree();
			}
		}
		else
		{
			// Direct queue free
			foreach (Node child in parent.GetChildren())
			{
				if (child is T)
					child.QueueFree();
			}
		}
	}

	private void ClearCycleTrails(CycleTrailData trailData)
	{
		// Clear trail visuals
		ClearChildrenOfType<Line2D>(trailData.TrailRenderer);

		// Clear collision shapes
		ClearChildrenOfType<CollisionPolygon2D>(trailData.TrailCollision);
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
		public Area2D TrailCollision;
		public List<TrailWall> Walls;
		public Vector2 CurrentWallStart;
		public bool HasWallStart;
		public int LastWallIndex;
	}
}
