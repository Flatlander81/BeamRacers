using Godot;
using System;

/// <summary>
/// Core game manager singleton for Light Cycle Escape.
/// Handles game state, progression, and cycle economy.
/// Access via: GetNode<GameManager>("/root/GameManager")
/// </summary>
public partial class GameManager : Node
{
	// Singleton pattern
	private static GameManager _instance;
	public static GameManager Instance => _instance;

	// Game State Enum
	public enum GameState
	{
		MainMenu,
		Playing,
		Paused,
		GameOver,
		Victory
	}

	// Properties - Current Game State
	private GameState _currentState = GameState.MainMenu;
	public GameState CurrentState
	{
		get => _currentState;
		private set
		{
			if (_currentState != value)
			{
				_currentState = value;
				GD.Print($"[GameManager] State changed to: {_currentState}");
				EmitSignal(SignalName.OnStateChanged, (int)_currentState);
			}
		}
	}

	// Properties - Run Progress
	private int _currentRoom = 1;
	public int CurrentRoom
	{
		get => _currentRoom;
		private set
		{
			if (_currentRoom != value)
			{
				_currentRoom = value;
				GD.Print($"[GameManager] Room changed to: {_currentRoom}/20");
				EmitSignal(SignalName.OnRoomChanged, _currentRoom);
			}
		}
	}

	// Properties - Cycle Economy
	private int _currentRunCycles = 0;
	public int CurrentRunCycles
	{
		get => _currentRunCycles;
		private set
		{
			_currentRunCycles = value;
			EmitSignal(SignalName.OnCyclesChanged, _currentRunCycles, _totalClockCycles);
		}
	}

	private int _totalClockCycles = 0;
	public int TotalClockCycles
	{
		get => _totalClockCycles;
		private set
		{
			_totalClockCycles = value;
			SaveTotalCycles();
		}
	}

	// Properties - Stats
	public float TimeElapsed { get; private set; } = 0.0f;
	public int EnemiesKilled { get; private set; } = 0;
	public int DronesKilled { get; private set; } = 0;

	// Signals
	[Signal]
	public delegate void OnStateChangedEventHandler(int newState);

	[Signal]
	public delegate void OnRoomChangedEventHandler(int roomNumber);

	[Signal]
	public delegate void OnCyclesChangedEventHandler(int currentRun, int total);

	// Constants
	private const int TOTAL_ROOMS = 20;
	private const int VICTORY_BONUS = 500;
	private const string SAVE_FILE_PATH = "user://lightcycle_save.dat";

	// Initialization
	public override void _EnterTree()
	{
		if (_instance != null && _instance != this)
		{
			GD.PrintErr("[GameManager] ERROR: Multiple GameManager instances detected! Removing duplicate.");
			QueueFree();
			return;
		}

		_instance = this;
		GD.Print("[GameManager] ✓ Singleton instance created");
	}

	public override void _Ready()
	{
		GD.Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
		GD.Print("[GameManager] Initializing Light Cycle Escape");
		GD.Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

		LoadTotalCycles();

		GD.Print($"[GameManager] ✓ Total Clock Cycles loaded: {_totalClockCycles}");
		GD.Print($"[GameManager] ✓ Initial state: {CurrentState}");
		GD.Print("[GameManager] ✓ Ready for action");
		GD.Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");
	}

	public override void _Process(double delta)
	{
		// Track time elapsed during gameplay
		if (CurrentState == GameState.Playing)
		{
			TimeElapsed += (float)delta;
		}
	}

	// Public Methods - Game Flow Control

	/// <summary>
	/// Starts a new run, resetting all run-specific stats
	/// </summary>
	public void StartNewRun()
	{
		GD.Print("\n[GameManager] ═══ STARTING NEW RUN ═══");

		// Reset run stats
		CurrentRoom = 1;
		_currentRunCycles = 0;
		TimeElapsed = 0.0f;
		EnemiesKilled = 0;
		DronesKilled = 0;

		// Emit cycles changed to update UI
		EmitSignal(SignalName.OnCyclesChanged, _currentRunCycles, _totalClockCycles);

		// Set state to playing
		CurrentState = GameState.Playing;

		GD.Print($"[GameManager] Room: {CurrentRoom}/{TOTAL_ROOMS}");
		GD.Print($"[GameManager] Cycles (Run/Total): {CurrentRunCycles}/{TotalClockCycles}");
		GD.Print("[GameManager] ═══════════════════════\n");
	}

	/// <summary>
	/// Ends the current run, saves progress
	/// </summary>
	public void EndRun()
	{
		GD.Print("\n[GameManager] ═══ RUN ENDED ═══");
		GD.Print($"[GameManager] Final Room: {CurrentRoom}/{TOTAL_ROOMS}");
		GD.Print($"[GameManager] Time Survived: {TimeElapsed:F2}s");
		GD.Print($"[GameManager] Enemies Killed: {EnemiesKilled}");
		GD.Print($"[GameManager] Drones Destroyed: {DronesKilled}");
		GD.Print($"[GameManager] Cycles Earned This Run: {CurrentRunCycles}");
		GD.Print($"[GameManager] Total Cycles: {TotalClockCycles}");
		GD.Print("[GameManager] ══════════════════\n");

		CurrentState = GameState.GameOver;
	}

