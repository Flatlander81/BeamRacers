using Godot;
using System.Collections.Generic;

/// <summary>
/// Types of objects that can occupy grid cells
/// </summary>
public enum CellOccupant
{
	Empty,
	Boundary,
	Obstacle,
	PlayerTrail,
	EnemyTrail
}

/// <summary>
/// Centralized grid-based collision system for all game objects.
/// Provides instant, deterministic collision detection without physics engine delays.
/// </summary>
public partial class GridCollisionManager : Node
{
	// ========== SINGLETON ==========
	private static GridCollisionManager _instance;
	public static GridCollisionManager Instance => _instance;

	// ========== GRID STORAGE ==========
	private Dictionary<Vector2I, CellOccupant> _grid = new Dictionary<Vector2I, CellOccupant>();
	private int _gridSize = 50;

	// ========== INITIALIZATION ==========
	public override void _EnterTree()
	{
		if (_instance != null && _instance != this)
		{
			GD.PrintErr("[GridCollisionManager] ERROR: Multiple instances detected!");
			QueueFree();
			return;
		}

		_instance = this;
	}

	public override void _Ready()
	{
		GD.Print("[GridCollisionManager] Grid collision system initialized");
	}

	/// <summary>
	/// Sets the grid cell size (must match cycle GridSize)
	/// </summary>
	public void SetGridSize(int size)
	{
		_gridSize = size;
	}

	// ========== CELL MANAGEMENT ==========

	/// <summary>
	/// Converts world position to grid cell coordinates
	/// </summary>
	public Vector2I WorldToGrid(Vector2 worldPos)
	{
		return new Vector2I(
			Mathf.RoundToInt(worldPos.X / _gridSize),
			Mathf.RoundToInt(worldPos.Y / _gridSize)
		);
	}

	/// <summary>
	/// Converts grid cell coordinates to world position
	/// </summary>
	public Vector2 GridToWorld(Vector2I gridPos)
	{
		return new Vector2(gridPos.X * _gridSize, gridPos.Y * _gridSize);
	}

	/// <summary>
	/// Marks a single grid cell as occupied
	/// </summary>
	public void SetCell(Vector2I gridPos, CellOccupant occupant)
	{
		if (occupant == CellOccupant.Empty)
		{
			_grid.Remove(gridPos);
		}
		else
		{
			_grid[gridPos] = occupant;
		}
	}

	/// <summary>
	/// Marks a single grid cell as occupied (world position)
	/// </summary>
	public void SetCell(Vector2 worldPos, CellOccupant occupant)
	{
		SetCell(WorldToGrid(worldPos), occupant);
	}

	/// <summary>
	/// Gets what occupies a grid cell
	/// </summary>
	public CellOccupant GetCell(Vector2I gridPos)
	{
		return _grid.TryGetValue(gridPos, out var occupant) ? occupant : CellOccupant.Empty;
	}

	/// <summary>
	/// Gets what occupies a grid cell (world position)
	/// </summary>
	public CellOccupant GetCell(Vector2 worldPos)
	{
		return GetCell(WorldToGrid(worldPos));
	}

	/// <summary>
	/// Marks a line of grid cells as occupied (for trails and walls)
	/// Uses Bresenham's line algorithm for accurate coverage
	/// </summary>
	public void SetLine(Vector2 start, Vector2 end, CellOccupant occupant)
	{
		Vector2I gridStart = WorldToGrid(start);
		Vector2I gridEnd = WorldToGrid(end);

		// Bresenham's line algorithm
		int dx = Mathf.Abs(gridEnd.X - gridStart.X);
		int dy = Mathf.Abs(gridEnd.Y - gridStart.Y);
		int sx = gridStart.X < gridEnd.X ? 1 : -1;
		int sy = gridStart.Y < gridEnd.Y ? 1 : -1;
		int err = dx - dy;

		Vector2I current = gridStart;

		while (true)
		{
			SetCell(current, occupant);

			if (current == gridEnd)
				break;

			int e2 = 2 * err;
			if (e2 > -dy)
			{
				err -= dy;
				current.X += sx;
			}
			if (e2 < dx)
			{
				err += dx;
				current.Y += sy;
			}
		}
	}

