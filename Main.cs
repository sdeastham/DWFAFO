using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DroxtalWolf;
using Environment = System.Environment;


namespace GoDots;

public partial class Main : Node
{
	[Export]
	public PackedScene AirMassScene {get;set;}
	private ISimulator _simulator;
	private double _simulationSpeed;
	private Dictionary<ulong, AirMass> _pointDict;

	private bool Idle;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_simulationSpeed = 1.0; // Simulation hours per wall-clock second
		Idle = true;
		_pointDict = [];
		StartIdleSimulation();

		// In case this is not set by the user..
		//TODO: Let the user set this directly from config or the opening menu
		string? ncPath = Environment.GetEnvironmentVariable("LIBNETCDFPATH");
		if (ncPath == null)
		{
			ncPath = "C:/Program Files/netCDF 4.9.2/lib/netcdf.lib";
			Environment.SetEnvironmentVariable("LIBNETCDFPATH", ncPath);
		}
	}

	public async void StartSimulation(string pathToConfig)
	{
		Hud? hud = GetNode<Hud>("HUD");
		Label? usrMsg = hud.GetNode<Label>("UserMessage");
		usrMsg.Text = "Loading...";
		usrMsg.Show();
		// Put up a semi-transparent veil
		hud.GetNode<ColorRect>("Blackout").Show();
		hud.GetNode<ColorRect>("Blackout").Color = Color.Color8(0,0,0,128);
		
		// Set up the full simulator
		Simulator mainSimulator = new Simulator();
		// Run the configuration/initial loading asynchronously; this ensures that the main
		// thread does not block. Also, the user gets to keep playing with the idle simulator
		await Task.Run(() => mainSimulator.Initialize(pathToConfig));
		// Once complete, kill the idle simulation and remove the veil
		StopIdleSimulation();
		_simulator = mainSimulator;
		//usrMsg.Hide();
		hud.GetNode<ColorRect>("Blackout").Hide();
		// Reduce the simulation speed
		_simulationSpeed = 60.0/3600.0; // Simulation hours per wall-clock second
	}
	
	private void StopIdleSimulation()
	{
		GetTree().CallGroup("AllPoints", Node.MethodName.QueueFree);
		((IdleSimulator)_simulator).ClearList();
		var oldNodes = GetTree().GetNodesInGroup("AllPoints");
		foreach (AirMass airMass in oldNodes)
		{
			airMass.KillNode();
		}
		Idle = false;
		_pointDict.Clear();
	}

	public void StartIdleSimulation()
	{
		// Create the simulator
		Idle = true;
		_simulator = new IdleSimulator();
		
		// Start with some non-zero number of points
		const int nLocations = 700;
		((IdleSimulator)_simulator).GenerateRandomPoints(nLocations);
		IEnumerable<Dot> points = _simulator.GetPointData();
		foreach (Dot point in points)
		{
			CreateAirMass(point.X,point.Y,point.UniqueIdentifier);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		_simulator.Advance(delta * _simulationSpeed * 3600.0);
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
		
		Dot[] newPoints = _simulator.GetPointData().ToArray();
		
		// Let the user know how far we have gotten
		if (!Idle)
		{
			Hud? hud = GetNode<Hud>("HUD");
			Label? usrMsg = hud.GetNode<Label>("UserMessage");
			usrMsg.Text = $"{_simulator.GetCurrentTime()}";
		}

		double framerate = Engine.GetFramesPerSecond();  
		GD.Print($"Frame rate: {framerate,10:f2}; point count: {newPoints.Length}");
		
		foreach (Dot point in newPoints)
		{
			// Is this an existing air mass?
			bool matched = _pointDict.ContainsKey(point.UniqueIdentifier);
			/*
			bool matched = false;
			foreach (AirMass airMass in oldNodes)
			{
				ulong nodeUID = airMass.UniqueIdentifier;
				matched = nodeUID == point.UniqueIdentifier;
				if (!matched) continue;
				airMass.Live = true;
				(float x, float y) = LonLatToXY(point.X, point.Y);
				Vector2 transformedLocation = new Vector2(x, y);
				//airMass.UpdatePosition(transformedLocation);
				break;
			}
			*/
			if (matched)
			{
				AirMass airMass = _pointDict[point.UniqueIdentifier];
				airMass.Live = true;
				(float x, float y) = LonLatToXY(point.X, point.Y);
				Vector2 transformedLocation = new Vector2(x, y);
				airMass.UpdatePosition(transformedLocation);
			}
			else
			{
				CreateAirMass(point.X, point.Y, point.UniqueIdentifier);
			}
		}
		
		foreach (AirMass airMass in oldNodes)
		{
			if (airMass.Live) continue;
			DeleteAirMass(airMass.UniqueIdentifier);
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
		_pointDict[dot.UniqueIdentifier] = dot;
		AddChild(dot);
	}

	private void DeleteAirMass(ulong uid)
	{
		AirMass airMass = _pointDict[uid];
		airMass.KillNode();
		_pointDict.Remove(uid);
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
		if (!Idle) { return; }
		if (@event is InputEventMouseButton eventMouseButton && @event.IsPressed()
			&& eventMouseButton.ButtonIndex == MouseButton.Left)
		{
			Vector2 newLoc = eventMouseButton.Position;
			(float lon, float lat) = XYToLonLat(newLoc.X, newLoc.Y);
			((IdleSimulator)_simulator).CreateInteractivePoint(lon, lat);
		}
	}
}
