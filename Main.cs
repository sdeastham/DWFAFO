using System;
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
	private Vector2 GlobalEarthUpperLeft, GlobalEarthLowerRight;
	private Vector2 _flightOrigin, _flightDestination;
	private bool _originSelected;
	private Vector2 _originLonLat;
	private const int FlightPathPointCount = 150;

	private bool _idle;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// Get the location of 90N/180E and 90S/180W in global coordinates
		TextureRect earth = GetNode<TextureRect>("EarthMap");
		GlobalEarthUpperLeft = earth.GetGlobalTransform() * (new Vector2(0,0));
		GlobalEarthLowerRight = earth.GetGlobalTransform() * earth.Size;
		
		// Start with no origin selcted
		_originSelected = false;
		
		_simulationSpeed = 1.0; // Simulation hours per wall-clock second
		_idle = true;
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
		// Set the camera position etc.
		//ShowRegion(-180.0f, 0.0f, 0.0f, 90.0f);
		ShowRegion(0.0f, 180.0f, -90.0f, 0.0f);
		// Standard global view
		ShowRegion(-180f, 180f, -90f, 90f);

		const bool startRandom = true;
		if (startRandom)
		{
			RandomNumberGenerator random = new RandomNumberGenerator();
			random.Randomize();
			// Use a zoom of 2 and pick somewhere at random
			float lonMid = random.Randf() * 180.0f - 90.0f;
			float latMid = random.Randf() * 90.0f - 45.0f;
			ShowRegion(lonMid - 90.0f, lonMid + 90.0f, latMid - 45.0f, latMid + 45.0f);
		}
		Line2D flightPath = GetNode<Line2D>("FlightPath");
		flightPath.Hide();
	}

	public void ShowRegion(float lonWest, float lonEast, float latSouth, float latNorth, bool allowBlank=false)
	{
		// Moves and zooms the camera to accommodate the target area
		// If allowBlank is true, then the priority is to include as much of the target region as possible
		// If false, then the priority is to ensure that the entire camera view is within the target region
		float lonMid = (lonWest + lonEast) / 2.0f;
		float latMid = (latSouth + latNorth) / 2.0f;
		float lonSpan = lonEast - lonWest;
		float latSpan = latNorth - latSouth;
		(float xSpan, float ySpan) = GetViewport().GetVisibleRect().Size;
		float xyRatio = xSpan / ySpan;
		float lonlatRatio = lonSpan / latSpan;
		// If target region is wider than the view region (per unit of vertical space) then using the
		// vertical extent to limit the camera view will result in points being cropped out, and vice
		// versa. xor expresses this nicely.
		bool fitVertical = (lonlatRatio > xyRatio) ^ (allowBlank);
		// Recall that 2 means we are zoomed in, 0.5 means we are zoomed out
		float zoomFactor;
		if (fitVertical)
		{
			// Set zoom based on vertical extent
			zoomFactor = 180.0f/latSpan;
		}
		else
		{
			// Set zoom based on horizontal extent
			zoomFactor = 360.0f/lonSpan;
		}
		Camera2D camera = GetNode<Camera2D>("Camera");
		camera.Position = LonLatToXYVector(lonMid, latMid);
		camera.Zoom = new Vector2(zoomFactor, zoomFactor);
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
		(double[] lonLims, double[] latLims) = mainSimulator.GetLonLatBounds();
		ShowRegion((float)lonLims[0], (float)lonLims[^1], (float)latLims[0], (float)latLims[^1]);
		//usrMsg.Hide();
		hud.GetNode<ColorRect>("Blackout").Hide();
		// Reduce the simulation speed
		_simulationSpeed = 60.0/3600.0; // Simulation hours per wall-clock second
		// Show and update the speed slider (display is in minutes/second)
		var slider = hud.GetNode<VSlider>("SpeedSlider");
		slider.Value = _simulationSpeed * 60.0;
		slider.Show();
	}

	public void UpdateSimulationSpeed(float newSpeed)
	{
		// Simulation speed is simulated hours per real-time second
		// newSpeed is simulated minutes per real-time second
		_simulationSpeed = newSpeed / 60.0;
		// We don't want the trails to get longer just because the simulation speed is higher
		// We want to drop a point once every simulated minute, and we want the dots to remain for an hour
		int targetNumber = 60;
		double newLifetime = 60.0 / newSpeed; // simulation minutes divided by simulation minutes per real-time second
		double newFrequency = (double)targetNumber / newLifetime;
		foreach (AirMass airMass in _pointDict.Values)
		{
			airMass.UpdateLifetime(newLifetime, newFrequency);
		}
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
		_idle = false;
		_pointDict.Clear();
	}

	public void StartIdleSimulation()
	{
		// Create the simulator
		_idle = true;
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
		if (!_idle)
		{
			Hud? hud = GetNode<Hud>("HUD");
			Label? usrMsg = hud.GetNode<Label>("UserMessage");
			usrMsg.Text = $"{_simulator.GetCurrentTime()}";
		}
		
		foreach (Dot point in newPoints)
		{
			// Is this an existing air mass?
			if (_pointDict.TryGetValue(point.UniqueIdentifier, out AirMass? value))
			{
				AirMass airMass = value;
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
		
		//double framerate = Engine.GetFramesPerSecond();  
		//GD.Print($"Frame rate: {framerate,10:f2}; point count: {_pointDict.Count}");
		
		if (_originSelected)
		{
			DrawFlightPath();
		}
	}

	private void DrawFlightPath()
	{
		Camera2D camera = GetNode<Camera2D>("Camera");
		// Update scale of origin marker
		// Draw line from origin marker to mouse
		Vector2 newLoc = camera.GetGlobalMousePosition();
		bool outOfWindow = newLoc.X < 0 || newLoc.Y < 0 || newLoc.X > GlobalEarthLowerRight[0] ||
		                   newLoc.Y > GlobalEarthLowerRight[1];
		if (outOfWindow) { return; }
		(float lonMouse, float latMouse) = XYToLonLat(newLoc.X, newLoc.Y);
		Line2D flightPath = GetNode<Line2D>("FlightPath");
		(double[] lons, double[] lats, _) = AtmosTools.Geodesy.GreatCircleWaypointsByCount(_originLonLat.X, _originLonLat.Y, 
			lonMouse, latMouse, FlightPathPointCount);
		for (int i = 1; i < (FlightPathPointCount-1); i++)
		{
			Vector2 tempLoc = LonLatToXYVector((float)lons[i], (float)lats[i]);
			if (Math.Abs(lons[i] - lons[i - 1]) > 50.0)
			{
				tempLoc = new Vector2(Single.NaN, Single.NaN);
			}
			flightPath.SetPointPosition(i, flightPath.GetGlobalTransform().AffineInverse() * tempLoc);
		}
		flightPath.SetPointPosition(FlightPathPointCount-1,flightPath.GetGlobalTransform().AffineInverse() * newLoc);
	}
	
	public void CreateAirMass(float longitude, float latitude, ulong uid)
	{
		AirMass dot = AirMassScene.Instantiate<AirMass>();
		dot.SetProperties(longitude,latitude);
		dot.SetUniqueIdentifier(uid);
		//dot.UpdateColor(Color.Color8(255,0,0,127));
		dot.UpdateColor(Color.Color8(255,255,255,127));
		dot.UpdateLifetime(2.0,10.0);
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
		// Convert from global coordinates to longitude and latitude
		Vector2 earthSize = GlobalEarthLowerRight - GlobalEarthUpperLeft;
		return (360.0f * (x/earthSize.X) - 180.0f,-1.0f * (180.0f * (y/earthSize.Y) - 90.0f));
	}

	public Vector2 LonLatToXYVector(float longitude, float latitude)
	{
		(float x, float y) = LonLatToXY(longitude, latitude);
		return new Vector2(x, y);
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
		Vector2 earthSize = GlobalEarthLowerRight - GlobalEarthUpperLeft;
		float xScaling = earthSize.X / 360.0f;
		float yScaling = earthSize.Y / 180.0f;
		// Also need to reflect latitude
		return ((lonMod+180)*xScaling,(180.0f - (latitude+90.0f))*yScaling);
	}
	
	public override void _Input(InputEvent @event)
	{
		// Place a new air mass wherever we click and hold control
		if (@event is InputEventMouseButton eventMouseButton && @event.IsPressed()
			&& eventMouseButton.ButtonIndex == MouseButton.Left && Input.IsActionPressed("AllowPoint"))
		{
			// mouseLoc is in the coordinates of... no idea, actually
			//Vector2 mouseLoc = eventMouseButton.Position;
			// Cheating and just asking the camera where the mouse is
			Vector2 newLoc = GetNode<Camera2D>("Camera").GetGlobalMousePosition();
			// For an idle simulation, this is straightforward
			/*
			if (_idle)
			{
				(float lon, float lat) = XYToLonLat(newLoc.X, newLoc.Y);
				((IdleSimulator)_simulator).CreateInteractivePoint(lon, lat);
				return;
			}
			*/
			// For a full simulation, was this the first or second click?
			if (!_originSelected)
			{
				SetFlightOrigin(newLoc);
			}
			else
			{
				SetFlightDestination(newLoc);
				if (_idle)
				{
					(float lon, float lat) = XYToLonLat(newLoc.X, newLoc.Y);
					//((IdleSimulator)_simulator).CreateInteractivePoint(_originLonLat.X, _originLonLat.Y);
					//((IdleSimulator)_simulator).CreateInteractivePoint(lon, lat);
					((IdleSimulator)_simulator).FlyFlight(_originLonLat.X, _originLonLat.Y, lon, lat);
				}
			}
		}
	}
	
	private void SetFlightOrigin(Vector2 xyLoc)
	{
		if (_originSelected) { return; }
		_flightOrigin = xyLoc;
		_originSelected = true;
		Node2D originMarker = GetNode<Node2D>("OriginMarker");
		originMarker.Position = xyLoc;
		(_originLonLat.X, _originLonLat.Y) = XYToLonLat(xyLoc.X, xyLoc.Y);
		// Before showing the particle manager, reset it - otherwise get some very weird effects
		GpuParticles2D particles;
		particles = originMarker.GetNode<GpuParticles2D>("OriginParticlesA");
		particles.Restart();
		particles = originMarker.GetNode<GpuParticles2D>("OriginParticlesB");
		particles.Restart();
		particles = originMarker.GetNode<GpuParticles2D>("OriginParticlesC");
		particles.Restart();
		// Show the node to which they are connected
		originMarker.Show();
		Line2D flightPath = GetNode<Line2D>("FlightPath");
		flightPath.ClearPoints();
		for (int i = 0; i < FlightPathPointCount; i++)
		{
			flightPath.AddPoint(flightPath.GetGlobalTransform().AffineInverse() * originMarker.Position);
		}
		flightPath.Show();
	}

	private void SetFlightDestination(Vector2 xyLoc)
	{
		if (!_originSelected) { return; }
		_flightDestination = xyLoc;
		_originSelected = false;
		// Run flight!
		Node2D originMarker = GetNode<Node2D>("OriginMarker");
		originMarker.Hide();
		Line2D flightPath = GetNode<Line2D>("FlightPath");
		flightPath.Hide();
		flightPath.ClearPoints();
	}
}
