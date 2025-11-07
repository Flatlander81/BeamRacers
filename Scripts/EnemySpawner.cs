using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Spawns and manages enemy cycles based on room progression
/// </summary>
public partial class EnemySpawner : Node
{
	// ========== ENEMY TRACKING ==========
	private List<EnemyCycle> _activeEnemies = new List<EnemyCycle>();

	// ========== SPAWNING CONSTANTS ==========
	private const float MIN_DISTANCE_FROM_PLAYER = 300.0f;
	private const float MIN_DISTANCE_FROM_ENEMIES = 150.0f;
	private const int MAX_SPAWN_ATTEMPTS = 50;

	// Speed scaling by room
	private readonly Dictionary<int, float> _speedByRoom = new Dictionary<int, float>
	{
		{ 1, 250.0f },
		{ 2, 275.0f },
		{ 3, 300.0f },
		{ 4, 325.0f }
	};

	// Enemy count by room
	private readonly Dictionary<int, (int min, int max)> _enemyCountByRoom = new Dictionary<int, (int, int)>
	{
		{ 1, (1, 2) },
		{ 2, (2, 3) },
		{ 3, (3, 4) }
	};

	// ========== SCENE REFERENCE ==========
	private PackedScene _enemyCycleScene;

	// ========== INITIALIZATION ==========
	public override void _Ready()
	{
		GD.Print("[EnemySpawner] Initializing enemy spawner...");

		// Load enemy scene
		_enemyCycleScene = GD.Load<PackedScene>("res://Scenes/Enemies/EnemyCycle.tscn");
		if (_enemyCycleScene == null)
		{
			GD.PrintErr("[EnemySpawner] ERROR: Could not load EnemyCycle.tscn!");
			return;
		}

		GD.Print("[EnemySpawner] ✓ Enemy spawner ready");
	}

	// ========== PUBLIC METHODS ==========
	/// <summary>
	/// Spawns enemies for the given room number
	/// </summary>
	/// <param name="roomNumber">Current room number</param>
	/// <param name="playerPosition">Player's current position</param>
	/// <param name="container">Node to spawn enemies under</param>
	/// <param name="arenaBounds">Arena boundary rectangle</param>
	/// <param name="gridSize">Grid size for snapping (default 50)</param>
	public void SpawnEnemies(int roomNumber, Vector2 playerPosition, Node2D container, Rect2 arenaBounds, int gridSize = 50)
	{
		if (_enemyCycleScene == null)
		{
			GD.PrintErr("[EnemySpawner] ERROR: Cannot spawn - enemy scene not loaded!");
			return;
		}

		// Determine enemy count
		int enemyCount = CalculateEnemyCount(roomNumber);

		// Determine enemy speed
		float enemySpeed = CalculateEnemySpeed(roomNumber);

		GD.Print($"[EnemySpawner] ═══ Spawning {enemyCount} enemies for Room {roomNumber} ═══");
		GD.Print($"[EnemySpawner] Arena bounds: {arenaBounds}");

		// Track spawn positions to ensure spacing
		List<Vector2> spawnPositions = new List<Vector2>();

		// Spawn each enemy
		for (int i = 0; i < enemyCount; i++)
		{
			Vector2 spawnPos = FindValidSpawnPosition(playerPosition, spawnPositions, arenaBounds, gridSize);

			if (spawnPos == Vector2.Zero)
			{
				GD.PrintErr($"[EnemySpawner] ERROR: Could not find valid spawn position for enemy {i + 1}");
				continue;
			}

			spawnPositions.Add(spawnPos);

			// Instantiate enemy
			EnemyCycle enemy = _enemyCycleScene.Instantiate<EnemyCycle>();
			enemy.GlobalPosition = spawnPos;
			enemy.MoveSpeed = enemySpeed;
			enemy.ArenaBounds = arenaBounds;

			// Connect to death signal
			enemy.OnEnemyDied += () => OnEnemyDied(enemy);

			// Add to container
			container.AddChild(enemy);

			// Track enemy
			_activeEnemies.Add(enemy);

			// Calculate distance from player
			float distFromPlayer = spawnPos.DistanceTo(playerPosition);

			GD.Print($"[EnemySpawner] Spawned enemy {i + 1} at {spawnPos}, speed: {enemySpeed}, distance from player: {distFromPlayer:F0}");
		}

		GD.Print($"[EnemySpawner] ✓ {_activeEnemies.Count} enemies active");
	}

