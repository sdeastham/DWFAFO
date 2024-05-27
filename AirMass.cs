using Godot;
using System;

public partial class AirMass : Node2D
{
	public ulong UniqueIdentifier {get; private set;}
	public bool Live;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		Live = true;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
	
	public void SetUniqueIdentifier(ulong uid)
	{
		UniqueIdentifier = uid;
	}
	
	public override void _Draw()
	{
		DrawRect(new Rect2(-0.5f, -0.5f, 1.0f, 1.0f), Colors.WhiteSmoke);
	}

	private void OnVisibleOnScreenNotifier2DScreenExited()
	{
		KillNode();
	}
	
	public void KillNode()
	{
		// Do other stuff related to the actual simulation
		Live = false;
		UniqueIdentifier = 0;
		// Tell Godot this node can die
		QueueFree();
	}

	public void UpdatePosition(Vector2 location)
	{
		Position = location;
	}
	
	public void SetProperties(float x, float y, float? c=null)
	{
		//Position = new Vector2(x,y);
		UpdatePosition(new Vector2(x,y));
		// Assign a random color to the point
		var particleGen = GetNode<GpuParticles2D>("ParticleSpawner");
		// NB: For this to work, you need to set "Local to Scene" in the
		// "Resource" tab for the process material
		float alpha = 0.5f;
		// Use a saturation of 1.0 and a value of 1.0, no matter what. The input
		// defines only the "hue"
		float hue;
		if (c == null)
		{
			hue = GD.Randf();
		}
		else
		{
			hue = (float)c;
		}
		var newColor = Color.FromHsv(hue,0.8f,0.9f,alpha);
		particleGen.ProcessMaterial.Set("color",newColor);
	}
}
