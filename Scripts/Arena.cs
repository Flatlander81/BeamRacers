using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Defines an obstacle in the arena
/// </summary>
public class ObstacleDefinition
{
	public enum Type
	{
		Pillar,
		Wall
	}

	public Type ObstacleType { get; set; }
	public Vector2 Position { get; set; }
	public float Size { get; set; }  // Pillar radius or wall length
	public float Rotation { get; set; }  // In radians

	public ObstacleDefinition(Type type, Vector2 position, float size, float rotation = 0)
	{
		ObstacleType = type;
		Position = position;
		Size = size;
		Rotation = rotation;
	}
}

/// <summary>
/// Defines a template for arena generation
/// </summary>
public class ArenaTemplate
{
	public string Name { get; set; }
	public List<ObstacleDefinition> Obstacles { get; set; }
	public float DifficultyRating { get; set; }  // 0-1 scale

	public ArenaTemplate(string name, float difficulty)
	{
		Name = name;
		DifficultyRating = difficulty;
		Obstacles = new List<ObstacleDefinition>();
	}

	public void AddObstacle(ObstacleDefinition obstacle)
	{
		Obstacles.Add(obstacle);
	}
}

/// <summary>
/// Arena scene controller - handles procedural arena generation with boundaries and obstacles
/// </summary>
public partial class Arena : Node2D
{
	// Child nodes
	private Node2D _boundaries;
	private Node2D _obstacles;
	private Node2D _grid;

	// Arena properties
	private Vector2 _arenaSize = new Vector2(1600, 900);
	private float _currentScale = 1.0f;

	// Templates
	private List<ArenaTemplate> _templates;
	private ArenaTemplate _currentTemplate;

	// Random generator
	private Random _random = new Random();

	// Procedural Generation Settings
	[Export] public float SafeZoneRadius = 200f;  // Clear area around spawn point
	[Export] public float MinObstacleDistance = 120f;  // Minimum distance between obstacles
	[Export] public float SafeBoundaryMargin = 380f;  // Stay within boundaries to survive rotation
	[Export] public int MinObstacles = 8;
	[Export] public int MaxObstacles = 15;

	// Constants
	private const int PILLAR_VERTICES = 16;
	private static readonly Color BOUNDARY_COLOR = new Color(0, 0.5f, 1, 1);  // Blue
	private const float BOUNDARY_WIDTH = 2.0f;
	private static readonly Color GRID_COLOR = new Color(0, 0.3f, 0.5f, 0.15f);  // Subtle blue
	private float GridSpacing => GridCollisionManager.Instance?.GetGridSize() ?? 50;

