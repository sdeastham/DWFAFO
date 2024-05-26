using Godot;
using System;

public partial class AirMass : Node2D
{
	public ulong UniqueIdentifier {get; private set;}
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
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
		DrawRect(new Rect2(-1.0f, -1.0f, 1.0f, 1.0f), Colors.Green);
	}
	*/
	
	private void OnVisibleOnScreenNotifier2DScreenExited()
	{
		KillNode();
	}
	
	private void KillNode()
	{
		// Do other stuff related to the actual simulation
		// Tell Godot this node can die
		QueueFree();
	}
	
	public void SetProperties(float x, float y, float? c=null)
	{
		Position = new Vector2(x,y);
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
		/*
		float rColor = GD.Randf() * 0.5f + 0.5f;
		float gColor = GD.Randf() * 0.5f + 0.5f;
		float bColor = GD.Randf() * 0.5f + 0.5f;
		var newColor = new Color(rColor,gColor,bColor,alpha);
		*/
		particleGen.ProcessMaterial.Set("color",newColor);
	}
}
