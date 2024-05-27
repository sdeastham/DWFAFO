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

	private IdleSimulator _idleSimulator;
	private Simulator _mainSimulator;

	private bool Idle;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		Idle = true;
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

	public async Task StartSimulation(string pathToConfig)
	{
		Hud? hud = GetNode<Hud>("HUD");
		Label? usrMsg = hud.GetNode<Label>("UserMessage");
		usrMsg.Text = "Loading...";
		usrMsg.Show();
		hud.GetNode<ColorRect>("Blackout").Show();
		StopIdleSimulation();
		
		// Set up the full simulator
		_mainSimulator = new Simulator();
		//_mainSimulator.Initialize();
		hud.GetNode<Label>("UserMessage").Hide();
		hud.GetNode<ColorRect>("Blackout").Hide();
	}
	
	private void StopIdleSimulation()
	{
		GetTree().CallGroup("AllPoints", Node.MethodName.QueueFree);
		_idleSimulator.ClearList();
		var oldNodes = GetTree().GetNodesInGroup("AllPoints");
		foreach (AirMass airMass in oldNodes)
		{
			airMass.KillNode();
		}
		Idle = false;
	}

	public void StartIdleSimulation()
	{
		// Create the simulator
		Idle = true;
		_idleSimulator = new IdleSimulator();
		
		// Start with some non-zero number of points
		const int nLocations = 70;
		_idleSimulator.GenerateRandomPoints(nLocations);
		IEnumerable<Dot> points = _idleSimulator.GetPointData();
		foreach (Dot point in points)
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

		if (!Idle) { return; }

		// Advance the external simulation
		const double simulationHoursPerSecond=1.0;
		_idleSimulator.AdvanceSimulation(delta * simulationHoursPerSecond * 3600.0);
		
		// Call an external function to get a list of the current node locations
		Dot[] newPoints = _idleSimulator.GetPointData().ToArray();
		foreach (Dot point in newPoints)
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
			_idleSimulator.CreateInteractivePoint(lon, lat);
		}
	}
}
