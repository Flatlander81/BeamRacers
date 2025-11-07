using Godot;
using System;

/// <summary>
/// Main scene controller for Light Cycle Escape.
/// Handles the initial game state and scene management.
/// </summary>
public partial class Main : Node2D
{
	// ========== EXPORTS (editable in Inspector) ==========
	[Export] public int GridSize = 50;
	[Export] public int GridExtent = 2000;

	// Scene references
	private Camera2D _camera;
	private Node2D _gameLayer;
	private CanvasLayer _uiLayer;
	private Node2D _effectsLayer;
	private Node2D _gridBackground;

	// UI references
	private ColorRect _titleBackground;  // Title screen background
	private ColorRect _gameBackground;   // Game background
	private Label _titleLabel;

	// Game state
	private bool _gameStarted = false;
	private GameManager _gameManager;
	private GridCollisionManager _gridCollisionManager;
	private TrailManager _trailManager;
	private CollisionTestController _collisionTestController;
	private Player _player;
	private Arena _arena;
	private EnemySpawner _enemySpawner;

	public override void _Ready()
	{
		GD.Print("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
		GD.Print("[Main] Initializing Main Scene");
		GD.Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

		// Get reference to GameManager
		_gameManager = GetNode<GameManager>("/root/GameManager");
		GD.Print("[Main] ✓ GameManager reference acquired");

		// Get scene layer references
		InitializeSceneLayers();

		// Initialize GridCollisionManager (must be first - needed by all collision)
		_gridCollisionManager = new GridCollisionManager();
		_gridCollisionManager.Name = "GridCollisionManager";
		_gridCollisionManager.SetGridSize(GridSize);
		AddChild(_gridCollisionManager);
		GD.Print("[Main] ✓ GridCollisionManager initialized");

		// Initialize TrailManager (must be before player/enemies spawn)
		_trailManager = new TrailManager();
		_trailManager.Name = "TrailManager";
		AddChild(_trailManager);
		GD.Print("[Main] ✓ TrailManager initialized");

		// Initialize enemy spawner
		_enemySpawner = new EnemySpawner();
		_enemySpawner.Name = "EnemySpawner";
		AddChild(_enemySpawner);
		GD.Print("[Main] ✓ EnemySpawner initialized");

		// Initialize collision test controller
		_collisionTestController = new CollisionTestController();
		_collisionTestController.Name = "CollisionTestController";
		AddChild(_collisionTestController);
		GD.Print("[Main] ✓ CollisionTestController initialized");

		// Create the start screen UI
		CreateStartScreen();

		GD.Print("[Main] ✓ Main scene initialized");
		GD.Print("[Main] ⌨ Press SPACE to start");
		GD.Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");
	}

	/// <summary>
	/// Initializes or retrieves references to the main scene layers
	/// </summary>
	/// <summary>
	/// Generic helper to initialize or find a layer node
	/// </summary>
	private T InitializeLayer<T>(ref T field, string nodeName, Action<T> configure = null, string createdMessage = null) where T : Node, new()
	{
		field = GetNodeOrNull<T>(nodeName);
		if (field == null)
		{
			field = new T();
			field.Name = nodeName;
			configure?.Invoke(field);
			AddChild(field);
			GD.Print($"[Main] ✓ {nodeName} created{(createdMessage != null ? $" ({createdMessage})" : "")}");
		}
		else
		{
			GD.Print($"[Main] ✓ {nodeName} found");
		}
		return field;
	}

	private void InitializeSceneLayers()
	{
		// Camera2D with custom configuration
		InitializeLayer(ref _camera, "Camera2D", camera =>
		{
			camera.Enabled = true;
			camera.Zoom = Vector2.One;
			camera.PositionSmoothingEnabled = true;
			camera.PositionSmoothingSpeed = 5.0f;
		}, "zoom: 1.0, smoothing: enabled");

		// Simple layers
		InitializeLayer(ref _gameLayer, "GameLayer");
		InitializeLayer(ref _uiLayer, "UILayer");
		InitializeLayer(ref _effectsLayer, "EffectsLayer");
	}

	/// <summary>
	/// Creates the initial start screen with title and instructions
	/// </summary>
	private void CreateStartScreen()
	{
		GD.Print("[Main] Creating start screen...");

		// Get viewport size for positioning
		Vector2 viewportSize = GetViewportRect().Size;

		// Create black background for title screen
		_titleBackground = new ColorRect();
		_titleBackground.Name = "TitleBackground";
		_titleBackground.Color = Colors.Black;
		_titleBackground.Size = viewportSize;
		_titleBackground.Position = Vector2.Zero;
		_uiLayer.AddChild(_titleBackground);
		GD.Print($"[Main] ✓ Title background created (size: {viewportSize})");

		// Create title label
		_titleLabel = new Label();
		_titleLabel.Name = "TitleLabel";
		_titleLabel.Text = "LIGHT CYCLE ESCAPE\n\nPRESS SPACE TO START";
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_titleLabel.VerticalAlignment = VerticalAlignment.Center;

		// Set font size
		_titleLabel.AddThemeColorOverride("font_color", Colors.White);
		_titleLabel.AddThemeFontSizeOverride("font_size", 32);

		// Position in center of screen
		_titleLabel.Size = viewportSize;
		_titleLabel.Position = Vector2.Zero;

		_uiLayer.AddChild(_titleLabel);
		GD.Print("[Main] ✓ Title label created (white, 32px, centered)");
	}

	/// <summary>
	/// Initiates the game start sequence
	/// </summary>
	private void StartGame()
	{
		if (_gameStarted)
			return;

		_gameStarted = true;

		GD.Print("\n[Main] ▶▶▶ STARTING GAME ◀◀◀");

		// Hide title screen
		if (_titleLabel != null)
		{
			_titleLabel.Visible = false;
		}
		if (_titleBackground != null)
		{
			_titleBackground.Visible = false;
		}
		GD.Print("[Main] ✓ Start screen hidden");

		// Create game background (in game layer, behind everything)
		CreateGameBackground();

		// Tell GameManager to start a new run
		if (_gameManager != null)
		{
			_gameManager.StartNewRun();
			GD.Print("[Main] ✓ New run initiated via GameManager");
		}
		else
		{
			GD.PrintErr("[Main] ERROR: GameManager not found! Cannot start game.");
			return;
		}

		// Create grid background
		CreateGridBackground();

		// Spawn the arena
		SpawnArena();

		// Spawn the player
		SpawnPlayer();

		// Spawn enemies after player and arena are ready
		SpawnEnemies();

		GD.Print("[Main] ✓ Game start sequence complete\n");
	}

	/// <summary>
	/// Creates the black background for the game
	/// </summary>
	private void CreateGameBackground()
	{
		// Create a large black ColorRect that follows the camera
		_gameBackground = new ColorRect();
		_gameBackground.Name = "GameBackground";
		_gameBackground.Color = Colors.Black;
		_gameBackground.Size = new Vector2(10000, 10000);
		_gameBackground.Position = new Vector2(-5000, -5000); // Center it
		_gameBackground.ZIndex = -1000; // Way behind everything

		_gameLayer.AddChild(_gameBackground);
		GD.Print("[Main] ✓ Game background created (black, 10000x10000)");
	}

	/// <summary>
	/// Creates a visual grid background for movement reference
	/// </summary>
	private void CreateGridBackground()
	{
		_gridBackground = new Node2D();
		_gridBackground.Name = "GridBackground";
		_gridBackground.ZIndex = -100; // Behind everything

		// Create grid lines using exported values
		Color gridColor = new Color(1.0f, 1.0f, 0.0f, 0.3f); // Yellow with transparency

		// Vertical lines
		for (int x = -GridExtent; x <= GridExtent; x += GridSize)
		{
			var line = new Line2D();
			line.AddPoint(new Vector2(x, -GridExtent));
			line.AddPoint(new Vector2(x, GridExtent));
			line.DefaultColor = gridColor;
			line.Width = 1.0f;
			_gridBackground.AddChild(line);
		}

		// Horizontal lines
		for (int y = -GridExtent; y <= GridExtent; y += GridSize)
		{
			var line = new Line2D();
			line.AddPoint(new Vector2(-GridExtent, y));
			line.AddPoint(new Vector2(GridExtent, y));
			line.DefaultColor = gridColor;
			line.Width = 1.0f;
			_gridBackground.AddChild(line);
		}

		// Add bright border lines to mark grid boundaries
		Color borderColor = new Color(1.0f, 1.0f, 0.0f, 0.8f); // Brighter yellow

		// Top border
		var borderTop = new Line2D();
		borderTop.AddPoint(new Vector2(-GridExtent, -GridExtent));
		borderTop.AddPoint(new Vector2(GridExtent, -GridExtent));
		borderTop.DefaultColor = borderColor;
		borderTop.Width = 3.0f;
		_gridBackground.AddChild(borderTop);

		// Bottom border
		var borderBottom = new Line2D();
		borderBottom.AddPoint(new Vector2(-GridExtent, GridExtent));
		borderBottom.AddPoint(new Vector2(GridExtent, GridExtent));
		borderBottom.DefaultColor = borderColor;
		borderBottom.Width = 3.0f;
		_gridBackground.AddChild(borderBottom);

		// Left border
		var borderLeft = new Line2D();
		borderLeft.AddPoint(new Vector2(-GridExtent, -GridExtent));
		borderLeft.AddPoint(new Vector2(-GridExtent, GridExtent));
		borderLeft.DefaultColor = borderColor;
		borderLeft.Width = 3.0f;
		_gridBackground.AddChild(borderLeft);

		// Right border
		var borderRight = new Line2D();
		borderRight.AddPoint(new Vector2(GridExtent, -GridExtent));
		borderRight.AddPoint(new Vector2(GridExtent, GridExtent));
		borderRight.DefaultColor = borderColor;
		borderRight.Width = 3.0f;
		_gridBackground.AddChild(borderRight);

		_gameLayer.AddChild(_gridBackground);
		GD.Print($"[Main] ✓ Grid background created ({GridSize}px yellow grid, extent: {GridExtent})");
	}

	/// <summary>
	/// Spawns the player in the game world
	/// </summary>
	private void SpawnPlayer()
	{
		// Load the player scene
		var playerScene = GD.Load<PackedScene>("res://Scenes/Player/Player.tscn");
		if (playerScene == null)
		{
			GD.PrintErr("[Main] ERROR: Failed to load Player scene!");
			return;
		}

		// Instantiate the player
		_player = playerScene.Instantiate<Player>();
		if (_player == null)
		{
			GD.PrintErr("[Main] ERROR: Failed to instantiate Player!");
			return;
		}

		// Set starting position (center of screen)
		_player.Position = Vector2.Zero;

		// Set grid parameters for grid-snapped turning and boundary detection
		_player.GridExtent = GridExtent;

		// Add to game layer
		_gameLayer.AddChild(_player);

		GD.Print("[Main] ✓ Player spawned at position (0, 0)");

		// Make camera follow player
		if (_camera != null)
		{
			// We'll update camera position in _Process
			GD.Print("[Main] ✓ Camera will follow player");
		}

		// Set arena bounds for player
		if (_arena != null)
		{
			var bounds = _arena.GetArenaBounds();
			GD.Print($"[Main] ✓ Arena bounds available: {bounds.Size.X}x{bounds.Size.Y}");
		}
	}

	/// <summary>
	/// Spawns the arena for the current room
	/// </summary>
	private void SpawnArena()
	{
		// Load the arena scene
		var arenaScene = GD.Load<PackedScene>("res://Scenes/Arena/Arena.tscn");
		if (arenaScene == null)
		{
			GD.PrintErr("[Main] ERROR: Failed to load Arena scene!");
			return;
		}

		// Instantiate the arena
		_arena = arenaScene.Instantiate<Arena>();
		if (_arena == null)
		{
			GD.PrintErr("[Main] ERROR: Failed to instantiate Arena!");
			return;
		}

		// Add to game layer (BEFORE player so it's behind)
		_gameLayer.AddChild(_arena);

		// Generate arena for current room
		if (_gameManager != null)
		{
			_arena.GenerateArena(_gameManager.CurrentRoom);
			GD.Print($"[Main] ✓ Arena spawned and generated for room {_gameManager.CurrentRoom}");
		}
		else
		{
			// Fallback if GameManager not found
			_arena.GenerateArena(1);
			GD.Print("[Main] ✓ Arena spawned and generated (default room 1)");
		}
	}

	/// <summary>
	/// Spawns enemies for the current room
	/// </summary>
	private void SpawnEnemies()
	{
		if (_enemySpawner == null)
		{
			GD.PrintErr("[Main] ERROR: EnemySpawner not initialized!");
			return;
		}

		if (_player == null)
		{
			GD.PrintErr("[Main] ERROR: Cannot spawn enemies - Player not found!");
			return;
		}

		if (_gameManager == null)
		{
			GD.PrintErr("[Main] ERROR: Cannot spawn enemies - GameManager not found!");
			return;
		}

		if (_arena == null)
		{
			GD.PrintErr("[Main] ERROR: Cannot spawn enemies - Arena not found!");
			return;
		}

		// Get arena bounds
		Rect2 arenaBounds = _arena.GetArenaBounds();

		// Spawn enemies for current room
		_enemySpawner.SpawnEnemies(
			_gameManager.CurrentRoom,
			_player.GlobalPosition,
			_gameLayer,
			arenaBounds,
			GridSize
		);

		GD.Print("[Main] ✓ Enemies spawned for current room");
	}

	public override void _Process(double delta)
	{
		// Only check for start input if we haven't started yet and we're in main menu
		if (!_gameStarted && _gameManager != null && _gameManager.CurrentState == GameManager.GameState.MainMenu)
		{
			// Check for Space key press (using Godot's input system)
			if (Input.IsActionJustPressed("ui_accept") || Input.IsKeyPressed(Key.Space))
			{
				StartGame();
			}
		}

		// Update camera to follow player
		if (_player != null && _camera != null)
		{
			_camera.Position = _player.Position;
		}

	}

	public override void _Input(InputEvent @event)
	{
		// TEST HOTKEYS: F1-F6 to switch arena templates (only during gameplay)
		if (!_gameStarted || _arena == null)
			return;

		// Check for F-key presses
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			switch (keyEvent.Keycode)
			{
				case Key.F1:
					_arena.GenerateArenaByTemplate(0); // The Box
					GD.Print("[Main] ⚡ F1 pressed - Loading 'The Box' arena");
					break;
				case Key.F2:
					_arena.GenerateArenaByTemplate(1); // Four Pillars
					GD.Print("[Main] ⚡ F2 pressed - Loading 'Four Pillars' arena");
					break;
				case Key.F3:
					_arena.GenerateArenaByTemplate(2); // The Cross
					GD.Print("[Main] ⚡ F3 pressed - Loading 'The Cross' arena");
					break;
				case Key.F4:
					_arena.GenerateArenaByTemplate(3); // The Ring
					GD.Print("[Main] ⚡ F4 pressed - Loading 'The Ring' arena");
					break;
				case Key.F5:
					_arena.GenerateArenaByTemplate(4); // Scattered
					GD.Print("[Main] ⚡ F5 pressed - Loading 'Scattered' arena");
					break;
				case Key.F6:
					_arena.GenerateProceduralArena(); // Procedural (random each time)
					GD.Print("[Main] ⚡ F6 pressed - Generating PROCEDURAL arena (random)");
					break;
				case Key.F7:
					GD.Print("[Main] ⚡ F7 pressed - Initializing COLLISION TEST MODE");
					ResetForCollisionTest();
					break;
			}
		}
	}

