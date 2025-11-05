using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Orchestrates automated collision testing for player and enemies.
/// Tests all standard and edge cases to verify collision system integrity.
/// </summary>
public partial class CollisionTestController : Node
{
	// ========== SINGLETON ==========
	private static CollisionTestController _instance;
	public static CollisionTestController Instance => _instance;

	// ========== TEST STATE ==========
	private bool _testActive = false;
	private int _currentTestScenario = 0;
	private float _testTimer = 0.0f;
	private List<TestScenario> _testScenarios = new List<TestScenario>();

	// ========== REFERENCES ==========
	private Player _player;
	private List<EnemyCycle> _testEnemies = new List<EnemyCycle>();
	private Node2D _gameLayer;

	// ========== TEST SCENARIO DEFINITION ==========
	private class TestScenario
	{
		public string Name;
		public string Description;
		public Action<CollisionTestController> Setup;
		public Action<CollisionTestController, float> Update;
		public float Duration;
		public bool Completed;

		public TestScenario(string name, string description, float duration,
		                     Action<CollisionTestController> setup,
		                     Action<CollisionTestController, float> update)
		{
			Name = name;
			Description = description;
			Duration = duration;
			Setup = setup;
			Update = update;
			Completed = false;
		}
	}

	// ========== INITIALIZATION ==========
	public override void _EnterTree()
	{
		if (_instance != null && _instance != this)
		{
			QueueFree();
			return;
		}
		_instance = this;
	}

	public override void _Ready()
	{
		InitializeTestScenarios();
		GD.Print("[CollisionTest] Controller ready with {0} test scenarios", _testScenarios.Count);
	}

