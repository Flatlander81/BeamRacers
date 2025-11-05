using Godot;
using System;

/// <summary>
/// Manages room progression, enemy tracking, and level transitions.
/// Handles room lifecycle from start to completion with bonuses and scaling.
/// </summary>
public partial class RoomManager : Node
{
	// ========== SIGNALS ==========
	[Signal]
	public delegate void OnRoomStartedEventHandler(int roomNumber);

	[Signal]
	public delegate void OnRoomClearedEventHandler();

	[Signal]
	public delegate void OnEnemiesChangedEventHandler(int remaining);

	// ========== SINGLETON ==========
	private static RoomManager _instance;
	public static RoomManager Instance => _instance;

	// ========== ROOM TRACKING ==========
	public int CurrentRoom { get; private set; } = 0;
	public int EnemiesRemaining { get; private set; } = 0;
	public float RoomStartTime { get; private set; } = 0f;
	public bool IsTransitioning { get; private set; } = false;

	// ========== REFERENCES ==========
	private Player _player;
	private Arena _arena;
	private EnemySpawner _enemySpawner;

	// ========== CONSTANTS ==========
	private const int BASE_CLEAR_BONUS = 50;
	private const int FAST_CLEAR_BONUS_30S = 10;
	private const int FAST_CLEAR_BONUS_20S = 20;
	private const int MAX_ROOMS = 20;

	// ========== INITIALIZATION ==========
	public override void _EnterTree()
	{
		if (_instance != null && _instance != this)
		{
			GD.PrintErr("[RoomManager] ERROR: Multiple RoomManager instances detected!");
			QueueFree();
			return;
		}

		_instance = this;
		GD.Print("[RoomManager] ✓ Singleton instance created");
	}

	public override void _Ready()
	{
		GD.Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
		GD.Print("[RoomManager] Initializing Room Manager");
		GD.Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
	}

	/// <summary>
	/// Initializes references to required game systems
	/// </summary>
	public void Initialize(Player player, Arena arena, EnemySpawner enemySpawner)
	{
		_player = player;
		_arena = arena;
		_enemySpawner = enemySpawner;

		// Subscribe to enemy spawner events
		if (_enemySpawner != null)
		{
			_enemySpawner.OnEnemyDied += OnEnemyDefeated;
			GD.Print("[RoomManager] ✓ Subscribed to EnemySpawner.OnEnemyDied");
		}
		else
		{
			GD.PrintErr("[RoomManager] ERROR: EnemySpawner reference is null!");
		}

		// Subscribe to GameManager events
		if (GameManager.Instance != null)
		{
			GameManager.Instance.OnRoomChanged += OnGameManagerRoomChanged;
			GD.Print("[RoomManager] ✓ Subscribed to GameManager.OnRoomChanged");
		}

		GD.Print("[RoomManager] ✓ Initialization complete");
	}

	// ========== ROOM LIFECYCLE ==========

	/// <summary>
	/// Starts a new room with the specified number
	/// This signals Main.cs to set up the room - doesn't do spawning directly
	/// </summary>
	public void StartRoom(int roomNumber)
	{
		if (roomNumber < 1 || roomNumber > MAX_ROOMS)
		{
			GD.PrintErr($"[RoomManager] ERROR: Invalid room number {roomNumber}!");
			return;
		}

		IsTransitioning = true;
		CurrentRoom = roomNumber;
		RoomStartTime = Time.GetTicksMsec() / 1000f;

		GD.Print("");
		GD.Print("═══════════════════════════════════════");
		GD.Print($"[RoomManager] >>> STARTING ROOM {CurrentRoom} <<<");
		GD.Print("═══════════════════════════════════════");

		// Emit signal for Main.cs to handle setup
		EmitSignal(SignalName.OnRoomStarted, CurrentRoom);

		IsTransitioning = false;
	}

	/// <summary>
	/// Notifies RoomManager that room setup is complete with enemy count
	/// Called by Main.cs after spawning
	/// </summary>
	public void NotifyRoomSetupComplete(int enemyCount)
	{
		EnemiesRemaining = enemyCount;
		GD.Print($"[RoomManager] Room {CurrentRoom} ready: {EnemiesRemaining} enemies");
		GD.Print("═══════════════════════════════════════");
		GD.Print("");
	}

