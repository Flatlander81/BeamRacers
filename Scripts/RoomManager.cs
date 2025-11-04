using Godot;
using System;

/// <summary>
/// Room Manager - Handles room progression, enemy tracking, and room lifecycle
/// Manages the 20-room progression system with arena scaling and bonuses
/// </summary>
public partial class RoomManager : Node
{
	// ========== ROOM TRACKING ==========
	private int _currentRoom = 1;
	public int CurrentRoom
	{
		get => _currentRoom;
		private set => _currentRoom = value;
	}

	private int _enemiesRemaining = 0;
	public int EnemiesRemaining
	{
		get => _enemiesRemaining;
		private set
		{
			_enemiesRemaining = value;
			EmitSignal(SignalName.OnEnemiesChanged, _enemiesRemaining);
		}
	}

	private ulong _roomStartTime = 0;
	public ulong RoomStartTime
	{
		get => _roomStartTime;
		private set => _roomStartTime = value;
	}

	private bool _isTransitioning = false;
	public bool IsTransitioning
	{
		get => _isTransitioning;
		private set => _isTransitioning = value;
	}

	// ========== SIGNALS ==========
	[Signal]
	public delegate void OnRoomStartedEventHandler(int roomNumber);

	[Signal]
	public delegate void OnRoomClearedEventHandler();

	[Signal]
	public delegate void OnEnemiesChangedEventHandler(int remaining);

	// ========== REFERENCES ==========
	private GameManager _gameManager;
	private Player _player;
	private Arena _arena;

	// ========== CONSTANTS ==========
	private const int TOTAL_ROOMS = 20;
	private const int BASE_ROOM_BONUS = 50;
	private const float FAST_CLEAR_THRESHOLD_1 = 30.0f;  // < 30s
	private const int FAST_CLEAR_BONUS_1 = 10;
	private const float FAST_CLEAR_THRESHOLD_2 = 20.0f;  // < 20s
	private const int FAST_CLEAR_BONUS_2 = 20;
	private const float ROOM_TRANSITION_DELAY = 1.0f;

