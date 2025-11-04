using Godot;
using System;

/// <summary>
/// Main scene controller for Light Cycle Escape.
/// Handles the initial game state and scene management.
/// </summary>
public partial class Main : Node2D
{
	// Scene references
	private Camera2D _camera;
	private Node2D _gameLayer;
	private CanvasLayer _uiLayer;
	private Node2D _effectsLayer;
	private Node2D _gridBackground;

	// UI references
	private ColorRect _background;
	private Label _titleLabel;

	// Game state
	private bool _gameStarted = false;
	private GameManager _gameManager;
	private Player _player;

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

		// Create the start screen UI
		CreateStartScreen();

		GD.Print("[Main] ✓ Main scene initialized");
		GD.Print("[Main] ⌨ Press SPACE to start");
		GD.Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");
	}

	/// <summary>
	/// Initializes or retrieves references to the main scene layers
	/// </summary>
	private void InitializeSceneLayers()
	{
		// Camera2D
		_camera = GetNodeOrNull<Camera2D>("Camera2D");
		if (_camera == null)
		{
			_camera = new Camera2D();
			_camera.Name = "Camera2D";
			_camera.Enabled = true;
			_camera.Zoom = Vector2.One;
			_camera.PositionSmoothingEnabled = true;
			_camera.PositionSmoothingSpeed = 5.0f;
			AddChild(_camera);
			GD.Print("[Main] ✓ Camera2D created (zoom: 1.0, smoothing: enabled)");
		}
		else
		{
			GD.Print("[Main] ✓ Camera2D found");
		}

		// GameLayer
		_gameLayer = GetNodeOrNull<Node2D>("GameLayer");
		if (_gameLayer == null)
		{
			_gameLayer = new Node2D();
			_gameLayer.Name = "GameLayer";
			AddChild(_gameLayer);
			GD.Print("[Main] ✓ GameLayer created");
		}
		else
		{
			GD.Print("[Main] ✓ GameLayer found");
		}

		// UILayer
		_uiLayer = GetNodeOrNull<CanvasLayer>("UILayer");
		if (_uiLayer == null)
		{
			_uiLayer = new CanvasLayer();
			_uiLayer.Name = "UILayer";
			AddChild(_uiLayer);
			GD.Print("[Main] ✓ UILayer created");
		}
		else
		{
			GD.Print("[Main] ✓ UILayer found");
		}

		// EffectsLayer
		_effectsLayer = GetNodeOrNull<Node2D>("EffectsLayer");
		if (_effectsLayer == null)
		{
			_effectsLayer = new Node2D();
			_effectsLayer.Name = "EffectsLayer";
			AddChild(_effectsLayer);
			GD.Print("[Main] ✓ EffectsLayer created");
		}
		else
		{
			GD.Print("[Main] ✓ EffectsLayer found");
		}
	}

	/// <summary>
	/// Creates the initial start screen with title and instructions
	/// </summary>
	private void CreateStartScreen()
	{
		GD.Print("[Main] Creating start screen...");

		// Get viewport size for positioning
		Vector2 viewportSize = GetViewportRect().Size;

		// Create black background
		_background = new ColorRect();
		_background.Name = "Background";
		_background.Color = Colors.Black;
		_background.Size = viewportSize;
		_background.Position = Vector2.Zero;
		_uiLayer.AddChild(_background);
		GD.Print($"[Main] ✓ Background created (size: {viewportSize})");

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

		// Hide start screen text (keep background visible)
		if (_titleLabel != null)
		{
			_titleLabel.Visible = false;
		}
		// Keep the black background visible during gameplay
		GD.Print("[Main] ✓ Start screen hidden");

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

		// Spawn the player
		SpawnPlayer();

		GD.Print("[Main] ✓ Game start sequence complete\n");
	}

	/// <summary>
	/// Creates a visual grid background for movement reference
	/// </summary>
	private void CreateGridBackground()
	{
		_gridBackground = new Node2D();
		_gridBackground.Name = "GridBackground";
		_gridBackground.ZIndex = -100; // Behind everything

		// Create grid lines
		const int GRID_SIZE = 50;
		const int GRID_EXTENT = 2000; // How far the grid extends
		Color gridColor = new Color(1.0f, 1.0f, 0.0f, 0.3f); // Yellow with transparency

		// Vertical lines
		for (int x = -GRID_EXTENT; x <= GRID_EXTENT; x += GRID_SIZE)
		{
			var line = new Line2D();
			line.AddPoint(new Vector2(x, -GRID_EXTENT));
			line.AddPoint(new Vector2(x, GRID_EXTENT));
			line.DefaultColor = gridColor;
			line.Width = 1.0f;
			_gridBackground.AddChild(line);
		}

		// Horizontal lines
		for (int y = -GRID_EXTENT; y <= GRID_EXTENT; y += GRID_SIZE)
		{
			var line = new Line2D();
			line.AddPoint(new Vector2(-GRID_EXTENT, y));
			line.AddPoint(new Vector2(GRID_EXTENT, y));
			line.DefaultColor = gridColor;
			line.Width = 1.0f;
			_gridBackground.AddChild(line);
		}

		// Add origin marker (brighter yellow)
		var originLineH = new Line2D();
		originLineH.AddPoint(new Vector2(-100, 0));
		originLineH.AddPoint(new Vector2(100, 0));
		originLineH.DefaultColor = new Color(1.0f, 1.0f, 0.0f, 0.6f);
		originLineH.Width = 2.0f;
		_gridBackground.AddChild(originLineH);

		var originLineV = new Line2D();
		originLineV.AddPoint(new Vector2(0, -100));
		originLineV.AddPoint(new Vector2(0, 100));
		originLineV.DefaultColor = new Color(1.0f, 1.0f, 0.0f, 0.6f);
		originLineV.Width = 2.0f;
		_gridBackground.AddChild(originLineV);

		_gameLayer.AddChild(_gridBackground);
		GD.Print("[Main] ✓ Grid background created (50px yellow grid on black)");
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

		// Add to game layer
		_gameLayer.AddChild(_player);

		GD.Print("[Main] ✓ Player spawned at position (0, 0)");

		// Make camera follow player
		if (_camera != null)
		{
			// We'll update camera position in _Process
			GD.Print("[Main] ✓ Camera will follow player");
		}
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
