using System;
using System.Diagnostics;
using System.Threading.Tasks;
using DroxtalWolf;

namespace GoDots;

public class Simulator
{
	private System.Collections.Generic.Dictionary<string, Stopwatch> Stopwatches;
	private RunOptions ConfigOptions;
	private double[] PressureConstsA;
	private double[] PressureConstsB;
	private DomainManager Domain;
	private MetManager Meteorology;
	
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
	}
	private async Task InitializeSimulator(string configFile)
	{
        ConfigOptions = RunOptions.ReadConfig(configFile);
        
        // Extract and store relevant variables
        bool verbose = ConfigOptions.Verbose;
        bool updateMeteorology = ConfigOptions.TimeDependentMeteorology;

        // Specify the domain
        double[] lonLims = ConfigOptions.Domain.LonLimits;
        double[] latLims = ConfigOptions.Domain.LatLimits;
        double[] pLims   = [ConfigOptions.Domain.PressureBase * 100.0,
            ConfigOptions.Domain.PressureCeiling * 100.0];

        // Major simulation settings
        DateTime startDate = ConfigOptions.Timing.StartDate;
        DateTime endDate = ConfigOptions.Timing.EndDate;
        // Time step in seconds
        double dt = ConfigOptions.Timesteps.Simulation;
        // How often to add data to the in-memory archive?
        double dtStorage = 60.0 * ConfigOptions.Timesteps.Storage;
        // How often to report to the user?
        double dtReport = 60.0 * ConfigOptions.Timesteps.Reporting;
        // How often to write the in-memory archive to disk?
        //double dtOutput = TimeSpan.ParseExact(configOptions.Timesteps.Output,"hhmmss",CultureInfo.InvariantCulture).TotalSeconds;
        double dtOutput = RunOptions.ParseHms(ConfigOptions.Timesteps.Output);
            
        DateTime currentDate = startDate; // DateTime is a value type so this creates a new copy
        
        // Check if the domain manager will need to calculate box heights (expensive)
        bool boxHeightsNeeded = ConfigOptions.PointsFlights is { Active: true, ComplexContrails: true };
        
        // Are we using MERRA-2 or ERA5 data?
        //TODO: Move AP and BP out of here/MERRA-2 into MetManager, then delete MERRA2
        string dataSource = ConfigOptions.InputOutput.MetSource;
        double[] AP, BP;
        bool fixedPressures;
        if (dataSource == "MERRA-2")
        {
            PressureConstsA = MERRA2.AP;
            PressureConstsB = MERRA2.BP;
            fixedPressures = false;
        }
        else if (dataSource == "ERA5")
        {
            PressureConstsA = [ 70.0e2, 100.0e2, 125.0e2, 150.0e2, 175.0e2,
								200.0e2, 225.0e2, 250.0e2, 300.0e2, 350.0e2,
								400.0e2, 450.0e2, 500.0e2, 550.0e2, 600.0e2,
								650.0e2, 700.0e2, 750.0e2, 775.0e2, 800.0e2,
								825.0e2, 850.0e2, 875.0e2, 900.0e2, 925.0e2,
								950.0e2, 975.0e2,1000.0e2];
            Array.Reverse(PressureConstsA); // All data will be flipped internally
            PressureConstsB = new double[PressureConstsA.Length];
            for (int i = 0; i < PressureConstsA.Length; i++)
            {
                PressureConstsB[i] = 0.0;
            }
            fixedPressures = true;
        }
        else
        {
            throw new ArgumentException($"Meteorology data source {dataSource} not recognized.");
        }
        
        // Set up the meteorology and domain
        Meteorology = new MetManager(ConfigOptions.InputOutput.MetDirectory, lonLims, latLims, startDate, 
            ConfigOptions.InputOutput.SerialMetData, Stopwatches, dataSource);
        (double[] lonEdge, double[] latEdge) = Meteorology.GetXYMesh();
        Domain = new DomainManager(lonEdge, latEdge, pLims, PressureConstsA,
	        PressureConstsB, Meteorology, Stopwatches, boxHeightsNeeded, fixedPressures);
	}
}