	/// <summary>
	/// Initializes all test scenarios
	/// </summary>
	private void InitializeTestScenarios()
	{
		// TEST 1: Player rapid double-turn (cross own trail)
		_testScenarios.Add(new TestScenario(
			"Player Rapid Double-Turn",
			"Player makes two quick left turns to test self-collision",
			8.0f,
			setup: (ctrl) =>
			{
				GD.Print("\n════════════════════════════════════════");
				GD.Print("[TEST 1] Player Rapid Double-Turn");
				GD.Print("Testing: Player crossing own trail via rapid turns");
				GD.Print("════════════════════════════════════════");
				ctrl.SetPlayerAutoTest(0, "rapid_double_turn");
			},
			update: (ctrl, delta) => { /* Player handles its own automation */ }
		));

		// TEST 2: Player box pattern (guaranteed self-collision)
		_testScenarios.Add(new TestScenario(
			"Player Box Pattern",
			"Player creates a box to cross own trail",
			10.0f,
			setup: (ctrl) =>
			{
				GD.Print("\n════════════════════════════════════════");
				GD.Print("[TEST 2] Player Box Pattern");
				GD.Print("Testing: Player creating closed loop");
				GD.Print("════════════════════════════════════════");
				ctrl.ResetPlayer(new Vector2(0, 0), 0); // Start at origin facing right
				ctrl.SetPlayerAutoTest(0, "box_pattern");
			},
			update: (ctrl, delta) => { }
		));

		// TEST 3: Enemy hitting player trail (actively drawing)
		_testScenarios.Add(new TestScenario(
			"Enemy vs Player Trail (Active)",
			"Enemy collides with player's actively-drawing trail",
			8.0f,
			setup: (ctrl) =>
			{
				GD.Print("\n════════════════════════════════════════");
				GD.Print("[TEST 3] Enemy vs Player Trail (Active)");
				GD.Print("Testing: Enemy hitting currently-drawing player trail");
				GD.Print("════════════════════════════════════════");
				ctrl.ResetPlayer(new Vector2(0, -50), 0); // Moving right
				ctrl.SpawnTestEnemy(new Vector2(200, 100), 3); // Below, moving UP to cross player's horizontal trail
				ctrl.SetPlayerAutoTest(0, "move_straight");
			},
			update: (ctrl, delta) => { }
		));

		// TEST 4: Enemy hitting completed player trail wall
		_testScenarios.Add(new TestScenario(
			"Enemy vs Player Trail (Wall)",
			"Enemy collides with completed player trail wall",
			10.0f,
			setup: (ctrl) =>
			{
				GD.Print("\n════════════════════════════════════════");
				GD.Print("[TEST 4] Enemy vs Player Trail (Wall)");
				GD.Print("Testing: Enemy hitting finalized player trail");
				GD.Print("════════════════════════════════════════");
				// Player creates a vertical wall by moving down then turning
				ctrl.ResetPlayer(new Vector2(0, -150), 1); // Starting up high, moving DOWN
				ctrl.SetPlayerAutoTest(0, "create_wall");
				// Enemy will cross the vertical trail after player finishes
				ctrl.CallDeferred("SpawnDelayedEnemy", new Vector2(-100, 0), 0, 3.0f); // Moving RIGHT to cross vertical trail
			},
			update: (ctrl, delta) => { }
		));

		// TEST 5: Player hitting enemy trail (actively drawing)
		_testScenarios.Add(new TestScenario(
			"Player vs Enemy Trail (Active)",
			"Player collides with enemy's actively-drawing trail",
			8.0f,
			setup: (ctrl) =>
			{
				GD.Print("\n════════════════════════════════════════");
				GD.Print("[TEST 5] Player vs Enemy Trail (Active)");
				GD.Print("Testing: Player hitting currently-drawing enemy trail");
				GD.Print("════════════════════════════════════════");
				ctrl.SpawnTestEnemy(new Vector2(200, -100), 1); // Enemy moving DOWN
				ctrl.ResetPlayer(new Vector2(0, 0), 0); // Player moving RIGHT (will intersect vertical enemy trail)
				ctrl.SetPlayerAutoTest(0, "move_straight");
			},
			update: (ctrl, delta) => { }
		));

		// TEST 6: Enemy hitting boundary
		_testScenarios.Add(new TestScenario(
			"Enemy vs Boundary",
			"Enemy hits arena boundary",
			6.0f,
			setup: (ctrl) =>
			{
				GD.Print("\n════════════════════════════════════════");
				GD.Print("[TEST 6] Enemy vs Boundary");
				GD.Print("Testing: Enemy collision with arena walls");
				GD.Print("════════════════════════════════════════");
				ctrl.ResetPlayer(new Vector2(0, 0), 0);
				ctrl.SpawnTestEnemy(new Vector2(700, 0), 0); // Near right boundary, moving right
				ctrl.SetPlayerAutoTest(0, "idle");
			},
			update: (ctrl, delta) => { }
		));

		// TEST 7: Enemy crossing own trail (self-collision)
		_testScenarios.Add(new TestScenario(
			"Enemy Self-Collision",
			"Enemy crosses its own trail",
			10.0f,
			setup: (ctrl) =>
			{
				GD.Print("\n════════════════════════════════════════");
				GD.Print("[TEST 7] Enemy Self-Collision");
				GD.Print("Testing: Enemy hitting its own trail");
				GD.Print("════════════════════════════════════════");
				ctrl.ResetPlayer(new Vector2(0, 0), 0);
				ctrl.SpawnTestEnemy(new Vector2(-200, 0), 0, "box_pattern"); // Enemy starting left of center, moving right
				ctrl.SetPlayerAutoTest(0, "idle");
			},
			update: (ctrl, delta) => { }
		));

		// TEST 8: Player hitting obstacle
		_testScenarios.Add(new TestScenario(
			"Player vs Obstacle",
			"Player collides with arena obstacle",
			5.0f,
			setup: (ctrl) =>
			{
				GD.Print("\n════════════════════════════════════════");
				GD.Print("[TEST 8] Player vs Obstacle");
				GD.Print("Testing: Player collision with static obstacle");
				GD.Print("════════════════════════════════════════");
				// Aim player at the right wall obstacle in test arena (at x=500)
				ctrl.ResetPlayer(new Vector2(300, 0), 0); // Moving RIGHT toward right wall
				ctrl.SetPlayerAutoTest(0, "move_straight");
			},
			update: (ctrl, delta) => { }
		));

		// TEST 9: Two enemies colliding with each other
		_testScenarios.Add(new TestScenario(
			"Enemy vs Enemy Trail",
			"Two enemies collide with each other's trails",
			8.0f,
			setup: (ctrl) =>
			{
				GD.Print("\n════════════════════════════════════════");
				GD.Print("[TEST 9] Enemy vs Enemy Trail");
				GD.Print("Testing: Enemy-to-enemy collision");
				GD.Print("════════════════════════════════════════");
				ctrl.ResetPlayer(new Vector2(0, 0), 0);
				ctrl.SpawnTestEnemy(new Vector2(100, -100), 1); // Enemy 1 moving DOWN
				ctrl.SpawnTestEnemy(new Vector2(-100, 0), 0);    // Enemy 2 moving RIGHT (will cross enemy 1's vertical trail)
				ctrl.SetPlayerAutoTest(0, "idle");
			},
			update: (ctrl, delta) => { }
		));

		// TEST 10: Player rapid turns near boundary (edge case)
		_testScenarios.Add(new TestScenario(
			"Player Boundary Edge Case",
			"Player makes rapid turns near boundary",
			8.0f,
			setup: (ctrl) =>
			{
				GD.Print("\n════════════════════════════════════════");
				GD.Print("[TEST 10] Player Boundary Edge Case");
				GD.Print("Testing: Rapid turns near arena boundary");
				GD.Print("════════════════════════════════════════");
				ctrl.ResetPlayer(new Vector2(700, 0), 1); // Near right edge, moving down
				ctrl.SetPlayerAutoTest(0, "boundary_dance");
			},
			update: (ctrl, delta) => { }
		));
	}