	/// <summary>
	/// Marks a rectangular area of grid cells as occupied (for obstacles)
	/// </summary>
	public void SetRect(Vector2 center, Vector2 size, CellOccupant occupant)
	{
		Vector2 halfSize = size / 2.0f;
		Vector2I gridMin = WorldToGrid(center - halfSize);
		Vector2I gridMax = WorldToGrid(center + halfSize);

		for (int x = gridMin.X; x <= gridMax.X; x++)
		{
			for (int y = gridMin.Y; y <= gridMax.Y; y++)
			{
				SetCell(new Vector2I(x, y), occupant);
			}
		}
	}

	/// <summary>
	/// Marks a circular area of grid cells as occupied (for pillar obstacles)
	/// </summary>
	public void SetCircle(Vector2 center, float radius, CellOccupant occupant)
	{
		Vector2I gridCenter = WorldToGrid(center);
		int gridRadius = Mathf.CeilToInt(radius / _gridSize);

		for (int x = -gridRadius; x <= gridRadius; x++)
		{
			for (int y = -gridRadius; y <= gridRadius; y++)
			{
				Vector2I gridPos = gridCenter + new Vector2I(x, y);
				Vector2 worldPos = GridToWorld(gridPos);

				if (worldPos.DistanceTo(center) <= radius)
				{
					SetCell(gridPos, occupant);
				}
			}
		}
	}

	// ========== COLLISION CHECKING ==========

	/// <summary>
	/// Checks if a grid cell is blocked for movement
	/// </summary>
	public bool IsCellBlocked(Vector2I gridPos)
	{
		CellOccupant occupant = GetCell(gridPos);
		return occupant != CellOccupant.Empty;
	}

	/// <summary>
	/// Checks if a world position is blocked for movement
	/// </summary>
	public bool IsCellBlocked(Vector2 worldPos)
	{
		return IsCellBlocked(WorldToGrid(worldPos));
	}

	/// <summary>
	/// Checks if a grid cell is blocked specifically by a trail
	/// </summary>
	public bool IsCellBlockedByTrail(Vector2I gridPos)
	{
		CellOccupant occupant = GetCell(gridPos);
		return occupant == CellOccupant.PlayerTrail || occupant == CellOccupant.EnemyTrail;
	}

	/// <summary>
	/// Checks if a world position is blocked specifically by a trail
	/// </summary>
	public bool IsCellBlockedByTrail(Vector2 worldPos)
	{
		return IsCellBlockedByTrail(WorldToGrid(worldPos));
	}

	/// <summary>
	/// Checks if moving in a direction would hit a collision
	/// Checks the cell in front of the current position
	/// </summary>
	public bool WouldCollide(Vector2 currentPos, Vector2 direction, float checkDistance)
	{
		Vector2 checkPos = currentPos + direction.Normalized() * checkDistance;
		return IsCellBlocked(checkPos);
	}

	// ========== BULK OPERATIONS ==========

	/// <summary>
	/// Clears all cells of a specific type (useful for clearing all trails on room change)
	/// </summary>
	public void ClearCellsOfType(CellOccupant type)
	{
		var cellsToRemove = new List<Vector2I>();

		foreach (var kvp in _grid)
		{
			if (kvp.Value == type)
			{
				cellsToRemove.Add(kvp.Key);
			}
		}

		foreach (var cell in cellsToRemove)
		{
			_grid.Remove(cell);
		}
	}

	/// <summary>
	/// Clears all grid cells (for room transitions)
	/// </summary>
	public void ClearAll()
	{
		_grid.Clear();
	}

	/// <summary>
	/// Gets total number of occupied cells (for debugging)
	/// </summary>
	public int GetOccupiedCellCount()
	{
		return _grid.Count;
	}

	// ========== CLEANUP ==========

	public override void _ExitTree()
	{
		_instance = null;
	}
}