	public override void _Ready()
	{
		GD.Print("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
		GD.Print("[Arena] Initializing Arena System");
		GD.Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

		// Create child nodes
		InitializeNodes();

		// Create templates
		InitializeTemplates();

		GD.Print("[Arena] ✓ Arena system ready");
		GD.Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");
	}

	/// <summary>
	/// Initialize the child node structure
	/// </summary>
	private void InitializeNodes()
	{
		// Boundaries container
		_boundaries = new Node2D();
		_boundaries.Name = "Boundaries";
		AddChild(_boundaries);
		GD.Print("[Arena] ✓ Boundaries node created");

		// Obstacles container
		_obstacles = new Node2D();
		_obstacles.Name = "Obstacles";
		AddChild(_obstacles);
		GD.Print("[Arena] ✓ Obstacles node created");

		// Grid container (for visual grid)
		_grid = new Node2D();
		_grid.Name = "Grid";
		AddChild(_grid);
		GD.Print("[Arena] ✓ Grid node created");
	}

	/// <summary>
	/// Initialize all arena templates
	/// </summary>
	private void InitializeTemplates()
	{
		_templates = new List<ArenaTemplate>();

		// Template 1: "The Box" - Empty arena
		var theBox = new ArenaTemplate("The Box", 0.1f);
		_templates.Add(theBox);

		// Template 2: "Four Pillars" - Classic symmetrical layout
		var fourPillars = new ArenaTemplate("Four Pillars", 0.3f);
		fourPillars.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Pillar, new Vector2(-400, -225), 30));
		fourPillars.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Pillar, new Vector2(400, -225), 30));
		fourPillars.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Pillar, new Vector2(-400, 225), 30));
		fourPillars.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Pillar, new Vector2(400, 225), 30));
		_templates.Add(fourPillars);

		// Template 3: "The Cross" - Two intersecting walls forming a + shape
		var theCross = new ArenaTemplate("The Cross", 0.4f);
		theCross.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Wall, new Vector2(0, 0), 400, 0));  // Horizontal
		theCross.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Wall, new Vector2(0, 0), 300, Mathf.Pi / 2));  // Vertical
		_templates.Add(theCross);

		// Template 4: "The Ring" - Inner rectangle forming a donut
		var theRing = new ArenaTemplate("The Ring", 0.5f);
		theRing.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Wall, new Vector2(0, -150), 400, 0));  // Top
		theRing.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Wall, new Vector2(0, 150), 400, 0));   // Bottom
		theRing.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Wall, new Vector2(-200, 0), 300, Mathf.Pi / 2));  // Left
		theRing.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Wall, new Vector2(200, 0), 300, Mathf.Pi / 2));   // Right
		_templates.Add(theRing);

		// Template 5: "Scattered" - Random pillars and walls
		var scattered = new ArenaTemplate("Scattered", 0.6f);
		// Add 8 random-ish obstacles (but predetermined for consistency)
		// Keep within ±400 safe zone to stay in bounds after rotation
		scattered.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Pillar, new Vector2(-380, -250), 35));
		scattered.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Pillar, new Vector2(350, -200), 40));
		scattered.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Pillar, new Vector2(-280, 180), 30));
		scattered.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Pillar, new Vector2(280, 280), 35));
		scattered.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Wall, new Vector2(-150, -120), 140, Mathf.Pi / 4));
		scattered.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Wall, new Vector2(180, 80), 120, -Mathf.Pi / 6));
		scattered.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Pillar, new Vector2(0, 0), 25));
		scattered.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Pillar, new Vector2(380, -320), 30));
		_templates.Add(scattered);

		// Template 6: "COLLISION TEST" - Forces specific collision scenarios for debugging
		var collisionTest = new ArenaTemplate("Collision Test Arena", 0.1f);
		// Create a narrow corridor on the RIGHT side forcing tight turns
		// Player spawns at (0,0) facing RIGHT, corridor is 200 units wide (4 grid cells)
		// Corridor runs from x=200 to x=500, y=-100 to y=100
		collisionTest.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Wall, new Vector2(350, -100), 300, 0));  // Top wall (horizontal)
		collisionTest.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Wall, new Vector2(350, 100), 300, 0));   // Bottom wall (horizontal)
		collisionTest.AddObstacle(new ObstacleDefinition(ObstacleDefinition.Type.Wall, new Vector2(500, 0), 200, Mathf.Pi / 2));  // Right wall (vertical)
		// Left side is open at x=200 so player drives straight into the corridor
		_templates.Add(collisionTest);

		GD.Print($"[Arena] ✓ Created {_templates.Count} arena templates");
	}

	/// <summary>
	/// Generate the arena boundaries
	/// </summary>
	/// <param name="arenaSize">Size of the arena (default 1600x900)</param>
	private void GenerateBoundaries(Vector2 arenaSize)
	{
		// Clear existing boundaries
		ClearAllChildren(_boundaries);

		float halfWidth = arenaSize.X / 2;
		float halfHeight = arenaSize.Y / 2;

		// Define wall endpoints for Line2D visual and grid collision
		var wallLines = new[]
		{
			("TopWall", new Vector2(-halfWidth, -halfHeight), new Vector2(halfWidth, -halfHeight)),
			("BottomWall", new Vector2(-halfWidth, halfHeight), new Vector2(halfWidth, halfHeight)),
			("LeftWall", new Vector2(-halfWidth, -halfHeight), new Vector2(-halfWidth, halfHeight)),
			("RightWall", new Vector2(halfWidth, -halfHeight), new Vector2(halfWidth, halfHeight))
		};

		// Create visual boundaries and register with grid collision
		foreach (var (name, start, end) in wallLines)
		{
			CreateBoundaryWall(name, start, end);
		}

		GD.Print($"[Arena] ✓ Generated boundaries ({arenaSize.X}x{arenaSize.Y}) with grid collision");
	}

	/// <summary>
	/// Creates a single boundary wall (visual + grid collision)
	/// </summary>
	private void CreateBoundaryWall(string name, Vector2 start, Vector2 end)
	{
		// Create visual container
		var wall = new Node2D();
		wall.Name = name;

		// Create visual Line2D
		var line = new Line2D();
		line.AddPoint(start);
		line.AddPoint(end);
		line.DefaultColor = BOUNDARY_COLOR;
		line.Width = BOUNDARY_WIDTH;
		wall.AddChild(line);

		_boundaries.AddChild(wall);

		// Register with grid collision system
		if (GridCollisionManager.Instance != null)
		{
			GridCollisionManager.Instance.SetLine(start, end, CellOccupant.Boundary);
		}
	}

	/// <summary>
	/// Spawn an obstacle based on its definition
	/// </summary>
	private void SpawnObstacle(ObstacleDefinition def)
	{
		if (def.ObstacleType == ObstacleDefinition.Type.Pillar)
		{
			SpawnPillar(def.Position, def.Size);
		}
		else if (def.ObstacleType == ObstacleDefinition.Type.Wall)
		{
			SpawnWall(def.Position, def.Size, def.Rotation);
		}
	}

	/// <summary>
	/// Spawn a pillar obstacle (visual + grid collision)
	/// </summary>
	private void SpawnPillar(Vector2 position, float radius)
	{
		// Create visual container
		var pillar = new Node2D();
		pillar.Position = position;

		// Create visual polygon (filled circle)
		var polygon = new Polygon2D();
		var points = new List<Vector2>();
		for (int i = 0; i < PILLAR_VERTICES; i++)
		{
			float angle = (i / (float)PILLAR_VERTICES) * Mathf.Pi * 2;
			points.Add(new Vector2(
				Mathf.Cos(angle) * radius,
				Mathf.Sin(angle) * radius
			));
		}
		polygon.Polygon = points.ToArray();
		polygon.Color = new Color(0.1f, 0.1f, 0.2f, 1);  // Dark blue-grey
		pillar.AddChild(polygon);

		// Create outline
		var outline = new Line2D();
		foreach (var point in points)
		{
			outline.AddPoint(point);
		}
		outline.AddPoint(points[0]);  // Close the loop
		outline.DefaultColor = BOUNDARY_COLOR;
		outline.Width = BOUNDARY_WIDTH;
		pillar.AddChild(outline);

		_obstacles.AddChild(pillar);

		// Register with grid collision system
		if (GridCollisionManager.Instance != null)
		{
			GridCollisionManager.Instance.SetCircle(position, radius, CellOccupant.Obstacle);
		}
	}

	/// <summary>
	/// Spawn a wall obstacle (visual + grid collision)
	/// </summary>
	private void SpawnWall(Vector2 position, float length, float rotation)
	{
		// Create visual container
		var wall = new Node2D();
		wall.Position = position;
		wall.Rotation = rotation;

		// Create visual outline
		var outline = new Line2D();
		float halfLength = length / 2;
		outline.AddPoint(new Vector2(-halfLength, -5));
		outline.AddPoint(new Vector2(halfLength, -5));
		outline.AddPoint(new Vector2(halfLength, 5));
		outline.AddPoint(new Vector2(-halfLength, 5));
		outline.AddPoint(new Vector2(-halfLength, -5));  // Close the loop
		outline.DefaultColor = BOUNDARY_COLOR;
		outline.Width = BOUNDARY_WIDTH;
		wall.AddChild(outline);

		_obstacles.AddChild(wall);

		// Calculate rotated wall endpoints for grid collision
		Vector2 direction = new Vector2(Mathf.Cos(rotation), Mathf.Sin(rotation));
		Vector2 start = position - direction * halfLength;
		Vector2 end = position + direction * halfLength;

		// Register with grid collision system
		if (GridCollisionManager.Instance != null)
		{
			GridCollisionManager.Instance.SetLine(start, end, CellOccupant.Obstacle);
		}
	}

	/// <summary>
	/// Generate a complete arena based on room number and difficulty
	/// </summary>
	/// <param name="roomNumber">Current room number (1-20)</param>
	/// <param name="sizeScale">Scale factor for arena size (default 1.0)</param>
	public void GenerateArena(int roomNumber, float sizeScale = 1.0f)
	{
		GD.Print($"\n[Arena] ═══ GENERATING ARENA FOR ROOM {roomNumber} ═══");

		// Select template based on room number
		ArenaTemplate selectedTemplate = SelectTemplate(roomNumber);

		// Generate arena with selected template
		GenerateArenaFromTemplate(selectedTemplate, sizeScale);
	}

	/// <summary>
	/// Generate arena using a specific template by index (for testing/debugging)
	/// </summary>
	/// <param name="templateIndex">Template index (0-4): 0=The Box, 1=Four Pillars, 2=The Cross, 3=The Ring, 4=Scattered</param>
	/// <param name="sizeScale">Scale factor for arena size (default 1.0)</param>
	public void GenerateArenaByTemplate(int templateIndex, float sizeScale = 1.0f)
	{
		// Validate template index
		if (templateIndex < 0 || templateIndex >= _templates.Count)
		{
			GD.PrintErr($"[Arena] ERROR: Invalid template index {templateIndex}. Must be 0-{_templates.Count - 1}");
			return;
		}

		GD.Print($"\n[Arena] ═══ GENERATING SPECIFIC ARENA (INDEX {templateIndex}) ═══");

		// Get the specific template
		ArenaTemplate selectedTemplate = _templates[templateIndex];

		// Generate arena with selected template
		GenerateArenaFromTemplate(selectedTemplate, sizeScale);
	}

	/// <summary>
	/// Core arena generation logic used by both GenerateArena and GenerateArenaByTemplate
	/// </summary>
	/// <param name="selectedTemplate">The arena template to generate</param>
	/// <param name="sizeScale">Scale factor for arena size</param>
	private void GenerateArenaFromTemplate(ArenaTemplate selectedTemplate, float sizeScale)
	{
		// Clear existing obstacles
		ClearObstacles();

		// Store current template
		_currentTemplate = selectedTemplate;

		// Apply random rotation (0°, 90°, 180°, 270°) - SKIP for collision test arena
		float arenaRotation = 0f;
		if (selectedTemplate.Name != "Collision Test Arena")
		{
			float[] possibleRotations = { 0, Mathf.Pi / 2, Mathf.Pi, 3 * Mathf.Pi / 2 };
			arenaRotation = possibleRotations[_random.Next(possibleRotations.Length)];
		}

		// Apply size scale
		_currentScale = sizeScale;
		Vector2 scaledSize = _arenaSize * sizeScale;

		// Generate boundaries
		GenerateBoundaries(scaledSize);

		// Spawn obstacles with rotation
		foreach (var obstacleDef in selectedTemplate.Obstacles)
		{
			// Create a rotated copy of the obstacle
			var rotatedObstacle = new ObstacleDefinition(
				obstacleDef.ObstacleType,
				RotatePoint(obstacleDef.Position, arenaRotation),
				obstacleDef.Size,
				obstacleDef.Rotation + arenaRotation
			);
			SpawnObstacle(rotatedObstacle);
		}

		GD.Print($"[Arena] Generated arena: \"{selectedTemplate.Name}\", {selectedTemplate.Obstacles.Count} obstacles");
		GD.Print($"[Arena] Arena size: {scaledSize.X}x{scaledSize.Y}, scale: {sizeScale:F2}");
		GD.Print($"[Arena] Rotation: {arenaRotation * 180 / Mathf.Pi:F0}°");
		GD.Print("[Arena] ═══════════════════════════════════\n");

		// Trigger redraw for grid
		QueueRedraw();
	}

	/// <summary>
	/// Generate completely procedural arena with random rectangular obstacles (for testing/variety)
	/// Guarantees survivability with safe spawn zone and obstacle spacing
	/// </summary>
	/// <param name="seed">Random seed for reproducible generation (0 = random)</param>
	/// <param name="sizeScale">Scale factor for arena size (default 1.0)</param>
	public void GenerateProceduralArena(int seed = 0, float sizeScale = 1.0f)
	{
		// Use seed for reproducible generation, or create new random seed
		Random procRandom = seed != 0 ? new Random(seed) : new Random();

		GD.Print($"\n[Arena] ═══ GENERATING PROCEDURAL ARENA (Seed: {(seed != 0 ? seed.ToString() : "Random")}) ═══");

		// Clear existing obstacles
		ClearObstacles();

		// Apply size scale
		_currentScale = sizeScale;
		Vector2 scaledSize = _arenaSize * sizeScale;

		// Generate boundaries
		GenerateBoundaries(scaledSize);

		// Decide how many obstacles to spawn (using exported settings)
		int obstacleCount = procRandom.Next(MinObstacles, MaxObstacles + 1);
		GD.Print($"[Arena] Generating {obstacleCount} procedural obstacles...");

		// Track placed obstacle positions for spacing validation
		var placedPositions = new List<Vector2>();

		int placedCount = 0;
		int attempts = 0;
		const int MAX_ATTEMPTS = 200;  // Prevent infinite loops

		while (placedCount < obstacleCount && attempts < MAX_ATTEMPTS)
		{
			attempts++;

			// Decide obstacle type (60% walls, 40% pillars)
			bool isWall = procRandom.NextDouble() < 0.6;

			// Generate random position within safe zone
			float x = (float)(procRandom.NextDouble() * 2 - 1) * SafeBoundaryMargin;
			float y = (float)(procRandom.NextDouble() * 2 - 1) * SafeBoundaryMargin;
			Vector2 position = new Vector2(x, y);

			// Check if too close to spawn point
			if (position.Length() < SafeZoneRadius)
			{
				continue;  // Too close to spawn, skip
			}

			// Check if too close to other obstacles
			bool tooClose = false;
			foreach (var existingPos in placedPositions)
			{
				if (position.DistanceTo(existingPos) < MinObstacleDistance)
				{
					tooClose = true;
					break;
				}
			}

			if (tooClose)
			{
				continue;  // Too close to another obstacle, skip
			}

			// Valid position! Create the obstacle
			if (isWall)
			{
				// Random wall parameters
				float length = (float)(procRandom.NextDouble() * 180 + 100);  // 100-280 units
				float rotation = (float)(procRandom.NextDouble() * Mathf.Pi * 2);  // Random angle

				SpawnWall(position, length, rotation);
			}
			else
			{
				// Random pillar parameters
				float radius = (float)(procRandom.NextDouble() * 20 + 25);  // 25-45 units

				SpawnPillar(position, radius);
			}

			placedPositions.Add(position);
			placedCount++;
		}

		GD.Print($"[Arena] Successfully placed {placedCount} obstacles after {attempts} attempts");
		GD.Print($"[Arena] Arena size: {scaledSize.X}x{scaledSize.Y}, scale: {sizeScale:F2}");
		GD.Print("[Arena] ═══════════════════════════════════\n");

		// Trigger redraw for grid
		QueueRedraw();
	}

	/// <summary>
	/// Select an arena template based on room difficulty
	/// </summary>
	private ArenaTemplate SelectTemplate(int roomNumber)
	{
		// Rooms 1-5: Easy templates (The Box or Four Pillars)
		if (roomNumber <= 5)
		{
			var easyTemplates = _templates.Where(t => t.DifficultyRating <= 0.3f).ToList();
			return easyTemplates[_random.Next(easyTemplates.Count)];
		}
		// Rooms 6-10: Medium templates (Four Pillars or The Cross)
		else if (roomNumber <= 10)
		{
			var mediumTemplates = _templates.Where(t => t.DifficultyRating >= 0.3f && t.DifficultyRating <= 0.4f).ToList();
			return mediumTemplates[_random.Next(mediumTemplates.Count)];
		}
		// Rooms 11+: Any template, weighted by difficulty
		else
		{
			// Weight templates by difficulty - higher difficulty more likely in later rooms
			float roomDifficultyFactor = Mathf.Clamp((roomNumber - 10) / 10.0f, 0, 1);

			// Simple weighted selection: prefer higher difficulty templates
			var weightedTemplates = new List<ArenaTemplate>();
			foreach (var template in _templates)
			{
				// Add template multiple times based on how close its difficulty matches the room
				int weight = 1 + (int)(template.DifficultyRating * roomDifficultyFactor * 5);
				for (int i = 0; i < weight; i++)
				{
					weightedTemplates.Add(template);
				}
			}

			return weightedTemplates[_random.Next(weightedTemplates.Count)];
		}
	}

	/// <summary>
	/// Rotate a point around the origin
	/// </summary>
	private Vector2 RotatePoint(Vector2 point, float angle)
	{
		float cos = Mathf.Cos(angle);
		float sin = Mathf.Sin(angle);
		return new Vector2(
			point.X * cos - point.Y * sin,
			point.X * sin + point.Y * cos
		);
	}

	/// <summary>
	/// Generic helper to clear all children from a parent node
	/// </summary>
	private void ClearAllChildren(Node parent)
	{
		foreach (Node child in parent.GetChildren())
		{
			child.QueueFree();
		}
	}

	/// <summary>
	/// Clear all obstacles from the arena
	/// </summary>
	private void ClearObstacles()
	{
		ClearAllChildren(_obstacles);
	}

	/// <summary>
	/// Get the usable bounds of the arena for player clamping
	/// </summary>
	/// <returns>Rectangle representing the arena bounds</returns>
	public Rect2 GetArenaBounds()
	{
		Vector2 scaledSize = _arenaSize * _currentScale;
		float halfWidth = scaledSize.X / 2;
		float halfHeight = scaledSize.Y / 2;

		return new Rect2(
			-halfWidth,
			-halfHeight,
			scaledSize.X,
			scaledSize.Y
		);
	}

	/// <summary>
	/// Draw the arena grid (optional visual element)
	/// </summary>
	public override void _Draw()
	{
		// Draw subtle grid lines
		Vector2 scaledSize = _arenaSize * _currentScale;
		float halfWidth = scaledSize.X / 2;
		float halfHeight = scaledSize.Y / 2;

		// Vertical lines
		for (float x = -halfWidth; x <= halfWidth; x += GridSpacing)
		{
			DrawLine(
				new Vector2(x, -halfHeight),
				new Vector2(x, halfHeight),
				GRID_COLOR,
				1.0f
			);
		}

		// Horizontal lines
		for (float y = -halfHeight; y <= halfHeight; y += GridSpacing)
		{
			DrawLine(
				new Vector2(-halfWidth, y),
				new Vector2(halfWidth, y),
				GRID_COLOR,
				1.0f
			);
		}
	}
}
