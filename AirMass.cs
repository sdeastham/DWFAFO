using Godot;
using System;

public partial class AirMass : Node2D
{
	public ulong UniqueIdentifier {get; private set;}
	public bool Live;
	private bool _dying;
	private GpuParticles2D _particleGen;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		Live = true;
		_dying = false;
		_particleGen = GetNode<GpuParticles2D>("ParticleSpawner");
		_particleGen.Emitting = true;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
	
	public void SetUniqueIdentifier(ulong uid)
	{
		UniqueIdentifier = uid;
	}

	/*
	public override void _Draw()
	{
		DrawRect(new Rect2(-0.5f, -0.5f, 1.0f, 1.0f), Colors.WhiteSmoke);
	}
	*/

	private void OnVisibleOnScreenNotifier2DScreenExited()
	{
		KillNode();
	}
	
	public void KillNode()
	{
		// Do other stuff related to the actual simulation
		_dying = true;
		_particleGen.Emitting = false;
	}

	[Signal]
	public delegate void FinalizeAirMassEventHandler(ulong uid);
	public void Inhume()
	{
		Live = false;
		UniqueIdentifier = 0;
		// Tell Main to remove this point from its dictionary
		EmitSignal(SignalName.FinalizeAirMass, UniqueIdentifier);
		// Tell Godot this node can die
		QueueFree();
	}

	public void UpdatePosition(Vector2 location)
	{
		Position = location;
	}
	
	public void SetProperties(float x, float y, bool randomColor=false)
	{
		_particleGen = GetNode<GpuParticles2D>("ParticleSpawner");
		UpdatePosition(new Vector2(x,y));
		if (!randomColor) { return; }
		// Assign a random color to the point
		// NB: For this to work, you need to set "Local to Scene" in the
		// "Resource" tab for the process material
		// Use a saturation of 1.0 and a value of 1.0, but a random hue
		UpdateColor(Color.FromHsv(GD.Randf(),0.8f,0.9f,0.5f));
	}

	// WARNING: Unless the material is currently local to scene, these will cause all trails to change!
	public void UpdateColor(Color newColor)
	{
		_particleGen.ProcessMaterial.Set("color",newColor);
	}
	
	public void UpdateLifetime(double lifetime, double frequency)
	{
		// Lifetime also affects the frequency of output - because the number of particles is capped
		int cap = (int)Math.Round(frequency * lifetime);
		_particleGen.Set("lifetime",(float)lifetime);
		// Only change this if you want to reduce the pain to the GPU
		//_particleGen.Set("amount",cap);
	}

	public void UpdateSize(double newSize)
	{
		// Scaling over time will be applied with this as the starting value
		_particleGen.ProcessMaterial.Set("scale_min", newSize);
		_particleGen.ProcessMaterial.Set("scale_max", newSize);
	}
}
