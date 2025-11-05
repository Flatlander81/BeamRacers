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

	// Constants
	private const int PILLAR_VERTICES = 16;
	private static readonly Color BOUNDARY_COLOR = new Color(0, 0.5f, 1, 1);  // Blue
	private const float BOUNDARY_WIDTH = 2.0f;
	private static readonly Color GRID_COLOR = new Color(0, 0.3f, 0.5f, 0.15f);  // Subtle blue
	private const float GRID_SPACING = 50.0f;
	private const int COLLISION_LAYER = 3;

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
		float wallThickness = 10.0f;

		// Define wall configurations (name, position, width, height, isHorizontal)
		var wallConfigs = new[]
		{
			("TopWall", new Vector2(0, -halfHeight), arenaSize.X, wallThickness, true),
			("BottomWall", new Vector2(0, halfHeight), arenaSize.X, wallThickness, true),
			("LeftWall", new Vector2(-halfWidth, 0), wallThickness, arenaSize.Y, false),
			("RightWall", new Vector2(halfWidth, 0), wallThickness, arenaSize.Y, false)
		};

		// Create all walls using loop
		foreach (var (name, position, width, height, isHorizontal) in wallConfigs)
		{
			var wall = CreateBoundaryWall(position, width, height, isHorizontal);
			wall.Name = name;
			_boundaries.AddChild(wall);
		}

		GD.Print($"[Arena] ✓ Generated boundaries ({arenaSize.X}x{arenaSize.Y}) with collision");
	}

	/// <summary>
	/// Creates a single boundary wall with collision
	/// </summary>
	private StaticBody2D CreateBoundaryWall(Vector2 position, float width, float height, bool horizontal)
	{
		var wall = new StaticBody2D();
		wall.Position = position;
		wall.CollisionLayer = COLLISION_LAYER;
		wall.CollisionMask = 0;

		// Create collision shape
		var collisionShape = new CollisionShape2D();
		var rectShape = new RectangleShape2D();
		rectShape.Size = new Vector2(width, height);
		collisionShape.Shape = rectShape;
		wall.AddChild(collisionShape);

		// Create visual outline
		var outline = new Line2D();
		float halfWidth = width / 2;
		float halfHeight = height / 2;

		outline.AddPoint(new Vector2(-halfWidth, -halfHeight));
		outline.AddPoint(new Vector2(halfWidth, -halfHeight));
		outline.AddPoint(new Vector2(halfWidth, halfHeight));
		outline.AddPoint(new Vector2(-halfWidth, halfHeight));
		outline.AddPoint(new Vector2(-halfWidth, -halfHeight)); // Close the loop
		outline.DefaultColor = BOUNDARY_COLOR;
		outline.Width = BOUNDARY_WIDTH;
		wall.AddChild(outline);

		return wall;
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
	/// Spawn a pillar obstacle
	/// </summary>
	private void SpawnPillar(Vector2 position, float radius)
	{
		var pillar = new StaticBody2D();
		pillar.Position = position;
		pillar.CollisionLayer = COLLISION_LAYER;
		pillar.CollisionMask = 0;

		// Create collision shape
		var collisionShape = new CollisionShape2D();
		var circleShape = new CircleShape2D();
		circleShape.Radius = radius;
		collisionShape.Shape = circleShape;
		pillar.AddChild(collisionShape);

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
	}

	/// <summary>
	/// Spawn a wall obstacle
	/// </summary>
	private void SpawnWall(Vector2 position, float length, float rotation)
	{
		var wall = new StaticBody2D();
		wall.Position = position;
		wall.Rotation = rotation;
		wall.CollisionLayer = COLLISION_LAYER;
		wall.CollisionMask = 0;

		// Create collision shape (rectangular)
		var collisionShape = new CollisionShape2D();
		var rectShape = new RectangleShape2D();
		rectShape.Size = new Vector2(length, 10);  // 10 units thick
		collisionShape.Shape = rectShape;
		wall.AddChild(collisionShape);

		// Create visual outline (just the rectangle outline)
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

		// Apply random rotation (0°, 90°, 180°, 270°)
		float[] possibleRotations = { 0, Mathf.Pi / 2, Mathf.Pi, 3 * Mathf.Pi / 2 };
		float arenaRotation = possibleRotations[_random.Next(possibleRotations.Length)];

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

		// Procedural generation parameters
		const float SAFE_ZONE_RADIUS = 200f;  // Clear area around spawn point (0,0)
		const float MIN_OBSTACLE_DISTANCE = 120f;  // Minimum distance between obstacles
		const float SAFE_BOUNDARY_MARGIN = 380f;  // Stay within ±380 to survive rotation
		const int MIN_OBSTACLES = 8;
		const int MAX_OBSTACLES = 15;

		// Decide how many obstacles to spawn
		int obstacleCount = procRandom.Next(MIN_OBSTACLES, MAX_OBSTACLES + 1);
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
			float x = (float)(procRandom.NextDouble() * 2 - 1) * SAFE_BOUNDARY_MARGIN;
			float y = (float)(procRandom.NextDouble() * 2 - 1) * SAFE_BOUNDARY_MARGIN;
			Vector2 position = new Vector2(x, y);

			// Check if too close to spawn point
			if (position.Length() < SAFE_ZONE_RADIUS)
			{
				continue;  // Too close to spawn, skip
			}

			// Check if too close to other obstacles
			bool tooClose = false;
			foreach (var existingPos in placedPositions)
			{
				if (position.DistanceTo(existingPos) < MIN_OBSTACLE_DISTANCE)
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
		for (float x = -halfWidth; x <= halfWidth; x += GRID_SPACING)
		{
			DrawLine(
				new Vector2(x, -halfHeight),
				new Vector2(x, halfHeight),
				GRID_COLOR,
				1.0f
			);
		}

		// Horizontal lines
		for (float y = -halfHeight; y <= halfHeight; y += GRID_SPACING)
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