	/// <summary>
	/// Starts the automated test sequence
	/// </summary>
	public void StartTests(Player player, Node2D gameLayer)
	{
		_player = player;
		_gameLayer = gameLayer;
		_testActive = true;
		_currentTestScenario = 0;
		_testTimer = 0.0f;

		// Enable collision test mode on player (logs collisions without dying)
		if (_player != null)
		{
			_player.Set("_collisionTestMode", true);
			GD.Print("[CollisionTest] Player collision test mode enabled");
		}

		GD.Print("\n╔═══════════════════════════════════════════════════════╗");
		GD.Print("║  COLLISION TEST SUITE - AUTOMATED TESTING INITIATED   ║");
		GD.Print("╚═══════════════════════════════════════════════════════╝");
		GD.Print($"Total scenarios: {_testScenarios.Count}\n");

		// Start first test
		if (_testScenarios.Count > 0)
		{
			StartScenario(0);
		}
	}

	/// <summary>
	/// Starts a specific test scenario
	/// </summary>
	private void StartScenario(int index)
	{
		if (index >= _testScenarios.Count) return;

		_currentTestScenario = index;
		_testTimer = 0.0f;

		var scenario = _testScenarios[index];
		scenario.Completed = false;

		// Clean up previous test
		CleanupPreviousTest();

		// Setup new test
		scenario.Setup?.Invoke(this);
	}

	/// <summary>
	/// Cleans up enemies and trails from previous test
	/// </summary>
	private void CleanupPreviousTest()
	{
		// Remove test enemies
		foreach (var enemy in _testEnemies)
		{
			if (enemy != null && IsInstanceValid(enemy))
			{
				enemy.QueueFree();
			}
		}
		_testEnemies.Clear();

		// Clear all trails
		if (_player != null)
		{
			_player.ClearTrail();
		}

		// Clear grid
		GridCollisionManager.Instance?.ClearCellsOfType(CellOccupant.PlayerTrail);
		GridCollisionManager.Instance?.ClearCellsOfType(CellOccupant.EnemyTrail);
	}