	// ========== INITIALIZATION ==========
	public override void _Ready()
	{
		GD.Print("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
		GD.Print("[RoomManager] Initializing Room Management System");
		GD.Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

		// Get GameManager reference
		_gameManager = GetNode<GameManager>("/root/GameManager");
		if (_gameManager != null)
		{
			GD.Print("[RoomManager] ✓ GameManager reference acquired");
		}
		else
		{
			GD.PrintErr("[RoomManager] ERROR: GameManager not found!");
		}

		GD.Print("[RoomManager] ✓ Room Manager ready");
		GD.Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");
	}

	// ========== ROOM LIFECYCLE ==========

	/// <summary>
	/// Starts a new room with the given room number
	/// </summary>
	/// <param name="roomNumber">Room number (1-20)</param>
	public void StartRoom(int roomNumber)
	{
		if (_isTransitioning)
		{
			GD.Print("[RoomManager] Cannot start room while transitioning");
			return;
		}

		GD.Print($"\n[RoomManager] ╔══════════════════════════════════╗");
		GD.Print($"[RoomManager] ║   STARTING ROOM {roomNumber,2} / {TOTAL_ROOMS}         ║");
		GD.Print($"[RoomManager] ╚══════════════════════════════════╝");

		// 1. Set current room
		CurrentRoom = roomNumber;

		// 2. Record start time
		RoomStartTime = Time.GetTicksMsec();

		// 3. Clear old enemies (placeholder for EnemySpawner integration)
		ClearEnemies();

		// 4. Clear player trail
		if (_player != null)
		{
			_player.ClearTrail();
			GD.Print("[RoomManager] ✓ Player trail cleared");
		}

		// 5. Reset player position to (0, 0)
		if (_player != null)
		{
			_player.ResetPosition(Vector2.Zero);
			GD.Print("[RoomManager] ✓ Player reset to (0, 0)");
		}

		// 6. Generate new arena (scale based on room)
		if (_arena != null)
		{
			float scale = GetArenaSizeScale(roomNumber);
			_arena.GenerateArena(roomNumber, scale);
			GD.Print($"[RoomManager] ✓ Arena generated (scale: {scale:F2})");
		}

		// 7. Spawn enemies for room (placeholder for EnemySpawner integration)
		int enemyCount = SpawnEnemiesForRoom(roomNumber);

		// 8. Track enemy count
		EnemiesRemaining = enemyCount;

		// 9. Emit OnRoomStarted
		EmitSignal(SignalName.OnRoomStarted, roomNumber);

		// 10. Console output
		GD.Print($"[RoomManager] Room {roomNumber} started: {enemyCount} enemies");
		GD.Print($"[RoomManager] Arena scale: {GetArenaSizeScale(roomNumber):F2}");
		GD.Print("[RoomManager] ════════════════════════════════════\n");
	}

	/// <summary>
	/// Called when an enemy is defeated
	/// </summary>
	public void OnEnemyDefeated()
	{
		// 1. Decrement enemies remaining
		EnemiesRemaining--;

		// 2. Emit OnEnemiesChanged (handled by property setter)

		// 3. Console output
		GD.Print($"[RoomManager] Enemy defeated: {EnemiesRemaining} remaining");

		// 4. Check if room is cleared
		if (EnemiesRemaining <= 0)
		{
			OnRoomCleared();
		}
	}

	/// <summary>
	/// Called when all enemies in a room are defeated
	/// </summary>
	private void OnRoomCleared()
	{
		if (_isTransitioning)
		{
			return; // Already processing room clear
		}

		_isTransitioning = true;

		// 1. Calculate clear time
		ulong endTime = Time.GetTicksMsec();
		float clearTime = (endTime - RoomStartTime) / 1000.0f; // Convert to seconds

		// 2. Award base bonus
		int totalBonus = BASE_ROOM_BONUS;

		// 3. Fast clear bonus
		int fastClearBonus = 0;
		if (clearTime < FAST_CLEAR_THRESHOLD_2)
		{
			// < 20s: +20 cycles
			fastClearBonus = FAST_CLEAR_BONUS_2;
			totalBonus += fastClearBonus;
		}
		else if (clearTime < FAST_CLEAR_THRESHOLD_1)
		{
			// < 30s: +10 cycles
			fastClearBonus = FAST_CLEAR_BONUS_1;
			totalBonus += fastClearBonus;
		}

		// Award cycles
		if (_gameManager != null)
		{
			_gameManager.AddClockCycles(totalBonus);
		}

		// 4. Console output
		GD.Print("\n[RoomManager] ╔══════════════════════════════════╗");
		GD.Print("[RoomManager] ║      >>> ROOM CLEARED <<<        ║");
		GD.Print("[RoomManager] ╚══════════════════════════════════╝");
		GD.Print($"[RoomManager] Time: {clearTime:F2}s");
		GD.Print($"[RoomManager] Base bonus: +{BASE_ROOM_BONUS} cycles");
		if (fastClearBonus > 0)
		{
			GD.Print($"[RoomManager] Fast clear bonus: +{fastClearBonus} cycles");
		}
		GD.Print($"[RoomManager] Total bonus: +{totalBonus} cycles");
		GD.Print("[RoomManager] ════════════════════════════════════\n");

		// Emit room cleared signal
		EmitSignal(SignalName.OnRoomCleared);

		// 5. Check if this is the final room
		if (CurrentRoom >= TOTAL_ROOMS)
		{
			// Victory!
			if (_gameManager != null)
			{
				GD.Print("[RoomManager] Final room completed - calling Victory()");
				_gameManager.Victory();
			}
			_isTransitioning = false;
		}
		else
		{
			// 6. Wait and advance to next room (or show power-up selection)
			GD.Print($"[RoomManager] Transitioning to room {CurrentRoom + 1} in {ROOM_TRANSITION_DELAY}s...");

			// Create timer for transition
			var timer = GetTree().CreateTimer(ROOM_TRANSITION_DELAY);
			timer.Timeout += () =>
			{
				// TODO: Show power-up selection here in future
				AdvanceToNextRoom();
				_isTransitioning = false;
			};
		}
	}

	/// <summary>
	/// Advances to the next room
	/// </summary>
	private void AdvanceToNextRoom()
	{
		int nextRoom = CurrentRoom + 1;
		GD.Print($"[RoomManager] Advancing to room {nextRoom}...");

		// Update GameManager room counter
		if (_gameManager != null)
		{
			_gameManager.NextRoom();
		}

		// Start the next room
		StartRoom(nextRoom);
	}

	// ========== ARENA SCALING ==========

	/// <summary>
	/// Gets the arena size scale based on room number
	/// </summary>
	/// <param name="room">Room number (1-20)</param>
	/// <returns>Scale factor for arena size</returns>
	public float GetArenaSizeScale(int room)
	{
		// Rooms 1-5: 1.0 (full size)
		if (room <= 5)
		{
			return 1.0f;
		}
		// Rooms 6-10: 0.9
		else if (room <= 10)
		{
			return 0.9f;
		}
		// Rooms 11-15: 0.8
		else if (room <= 15)
		{
			return 0.8f;
		}
		// Rooms 16-20: 0.85
		else
		{
			return 0.85f;
		}
	}

	// ========== ENEMY MANAGEMENT (Placeholder for EnemySpawner) ==========

	/// <summary>
	/// Clears all enemies from the arena
	/// Placeholder for EnemySpawner integration
	/// </summary>
	private void ClearEnemies()
	{
		// TODO: Call EnemySpawner.ClearAllEnemies() when implemented
		GD.Print("[RoomManager] ✓ Enemies cleared (placeholder)");
	}

	/// <summary>
	/// Spawns enemies for the given room
	/// Placeholder for EnemySpawner integration
	/// </summary>
	/// <param name="roomNumber">Room number (1-20)</param>
	/// <returns>Number of enemies spawned</returns>
	private int SpawnEnemiesForRoom(int roomNumber)
	{
		// TODO: Call EnemySpawner.SpawnEnemiesForRoom(roomNumber) when implemented
		// For now, return a placeholder count based on room number
		int enemyCount = CalculateEnemyCount(roomNumber);
		GD.Print($"[RoomManager] ✓ Enemies spawned (placeholder): {enemyCount}");
		return enemyCount;
	}

	/// <summary>
	/// Calculates how many enemies should spawn for a given room
	/// </summary>
	/// <param name="roomNumber">Room number (1-20)</param>
	/// <returns>Number of enemies to spawn</returns>
	private int CalculateEnemyCount(int roomNumber)
	{
		// Simple scaling: 2 enemies for room 1, +1 every 2 rooms
		// Room 1-2: 2 enemies
		// Room 3-4: 3 enemies
		// Room 5-6: 4 enemies
		// etc., up to room 19-20: 11 enemies
		return 2 + (roomNumber - 1) / 2;
	}

	// ========== PUBLIC SETUP METHODS ==========

	/// <summary>
	/// Sets the player reference
	/// </summary>
	/// <param name="player">Player instance</param>
	public void SetPlayer(Player player)
	{
		_player = player;
		GD.Print("[RoomManager] ✓ Player reference set");
	}

	/// <summary>
	/// Sets the arena reference
	/// </summary>
	/// <param name="arena">Arena instance</param>
	public void SetArena(Arena arena)
	{
		_arena = arena;
		GD.Print("[RoomManager] ✓ Arena reference set");
	}

	// ========== DEBUG ==========

	/// <summary>
	/// Returns a formatted string with current room stats
	/// </summary>
	public string GetRoomStatsString()
	{
		float elapsedTime = (Time.GetTicksMsec() - RoomStartTime) / 1000.0f;
		return $"Room: {CurrentRoom}/{TOTAL_ROOMS} | Enemies: {EnemiesRemaining} | Time: {elapsedTime:F1}s";
	}
}