	/// <summary>
	/// Called when an enemy is defeated
	/// </summary>
	private void OnEnemyDefeated()
	{
		if (EnemiesRemaining <= 0)
		{
			GD.PrintErr("[RoomManager] WARNING: OnEnemyDefeated called but no enemies remaining!");
			return;
		}

		EnemiesRemaining--;
		EmitSignal(SignalName.OnEnemiesChanged, EnemiesRemaining);

		GD.Print($"[RoomManager] Enemy defeated: {EnemiesRemaining} remaining");

		// Check if room is cleared
		if (EnemiesRemaining == 0)
		{
			HandleRoomCleared();
		}
	}

	/// <summary>
	/// Called when all enemies in the room are defeated
	/// </summary>
	private void HandleRoomCleared()
	{
		float clearTime = Time.GetTicksMsec() / 1000f - RoomStartTime;
		int totalBonus = BASE_CLEAR_BONUS;

		GD.Print("");
		GD.Print("═══════════════════════════════════════");
		GD.Print($"[RoomManager] >>> ROOM {CurrentRoom} CLEARED <<<");
		GD.Print("═══════════════════════════════════════");

		// Calculate fast clear bonuses
		if (clearTime < 20f)
		{
			totalBonus += FAST_CLEAR_BONUS_20S;
			GD.Print($"[RoomManager] ⚡ Lightning Fast! (<20s) +{FAST_CLEAR_BONUS_20S} cycles");
		}
		else if (clearTime < 30f)
		{
			totalBonus += FAST_CLEAR_BONUS_30S;
			GD.Print($"[RoomManager] ⚡ Fast Clear! (<30s) +{FAST_CLEAR_BONUS_30S} cycles");
		}

		// Award bonuses
		GameManager.Instance?.AddClockCycles(totalBonus);

		GD.Print($"[RoomManager] Time: {clearTime:F1}s");
		GD.Print($"[RoomManager] Bonus: +{totalBonus} cycles");
		GD.Print("═══════════════════════════════════════");
		GD.Print("");

		// Emit signal
		EmitSignal(SignalName.OnRoomCleared);

		// Handle progression
		if (CurrentRoom < MAX_ROOMS)
		{
			GD.Print($"[RoomManager] Preparing for Room {CurrentRoom + 1}...");
			// Wait 1 second before next room
			GetTree().CreateTimer(1.0).Timeout += () =>
			{
				// TODO: Show power-up selection here (future feature)
				GD.Print("[RoomManager] Power-up selection placeholder");

				// Advance to next room
				GameManager.Instance?.NextRoom();
			};
		}
		else
		{
			// Final room cleared - Victory!
			GD.Print("[RoomManager] 🎉 ALL ROOMS CLEARED - VICTORY! 🎉");
			GameManager.Instance?.Victory();
		}
	}

	/// <summary>
	/// Called when GameManager room changes (to sync state)
	/// </summary>
	private void OnGameManagerRoomChanged(int newRoom)
	{
		GD.Print($"[RoomManager] GameManager room changed to {newRoom}");

		// Start the new room
		StartRoom(newRoom);
	}

	// ========== ARENA SCALING ==========

	/// <summary>
	/// Returns the arena size scale based on room number
	/// </summary>
	public float GetArenaSizeScale(int room)
	{
		if (room >= 1 && room <= 5)
			return 1.0f;
		else if (room >= 6 && room <= 10)
			return 0.9f;
		else if (room >= 11 && room <= 15)
			return 0.8f;
		else if (room >= 16 && room <= 20)
			return 0.85f;
		else
			return 1.0f; // Default
	}

	// ========== CLEANUP ==========

	public override void _ExitTree()
	{
		// Unsubscribe from events
		if (_enemySpawner != null)
		{
			_enemySpawner.OnEnemyDied -= OnEnemyDefeated;
		}

		if (GameManager.Instance != null)
		{
			GameManager.Instance.OnRoomChanged -= OnGameManagerRoomChanged;
		}

		_instance = null;
	}
}