	public override void _Process(double delta)
	{
		if (!_testActive) return;

		float deltaF = (float)delta;
		_testTimer += deltaF;

		var scenario = _testScenarios[_currentTestScenario];

		// Update current scenario
		scenario.Update?.Invoke(this, deltaF);

		// Check if scenario is complete
		if (_testTimer >= scenario.Duration)
		{
			GD.Print($"\n[TEST {_currentTestScenario + 1}] Scenario '{scenario.Name}' completed");
			GD.Print("----------------------------------------\n");

			scenario.Completed = true;
			_currentTestScenario++;

			// Check if all tests complete
			if (_currentTestScenario >= _testScenarios.Count)
			{
				CompleteAllTests();
			}
			else
			{
				// Start next test
				StartScenario(_currentTestScenario);
			}
		}
	}

	/// <summary>
	/// Called when all test scenarios are complete
	/// </summary>
	private void CompleteAllTests()
	{
		_testActive = false;

		// Disable collision test mode and auto test mode on player
		if (_player != null)
		{
			_player.Set("_collisionTestMode", false);
			_player.Set("_autoTestMode", false);
			GD.Print("[CollisionTest] Player test modes disabled");
		}

		GD.Print("\n╔═══════════════════════════════════════════════════════╗");
		GD.Print("║     COLLISION TEST SUITE COMPLETE                     ║");
		GD.Print("╚═══════════════════════════════════════════════════════╝");
		GD.Print($"Total tests run: {_testScenarios.Count}");
		GD.Print("Review logs above for collision behavior analysis\n");

		CleanupPreviousTest();
	}

	// ========== HELPER METHODS ==========

	private void ResetPlayer(Vector2 position, int direction)
	{
		if (_player == null) return;

		_player.GlobalPosition = position;
		_player.Set("_currentDirection", direction);
		_player.Rotation = direction * Mathf.Pi / 2.0f;
		_player.ClearTrail();

		GD.Print($"[CollisionTest] Player reset: pos={position}, dir={direction}");
	}

	private void SetPlayerAutoTest(int step, string pattern)
	{
		if (_player == null) return;

		_player.Set("_autoTestMode", true);
		_player.Set("_autoTestStep", step);
		_player.Set("_autoTestTimer", 0.0f);
		_player.Set("_autoTestPattern", pattern);

		GD.Print($"[CollisionTest] Player auto-test enabled: pattern='{pattern}'");
	}

	private void SpawnTestEnemy(Vector2 position, int direction, string pattern = "move_straight")
	{
		if (_gameLayer == null) return;

		var enemyScene = GD.Load<PackedScene>("res://Scenes/Enemies/EnemyCycle.tscn");
		if (enemyScene == null)
		{
			GD.PrintErr("[CollisionTest] Failed to load EnemyCycle scene!");
			return;
		}

		var enemy = enemyScene.Instantiate<EnemyCycle>();
		enemy.GlobalPosition = position;
		enemy.Set("_currentDirection", direction);
		enemy.Rotation = direction * Mathf.Pi / 2.0f;
		enemy.Set("_autoTestMode", true);
		enemy.Set("_autoTestPattern", pattern);
		enemy.Set("_collisionTestMode", true);  // Log collisions without dying

		_gameLayer.AddChild(enemy);
		_testEnemies.Add(enemy);

		GD.Print($"[CollisionTest] Enemy spawned: pos={position}, dir={direction}, pattern='{pattern}'");
	}

	private void SpawnDelayedEnemy(Vector2 position, int direction, float delay)
	{
		GetTree().CreateTimer(delay).Timeout += () =>
		{
			SpawnTestEnemy(position, direction);
		};
	}

	// ========== CLEANUP ==========
	public override void _ExitTree()
	{
		_instance = null;
		CleanupPreviousTest();
	}
}
