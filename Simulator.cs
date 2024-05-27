using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using DroxtalWolf;
using Godot;
using MathNet.Numerics.Random;

namespace GoDots;

public class Simulator
{
	private readonly System.Collections.Generic.Dictionary<string, Stopwatch> Stopwatches;
	private RunOptions _configOptions;
	private double[] _pressureConstantsAp;
	private double[] _pressureConstantsBp;
	private DomainManager _domain;
	private MetManager _meteorology;
	private Random _masterRandomNumberGenerator;
	private readonly List<int> _seedsUsed;
	private readonly List<PointManager> _pointManagers;
	private TimeManager _timeManager;
	private ulong StoredTimePoints;
	
	public Simulator()
	{
		Stopwatches = [];
		// Initialize timing
		foreach (string watchName in (string[])
		         [
			         "Total", "Point seeding", "Point physics", "Point culling", "Met advance",
			         "Derived quantities", "Met interpolate", "Archiving", "File writing"
		         ])
		{
			Stopwatches.Add(watchName, new Stopwatch());
		}
		_seedsUsed = [];
		_pointManagers = [];
		StoredTimePoints = 0;
	}
	
	public async Task Initialize(string configFile)
	{
		// Read the configuration file
        _configOptions = RunOptions.ReadConfig(configFile);

        // Figure out timings
        _timeManager = new TimeManager(_configOptions);
        
        // Set up the meteorology and domain managers
        // Check if the domain manager will need to calculate box heights (expensive)
        // Checks that PointsFlights is not null, has .Active = true, and has .ComplexContrails = true
        bool boxHeightsNeeded = _configOptions.PointsFlights is { Active: true, ComplexContrails: true };
        
        // Are we using MERRA-2 or ERA5 data?
        //TODO: Move AP and BP out of here/MERRA-2 into MetManager, then delete MERRA2
        bool fixedPressures;
        switch (_configOptions.InputOutput.MetSource)
        {
	        case "MERRA-2":
		        _pressureConstantsAp = MERRA2.AP;
		        _pressureConstantsBp = MERRA2.BP;
		        fixedPressures = false;
		        break;
	        case "ERA5":
	        {
		        _pressureConstantsAp = [ 70.0e2, 100.0e2, 125.0e2, 150.0e2, 175.0e2,
			        200.0e2, 225.0e2, 250.0e2, 300.0e2, 350.0e2,
			        400.0e2, 450.0e2, 500.0e2, 550.0e2, 600.0e2,
			        650.0e2, 700.0e2, 750.0e2, 775.0e2, 800.0e2,
			        825.0e2, 850.0e2, 875.0e2, 900.0e2, 925.0e2,
			        950.0e2, 975.0e2,1000.0e2];
		        Array.Reverse(_pressureConstantsAp); // All data will be flipped internally
		        _pressureConstantsBp = new double[_pressureConstantsAp.Length];
		        for (int i = 0; i < _pressureConstantsAp.Length; i++)
		        {
			        _pressureConstantsBp[i] = 0.0;
		        }
		        fixedPressures = true;
		        break;
	        }
	        default:
		        throw new ArgumentException($"Meteorology data source {_configOptions.InputOutput.MetSource} not recognized.");
        }
        
        // Set up the meteorology and domain
        double[] lonLimits = _configOptions.Domain.LonLimits;
        double[] latLimits = _configOptions.Domain.LatLimits;
        double[] pLimits   = [_configOptions.Domain.PressureBase * 100.0,
	        _configOptions.Domain.PressureCeiling * 100.0];
        _meteorology = new MetManager(_configOptions.InputOutput.MetDirectory, lonLimits, latLimits, 
	        _configOptions.Timing.StartDate,_configOptions.InputOutput.SerialMetData, Stopwatches, _configOptions.InputOutput.MetSource);
        (double[] lonEdge, double[] latEdge) = _meteorology.GetXYMesh();
        _domain = new DomainManager(lonEdge, latEdge, pLimits, _pressureConstantsAp,
	        _pressureConstantsBp, _meteorology, Stopwatches, boxHeightsNeeded, fixedPressures);

        // Use a master RNG to generate seeds predictably.  A specific seed can be requested, otherwise chosen randomly
        _masterRandomNumberGenerator = _configOptions.Seed != null ? new SystemRandomSource((int)_configOptions.Seed) : SystemRandomSource.Default;

        SetupPointManagers();
	}
	
