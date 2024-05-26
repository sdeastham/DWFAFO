using Godot;
using System;
using DroxtalWolf;

public partial class Hud : CanvasLayer
{
	[Signal]
	public delegate void StartSimulationEventHandler(string configPath);
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// Check if the default file is producing a valid config..
		CheckConfig();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void OnStartButtonPressed()
	{
		GetNode<Button>("BeginSimulationButton").Hide();
		GetNode<Button>("ConfigSelectButton").Hide();
		GetNode<Label>("ConfigPathLabel").Hide();
		string configPath = GetNode<Label>("ConfigPathLabel").Text;
		EmitSignal(SignalName.StartSimulation,ProjectSettings.GlobalizePath(configPath));
	}

	private void OnChooseFileButtonPressed()
	{
		GetNode<FileDialog>("ConfigFileDialog").Show();
	}

	private void OnFileSelection(string filePath)
	{
		GetNode<Label>("ConfigPathLabel").Text = filePath;
		// DWCore needs the real path
		CheckConfig();
	}

	private void CheckConfig()
	{
		string filePath = GetNode<Label>("ConfigPathLabel").Text;
		string globalPath = ProjectSettings.GlobalizePath(filePath);
		// Attempt to read the configuration
		try
		{
			RunOptions runOptions = RunOptions.ReadConfig(globalPath);
			GetNode<Button>("BeginSimulationButton").Disabled = false;
		}
		catch (Exception e)
		{
			GD.Print(e);
			GetNode<Button>("BeginSimulationButton").Disabled = true;
			throw;
		}
	}
}
