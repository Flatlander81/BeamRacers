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

	// UI references
	private ColorRect _background;
	private Label _titleLabel;

	// Game state
	private bool _gameStarted = false;
	private GameManager _gameManager;

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

		// Hide start screen
		if (_titleLabel != null)
		{
			_titleLabel.Visible = false;
			GD.Print("[Main] ✓ Start screen hidden");
		}

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

		// TODO: Later steps will:
		// - Load the first arena room
		// - Spawn the player
		// - Initialize game UI

		GD.Print("[Main] ✓ Game start sequence complete");
		GD.Print("[Main] (Note: Arena and player spawning will be added in later steps)\n");
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