	public void Advance(double timePerFrame)
	{
		//TODO: Find a robust way to have the simulation advance at a fixed speed regardless of frame rate
		// This won't necessarily be smooth because the priority is to make the simulation repeatable rather than
		// visually appealing. As such, we will have a cap on frame rate, and if we achieve it - great. If not, the
		// simulation will just proceed as fast as the computer will allow.
		int nSteps = (int)Math.Round(timePerFrame / _timeManager.dt);
		for (int iter = 0; iter < nSteps; iter++)
		{
			if (_configOptions.TimeDependentMeteorology)
			{
				_meteorology.AdvanceToTime(_timeManager.CurrentDate);
				// Calculate derived quantities
				_domain.UpdateMeteorology();
			}

			foreach (PointManager pointManager in _pointManagers)
			{
				// Seed new points
				Stopwatches["Point seeding"].Start();
				pointManager.Seed(_timeManager.dt);
				Stopwatches["Point seeding"].Stop();
                    
				// Do the actual work
				Stopwatches["Point physics"].Start();
				pointManager.Advance(_timeManager.dt);
				Stopwatches["Point physics"].Stop();

				// TODO: Allow for this to not happen every time step
				Stopwatches["Point culling"].Start();
				pointManager.Cull();
				Stopwatches["Point culling"].Stop();
			}
			_timeManager.Advance();
		}
	}
	
	public IEnumerable<Dot> GetPointData()
	{
		List<Dot> pointList = [];
		foreach (PointManager pm in _pointManagers)
		{
			foreach (IAdvected advPoint in pm.ActivePoints)
			{
				(double x, double y, double p) = advPoint.GetLocation();
				ulong uid = advPoint.GetUID();
				Dot point = new Dot((float)x, (float)y, uid, 1.0);
				pointList.Add(point);
			}
		}
		return pointList;
	}

	public void SetupPointManagers()
	{
		// Dense point managers need an RNG for random point seeding
        // This approach is designed to avoid two failure modes:
        // * The relationship between successive managers being consistent (avoided by using a master RNG)
        // * Seeds being reused (avoided by generating until you hit a new seed)
        // The generation-until-new-seed is in theory slow but that would only matter if we were generating
        // a large number (>>>10) of dense point managers, which is not expected to be the case
        if (_configOptions.PointsDense.Active)
        {
            Random pointMgrRandomNumberGenerator = DroxtalWolf.Simulator.GetNextRandomNumberGenerator(_masterRandomNumberGenerator, _seedsUsed);

            // The point manager holds all the actual point data and controls velocity calculations (in deg/s)
            PointManager pointManager = new PointManagerDense(_domain, _configOptions, _configOptions.PointsDense, pointMgrRandomNumberGenerator);

            // Scatter N points randomly over the domain
            (double[] xInitial, double[] yInitial, double[] pInitial) =
                _domain.MapRandomToXYP(_configOptions.PointsDense.Initial, pointMgrRandomNumberGenerator);
            pointManager.CreatePointSet(xInitial, yInitial, pInitial);

            // Add to the list of _all_ point managers
            _pointManagers.Add(pointManager);
        }

        // Now add plume point managers - contrail point managers, exhaust point managers...
        // Current proposed approach will be to do this via logical connections (i.e. one manager handles
        // all contrails) rather than e.g. one manager per flight
        // The point manager holds all the actual point data and controls velocity calculations (in deg/s)
        if (_configOptions.PointsFlights.Active)
        {
            Random pointMgrRandomNumberGenerator = DroxtalWolf.Simulator.GetNextRandomNumberGenerator(_masterRandomNumberGenerator, _seedsUsed);
            PointManagerFlight pointManager = new PointManagerFlight(_domain, _configOptions, _configOptions.PointsFlights, pointMgrRandomNumberGenerator);

            if (_configOptions.PointsFlights.ScheduleFilename != null)
            {
                Debug.Assert(_configOptions.PointsFlights.AirportsFilename != null,
                    "No airport file provided");
                string scheduleFileName = Path.Join(_configOptions.InputOutput.InputDirectory,
                    _configOptions.PointsFlights.ScheduleFilename);
                string airportFileName = Path.Join(_configOptions.InputOutput.InputDirectory,
                    _configOptions.PointsFlights.AirportsFilename);
                pointManager.ReadScheduleFile(scheduleFileName, airportFileName, _configOptions.Timing.StartDate, 
	                _configOptions.Timing.EndDate);
            }
                
            if (_configOptions.PointsFlights.SegmentsFilename != null)
            {
                pointManager.ReadSegmentsFile(_configOptions.PointsFlights.SegmentsFilename);
            }
                
            // Add to the list of _all_ point managers
            _pointManagers.Add(pointManager);
        }
        
        if (_pointManagers.Count == 0)
        {
            throw new ArgumentException("No point managers enabled.");
        }
	}

