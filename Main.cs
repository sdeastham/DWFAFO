using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Main : Node
{
	[Export]
	public PackedScene AirMassScene {get;set;}

	private Simulator DotSimulator;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{	
		// Create the simulator
		DotSimulator = new Simulator();
		
		// Start with some non-zero number of points
		const int nLocations = 70;
		DotSimulator.GenerateRandomPoints(nLocations);
		IEnumerable<Point> points = DotSimulator.GetPointData();
		foreach (Point point in points)
		{
			CreateAirMass(point.X,point.Y,point.UniqueIdentifier);
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		// Advance the external simulation
		const double simulationHoursPerSecond=1.0;
		DotSimulator.AdvanceSimulation(delta * simulationHoursPerSecond);
		
		// Call an external function to get a list of the current node locations
		Point[] newPoints = DotSimulator.GetPointData().ToArray();
	}
	
	public void CreateAirMass(float longitude, float latitude, ulong uid)
	{
		(float x,float y) = TransformCoords(longitude,latitude);
		CreateAirMassXY(x,y,uid);
	}
	
	public void CreateAirMassXY(float x, float y, ulong uid)
	{
		AirMass dot = AirMassScene.Instantiate<AirMass>();
		dot.SetProperties(x,y);
		dot.SetUniqueIdentifier(0);
		AddChild(dot);
	}
	
	public (float,float) TransformCoords(float longitude, float latitude)
	{
		// Longitude can wrap around - get it into the range of -180 : 180
		float lonMod = longitude;
		while (lonMod < -180.0)
		{
			lonMod += 360.0f;
		}
		while (lonMod >= 180.0f)
		{
			lonMod -= 360.0f;
		}
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		float xScaling = viewportSize.X / 360.0f;
		float yScaling = viewportSize.Y / 180.0f;
		// Also need to reflect latitude
		return ((lonMod+180)*xScaling,(180.0f - (latitude+90.0f))*yScaling);
	}
	
	public override void _Input(InputEvent @event)
	{
		// Place a new air mass wherever we click
		if (@event is InputEventMouseButton eventMouseButton && @event.IsPressed()
			&& eventMouseButton.ButtonIndex == MouseButton.Left)
		{
			Vector2 newLoc = eventMouseButton.Position;
			float x = newLoc.X;
			float y = newLoc.Y;
			CreateAirMassXY(x,y,0);
		}
	}
}

public class Simulator
{
	private LinkedList<Point> PointList;
	RandomNumberGenerator Random;

	public Simulator()
	{
		Random = new RandomNumberGenerator();
		Random.Randomize();
		PointList = new LinkedList<Point>();
	}
	
	public void AdvanceSimulation(double timeStep)
	{
		// We have a rate at which points are created and a rate at which they are lost
		// Interface to external code to grab the updated list of point locations and properties
		List<Point> newPoints = new List<Point>();
		// Take the existing points and move them East at 50 kph
		const float uSpeed = 50.0f * 1000.0f / 3600.0f; // Change from kph to m/s
		foreach (Point point in PointList)
		{
			float lon = point.X;
			float lat = point.Y;
			// Don't change latitude, but do increase longitude
			float localCircumference = (float)(6378.0e3 * Math.PI * 2.0 * Math.Cos(Math.PI * lat / 180.0));
			float newLon = lon + uSpeed * 360.0f / localCircumference;
		}
		return;
	}

	public IEnumerable<Point> GetPointData()
	{
		return PointList;
	}
	
	public void GenerateRandomPoints(int nLocations)
	{
		for (int i=0; i < nLocations; i++)
		{
			float lon = Random.Randf() * 360.0f - 180.0f;
			float lat = Random.Randf() * 180.0f - 90.0f;
			PointList.AddLast(new Point(lon,lat,0));
		}
	}
}

public class Point
{
	// A simple class to hold the minimum information needed to identify
	// a point
	public Vector2 Location;
	public ulong UniqueIdentifier {get; private set;}
	// X and Y are lon and lat, NOT in window coordinates
	public float X => Location.X;
	public float Y => Location.Y;
		
	public Point(float x, float y, ulong uniqueIdentifier)
	{
		Location = new Vector2(x,y);
		UniqueIdentifier = uniqueIdentifier;	
	}
}
