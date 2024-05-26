using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Godot.Collections;
using DroxtalWolf;

public partial class Main : Node
{
	[Export]
	public PackedScene AirMassScene {get;set;}

	private Simulator DotSimulator;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		StartIdleAnimation();
	}

	public void StartSimulation(string pathToConfig)
	{
		//ClearSimulation();
		GetTree().CallGroup("AllPoints", Node.MethodName.QueueFree);
		DotSimulator.ClearList();
		GD.Print(pathToConfig);
	}

	private void ClearSimulation()
	{
		var oldNodes = GetTree().GetNodesInGroup("AllPoints");
		foreach (AirMass airMass in oldNodes)
		{
			airMass.KillNode();
		}
	}

	public void StartIdleAnimation()
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
		// Set all the current points "Live" flag to false - this will be used to check if they are still here after
		// the update
		var oldNodes = GetTree().GetNodesInGroup("AllPoints");
		foreach (AirMass node in oldNodes)
		{
			//AirMass airMass = node.GetNode<AirMass>("AirMass");
			node.Live = false;
		}
		// Advance the external simulation
		const double simulationHoursPerSecond=1.0;
		DotSimulator.AdvanceSimulation(delta * simulationHoursPerSecond * 3600.0);
		
		// Call an external function to get a list of the current node locations
		Point[] newPoints = DotSimulator.GetPointData().ToArray();
		foreach (Point point in newPoints)
		{
			// Is this an existing air mass?
			bool matched = false;
			foreach (AirMass airMass in oldNodes)
			{
				ulong nodeUID = airMass.UniqueIdentifier;
				matched = nodeUID == point.UniqueIdentifier;
				if (!matched) continue;
				airMass.Live = true;
				(float x, float y) = LonLatToXY(point.X, point.Y);
				Vector2 transformedLocation = new Vector2(x, y);
				airMass.UpdatePosition(transformedLocation);
				break;
			}
			if (!matched)
			{
				CreateAirMass(point.X, point.Y, point.UniqueIdentifier);
			}
		}

		foreach (AirMass airMass in oldNodes)
		{
			if (airMass.Live) continue;
			airMass.KillNode();
		}
	}
	
	public void CreateAirMass(float longitude, float latitude, ulong uid)
	{
		(float x,float y) = LonLatToXY(longitude,latitude);
		CreateAirMassXY(x,y,uid);
	}
	
	public void CreateAirMassXY(float x, float y, ulong uid)
	{
		AirMass dot = AirMassScene.Instantiate<AirMass>();
		dot.SetProperties(x,y);
		dot.SetUniqueIdentifier(uid);
		AddChild(dot);
	}
	
	public (float, float) XYToLonLat(float x, float y)
	{
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		// Reflect latitude
		return (360.0f * (x/viewportSize.X) - 180.0f,-1.0f * (180.0f * (y/viewportSize.Y) - 90.0f));
	}
	public (float,float) LonLatToXY(float longitude, float latitude)
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
			//float x = newLoc.X;
			//float y = newLoc.Y;
			//CreateAirMassXY(x,y,0);
			(float lon, float lat) = XYToLonLat(newLoc.X, newLoc.Y);
			//GD.Print($"{newLoc.X} -> {lon}, {newLoc.Y} -> {lat}");
			DotSimulator.CreateInteractivePoint(lon, lat);
		}
	}
}

public class Simulator
{
	private readonly LinkedList<Point> _pointList;
	private readonly RandomNumberGenerator _random;
	private ulong _nextUniqueIdentifier;

	public Simulator()
	{
		_random = new RandomNumberGenerator();
		_random.Randomize();
		_nextUniqueIdentifier = 1;
		_pointList = new LinkedList<Point>();
	}

	public void ClearList()
	{
		_pointList.Clear();
	}
	
	public void AdvanceSimulation(double timeStep)
	{
		// Rate of new point creation, in points per hour
		double newPointRate = 1.0 / 3600.0;
		// Rate of point destruction, in points per hour
		double pointLossRate = newPointRate;
		// We have a rate at which points are created and a rate at which they are lost
		// Interface to external code to grab the updated list of point locations and properties
		// Take the existing points and move them East at 200 kph
		const float uSpeed = 200.0f * 1000.0f / 3600.0f; // Change from kph to m/s
		foreach (Point point in _pointList)
		{
			float lon = point.X;
			float lat = point.Y;
			// Don't change latitude, but do increase longitude
			double localCircumference = 6378.0e3 * Math.PI * 2.0 * Math.Cos(Math.PI * lat / 180.0);
			float newLon = (float)(lon + timeStep * uSpeed * 360.0f / localCircumference);
			while (newLon >= 180.0f)
			{
				newLon -= 360.0f;
			}

			while (newLon < -180.0f)
			{
				newLon += 360.0f;
			}
			point.X = newLon;
			point.Age += timeStep;
		}
		// Create new points
		float probability = _random.Randf();
		int nNew = 0;
		double factorMult = 1.0;
		double lambda = timeStep * newPointRate; // Expected number of events in the given interval
		while (nNew < 5 && Math.Pow(lambda,nNew) * Math.Exp(-1.0 * lambda)/factorMult < probability)
		{
			nNew++;
			factorMult *= nNew;
		}
		GenerateRandomPoints(nNew);
		// Cull points by age
		LinkedListNode<Point>? nextNode = null;
		LinkedListNode<Point>? node = _pointList.First;
		while (node != null)
		{
			nextNode = node.Next;
			if (node.Value.Age > node.Value.MaxLifetime)
			{
				_pointList.Remove(node);
			}
			node = nextNode;
		}
	}

	public IEnumerable<Point> GetPointData()
	{
		return _pointList;
	}
	
	public void GenerateRandomPoints(int nLocations)
	{
		for (int i=0; i < nLocations; i++)
		{
			float lon = _random.Randf() * 360.0f - 180.0f;
			float lat = _random.Randf() * 180.0f - 90.0f;
			AddPoint(lon, lat);
		}
	}

	public void CreateInteractivePoint(float lon, float lat)
	{
		AddPoint(lon, lat);
	}
	
	private void AddPoint(float lon, float lat)
	{
		// Lifetime of 1-24 hours for all points
		double maxLifetime = 3600.0 * (24.0 * _random.Randf() + 1.0);
		_pointList.AddLast(new Point(lon,lat,_nextUniqueIdentifier,maxLifetime));
		_nextUniqueIdentifier++;
	}
}

public class Point
{
	// A simple class to hold the minimum information needed to identify
	// a point
	public Vector2 Location;
	public ulong UniqueIdentifier {get; private set;}
	// X and Y are lon and lat, NOT in window coordinates
	public float X
	{
		get => Location.X;
		set => Location.X = value;
	}

	public float Y
	{
		get => Location.Y;
		set => Location.Y = value;
	}

	public double Age;
	public double MaxLifetime;

	public Point(float x, float y, ulong uniqueIdentifier, double maxLifetime)
	{
		Location = new Vector2(x, y);
		UniqueIdentifier = uniqueIdentifier;
		Age = 0.0;
		MaxLifetime = maxLifetime;
	}
}