	private class TimeManager
	{
		// All in seconds since simulation start
		public double StartTime { get; private set; }
		public double StopTime { get; private set; }
		public double CurrentTime { get; private set; }
		public double ReportTime { get; private set; }
		public double StorageTime { get; private set; }
		public double OutputTime { get; private set; }
		// Time steps
		public double SimulateStep { get; private set; }
		public double ReportStep { get; private set; }
		public double OutputStep { get; private set; }
		public double StorageStep { get; private set; }
		public double dt => SimulateStep;

		public ulong StepCount { get; private set; } = 0;
		public double Duration { get; private set; }
		public ulong StepMax { get; private set; }
		
		public DateTime ReferenceDate { get; private set; }
		public DateTime CurrentDate { get; private set; }
		public DateTime StartDate { get; private set; }
		public DateTime EndDate { get; private set; }
		public TimeSpan StepSpan { get; private set; }

		internal TimeManager(RunOptions configOptions)
		{
			SimulateStep = configOptions.Timesteps.Simulation;
			// How often to add data to the in-memory archive?
			StorageStep = 60.0 * configOptions.Timesteps.Storage;
			// How often to report to the user?
			ReportStep = 60.0 * configOptions.Timesteps.Reporting;
			// How often to write the in-memory archive to disk?
			//double dtOutput = TimeSpan.ParseExact(configOptions.Timesteps.Output,"hhmmss",CultureInfo.InvariantCulture).TotalSeconds;
			OutputStep = RunOptions.ParseHms(configOptions.Timesteps.Output);
            
			// Convert to datetimes
			StartDate = configOptions.Timing.StartDate;
			EndDate = configOptions.Timing.EndDate;
			// Use epoch time
			ReferenceDate = new DateTime(1970, 1, 1, 0, 0, 0, 0);
			CurrentDate = StartDate; // DateTime is a value type so this creates a new copy
			// Absolute times
			double nDays = (EndDate - StartDate).TotalDays; // Days to run
			Duration = 60.0 * 60.0 * 24.0 * nDays; // Simulation duration in seconds
			StartTime = (StartDate - ReferenceDate).TotalSeconds;
			StopTime = StartTime + Duration;
			CurrentTime = StartTime;
			StepCount = 0;
			StepMax = (ulong)Math.Ceiling((StopTime - StartTime)/dt);
			ReportTime = StartTime; // Next time we want output to go to the user
			StorageTime = StartTime + StorageStep; // Next time that we want data to be added to the in-memory archive
			OutputTime = StartTime + OutputStep; // Next time we want the in-memory archive to be written to file
			// Time step expressed as a time delta
			StepSpan = TimeSpan.FromSeconds(dt);
		}

		public void Advance()
		{
			CurrentTime += dt;
			CurrentDate += StepSpan;
			StepCount += 1;
		}
	}
}