	/// <summary>
	/// Called when player completes all 20 rooms
	/// </summary>
	public void Victory()
	{
		GD.Print("\n[GameManager] ★★★ VICTORY! ★★★");
		GD.Print($"[GameManager] All {TOTAL_ROOMS} rooms completed!");
		GD.Print($"[GameManager] Time: {TimeElapsed:F2}s");
		GD.Print($"[GameManager] Total Kills: {EnemiesKilled}");

		// Award victory bonus
		AddClockCycles(VICTORY_BONUS);
		GD.Print($"[GameManager] Victory Bonus: +{VICTORY_BONUS} cycles");
		GD.Print($"[GameManager] Final Total Cycles: {TotalClockCycles}");
		GD.Print("[GameManager] ★★★★★★★★★★★★★★★\n");

		CurrentState = GameState.Victory;
	}

	/// <summary>
	/// Advances to the next room, checks for victory condition
	/// </summary>
	public void NextRoom()
	{
		if (CurrentState != GameState.Playing)
		{
			GD.PrintErr("[GameManager] ERROR: Cannot advance room while not playing");
			return;
		}

		CurrentRoom++;

		GD.Print($"[GameManager] ➜ Advancing to Room {CurrentRoom}/{TOTAL_ROOMS}");

		// Check for victory
		if (CurrentRoom > TOTAL_ROOMS)
		{
			Victory();
		}
	}

	/// <summary>
	/// Pauses the game
	/// </summary>
	public void PauseGame()
	{
		if (CurrentState == GameState.Playing)
		{
			GD.Print("[GameManager] ⏸ Game Paused");
			CurrentState = GameState.Paused;
			GetTree().Paused = true;
		}
	}

	/// <summary>
	/// Resumes the game from pause
	/// </summary>
	public void ResumeGame()
	{
		if (CurrentState == GameState.Paused)
		{
			GD.Print("[GameManager] ▶ Game Resumed");
			CurrentState = GameState.Playing;
			GetTree().Paused = false;
		}
	}

	/// <summary>
	/// Adds clock cycles to both current run and total persistent cycles
	/// </summary>
	/// <param name="amount">Amount of cycles to add</param>
	public void AddClockCycles(int amount)
	{
		if (amount <= 0)
			return;

		CurrentRunCycles += amount;
		TotalClockCycles += amount;

		GD.Print($"[GameManager] +{amount} cycles | Run: {CurrentRunCycles} | Total: {TotalClockCycles}");
	}

	/// <summary>
	/// Increments enemy kill counter
	/// </summary>
	public void AddEnemyKill()
	{
		EnemiesKilled++;
		GD.Print($"[GameManager] Enemy eliminated! Total: {EnemiesKilled}");
	}

	/// <summary>
	/// Increments drone kill counter
	/// </summary>
	public void AddDroneKill()
	{
		DronesKilled++;
		GD.Print($"[GameManager] Drone destroyed! Total: {DronesKilled}");
	}

	// Save/Load System

	/// <summary>
	/// Saves total cycles to disk
	/// </summary>
	private void SaveTotalCycles()
	{
		using var saveFile = FileAccess.Open(SAVE_FILE_PATH, FileAccess.ModeFlags.Write);
		if (saveFile != null)
		{
			saveFile.Store32((uint)_totalClockCycles);
			GD.Print($"[GameManager] 💾 Saved {_totalClockCycles} total cycles");
		}
		else
		{
			GD.PrintErr($"[GameManager] ERROR: Failed to save cycles - {FileAccess.GetOpenError()}");
		}
	}

	/// <summary>
	/// Loads total cycles from disk
	/// </summary>
	private void LoadTotalCycles()
	{
		if (FileAccess.FileExists(SAVE_FILE_PATH))
		{
			using var saveFile = FileAccess.Open(SAVE_FILE_PATH, FileAccess.ModeFlags.Read);
			if (saveFile != null)
			{
				_totalClockCycles = (int)saveFile.Get32();
				GD.Print($"[GameManager] 💾 Loaded {_totalClockCycles} total cycles from save");
			}
			else
			{
				GD.PrintErr($"[GameManager] ERROR: Failed to load cycles - {FileAccess.GetOpenError()}");
			}
		}
		else
		{
			GD.Print("[GameManager] No save file found - starting fresh");
			_totalClockCycles = 0;
		}
	}

	// Debug/Utility Methods

	/// <summary>
	/// Returns a formatted string with current game stats
	/// </summary>
	public string GetStatsString()
	{
		return $"Room: {CurrentRoom}/{TOTAL_ROOMS} | Time: {TimeElapsed:F1}s | Kills: {EnemiesKilled} | Cycles: {CurrentRunCycles}/{TotalClockCycles}";
	}

	/// <summary>
	/// Resets all persistent data (use with caution!)
	/// </summary>
	public void ResetAllProgress()
	{
		GD.Print("[GameManager] ⚠ RESETTING ALL PROGRESS");
		_totalClockCycles = 0;
		SaveTotalCycles();
		StartNewRun();
	}
}