	/// <summary>
	/// Resets game state for collision testing
	/// </summary>
	private void ResetForCollisionTest()
	{
		GD.Print("\n[Main] ════════════ RESETTING FOR COLLISION TEST ════════════");

		// 1. Clear all existing enemies
		if (_gameLayer != null)
		{
			foreach (Node child in _gameLayer.GetChildren())
			{
				if (child is EnemyCycle)
				{
					child.QueueFree();
				}
			}
			GD.Print("[Main] ✓ Cleared all enemies");
		}

		// 2. Reset player to starting position
		if (_player != null)
		{
			_player.GlobalPosition = Vector2.Zero;
			_player.Set("_currentDirection", 0); // Facing RIGHT
			_player.Rotation = 0;
			_player.Set("_isDead", false);
			_player.Set("_inputEnabled", true);
			_player.Velocity = Vector2.Zero;

			// Clear player trail
			_player.ClearTrail();
			GD.Print("[Main] ✓ Player reset to (0, 0) facing RIGHT");
		}

		// 3. Clear all trails from grid
		if (GridCollisionManager.Instance != null)
		{
			GridCollisionManager.Instance.ClearCellsOfType(CellOccupant.PlayerTrail);
			GridCollisionManager.Instance.ClearCellsOfType(CellOccupant.EnemyTrail);
			GD.Print("[Main] ✓ Cleared all trails from grid");
		}

		// 4. Load collision test arena
		if (_arena != null)
		{
			_arena.GenerateArenaByTemplate(5);
			GD.Print("[Main] ✓ Loaded Collision Test Arena");
		}

		// 5. Start automated test suite
		if (_collisionTestController != null && _player != null && _gameLayer != null)
		{
			_collisionTestController.StartTests(_player, _gameLayer);
			GD.Print("[Main] 🤖 COMPREHENSIVE COLLISION TEST SUITE STARTED");
		}

		GD.Print("[Main] ═══════════════════════════════════════════════════════\n");
	}

	/// <summary>
	/// Public method to get the game layer for spawning entities
	/// </summary>
	public Node2D GetGameLayer() => _gameLayer;

	/// <summary>
	/// Public method to get the UI layer for UI elements
	/// </summary>
	public CanvasLayer GetUILayer() => _uiLayer;

	/// <summary>
	/// Public method to get the effects layer for visual effects
	/// </summary>
	public Node2D GetEffectsLayer() => _effectsLayer;

	/// <summary>
	/// Public method to get the camera
	/// </summary>
	public Camera2D GetCamera() => _camera;
}