	/// <summary>
	/// Clears all active enemies
	/// </summary>
	public void ClearAllEnemies()
	{
		GD.Print($"[EnemySpawner] Clearing {_activeEnemies.Count} enemies...");

		// Copy list to avoid modification during iteration
		var enemiesToRemove = new List<EnemyCycle>(_activeEnemies);

		foreach (var enemy in enemiesToRemove)
		{
			if (IsInstanceValid(enemy))
			{
				enemy.QueueFree();
			}
		}

		_activeEnemies.Clear();

		GD.Print("[EnemySpawner] ✓ All enemies cleared");
	}

	/// <summary>
	/// Returns the number of active enemies
	/// </summary>
	public int GetActiveEnemyCount()
	{
		// Clean up invalid references
		_activeEnemies.RemoveAll(e => !IsInstanceValid(e));
		return _activeEnemies.Count;
	}

	// ========== PRIVATE METHODS ==========
	/// <summary>
	/// Calculates enemy count based on room number
	/// </summary>
	private int CalculateEnemyCount(int roomNumber)
	{
		// Room 4+: 4-5 enemies
		if (roomNumber >= 4)
		{
			return GD.RandRange(4, 5);
		}

		// Look up in dictionary
		if (_enemyCountByRoom.ContainsKey(roomNumber))
		{
			var range = _enemyCountByRoom[roomNumber];
			return GD.RandRange(range.min, range.max);
		}

		// Default fallback
		return 2;
	}

	/// <summary>
	/// Calculates enemy speed based on room number
	/// </summary>
	private float CalculateEnemySpeed(int roomNumber)
	{
		// Room 4+: 325 units/sec + 25 per room
		if (roomNumber >= 4)
		{
			return 325.0f + (roomNumber - 4) * 25.0f;
		}

		// Look up in dictionary
		if (_speedByRoom.ContainsKey(roomNumber))
		{
			return _speedByRoom[roomNumber];
		}

		// Default fallback
		return 250.0f;
	}

	/// <summary>
	/// Finds a valid spawn position that meets distance requirements
	/// </summary>
	private Vector2 FindValidSpawnPosition(Vector2 playerPosition, List<Vector2> existingPositions, Rect2 arenaBounds, int gridSize)
	{
		Random random = new Random();

		for (int attempt = 0; attempt < MAX_SPAWN_ATTEMPTS; attempt++)
		{
			// Generate random position around arena edges
			Vector2 candidate = GenerateEdgePosition(arenaBounds, gridSize, random);

			// Check distance from player
			if (candidate.DistanceTo(playerPosition) < MIN_DISTANCE_FROM_PLAYER)
			{
				continue;
			}

			// Check distance from other enemies
			bool tooClose = false;
			foreach (var pos in existingPositions)
			{
				if (candidate.DistanceTo(pos) < MIN_DISTANCE_FROM_ENEMIES)
				{
					tooClose = true;
					break;
				}
			}

			if (tooClose)
			{
				continue;
			}

			// Valid position found
			return candidate;
		}

		// Failed to find valid position
		GD.PrintErr($"[EnemySpawner] Failed to find valid spawn position after {MAX_SPAWN_ATTEMPTS} attempts");
		return Vector2.Zero;
	}

	/// <summary>
	/// Generates a random grid-snapped position around the arena edges
	/// </summary>
	private Vector2 GenerateEdgePosition(Rect2 arenaBounds, int gridSize, Random random)
	{
		// Choose a random edge (0=top, 1=right, 2=bottom, 3=left)
		int edge = random.Next(0, 4);

		float margin = 100.0f; // Stay away from absolute edges

		// Calculate usable range with margins
		float minX = arenaBounds.Position.X + margin;
		float maxX = arenaBounds.Position.X + arenaBounds.Size.X - margin;
		float minY = arenaBounds.Position.Y + margin;
		float maxY = arenaBounds.Position.Y + arenaBounds.Size.Y - margin;

		Vector2 position = edge switch
		{
			0 => new Vector2((float)random.NextDouble() * (maxX - minX) + minX, minY), // Top edge
			1 => new Vector2(maxX, (float)random.NextDouble() * (maxY - minY) + minY), // Right edge
			2 => new Vector2((float)random.NextDouble() * (maxX - minX) + minX, maxY), // Bottom edge
			3 => new Vector2(minX, (float)random.NextDouble() * (maxY - minY) + minY), // Left edge
			_ => Vector2.Zero
		};

		// Snap to grid
		position.X = Mathf.Round(position.X / gridSize) * gridSize;
		position.Y = Mathf.Round(position.Y / gridSize) * gridSize;

		return position;
	}

	/// <summary>
	/// Called when an enemy dies
	/// </summary>
	private void OnEnemyDied(EnemyCycle enemy)
	{
		// Remove from tracking list
		_activeEnemies.Remove(enemy);

		GD.Print($"[EnemySpawner] Enemy died. Remaining: {_activeEnemies.Count}");
	}
}
